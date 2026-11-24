using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace CuboVR.Networking
{
    /// <summary>Información de una sala anunciada en la red local.</summary>
    [Serializable]
    public class RoomInfo
    {
        public string code;
        public string name;
        public string address;
        public ushort port;
        public int players;
        public int maxPlayers;
        public float lastSeen;

        public bool IsFull => players >= maxPlayers;
    }

    /// <summary>
    /// Descubrimiento de salas en LAN por broadcast UDP.
    /// - El profesor (host) anuncia su sala cada segundo.
    /// - Los estudiantes escuchan y mantienen la lista de salas visibles.
    /// No necesita internet ni cuentas: solo que todos estén en la misma red Wi-Fi.
    /// </summary>
    public class RoomDiscovery : MonoBehaviour
    {
        const string Header = "CUBOVR";
        const int ProtocolVersion = 1;

        [Tooltip("Puerto UDP para anunciar/buscar salas (distinto al puerto del juego).")]
        public int discoveryPort = 47777;
        [Tooltip("Segundos entre anuncios del host.")]
        public float broadcastInterval = 1f;
        [Tooltip("Una sala desaparece de la lista si no se escucha en este tiempo.")]
        public float roomTimeout = 4f;

        public event Action RoomsChanged;

        readonly Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>();
        public IReadOnlyCollection<RoomInfo> Rooms => rooms.Values;

        UdpClient sender;
        UdpClient listener;
        RoomInfo advertised;
        float nextBroadcast;
        List<IPEndPoint> broadcastTargets;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject multicastLock;
#endif

        public bool IsAdvertising => advertised != null;
        public bool IsBrowsing => listener != null;

        // ---------------- HOST: anunciar ----------------
        public void StartAdvertising(RoomInfo room)
        {
            StopAdvertising();
            advertised = room;
            try
            {
                sender = new UdpClient();
                sender.EnableBroadcast = true;
                broadcastTargets = GetBroadcastTargets();
                nextBroadcast = 0f;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CuboVR] No se pudo iniciar el anuncio de sala: {e.Message}");
                advertised = null;
            }
        }

        public void UpdateAdvertisedPlayers(int players)
        {
            if (advertised != null) advertised.players = players;
        }

        public void StopAdvertising()
        {
            advertised = null;
            sender?.Close();
            sender = null;
        }

        // ---------------- CLIENTE: buscar ----------------
        public void StartBrowsing()
        {
            if (listener != null) return;
            rooms.Clear();
            try
            {
                AcquireMulticastLock();
                listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.EnableBroadcast = true;
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            }
            catch (Exception e)
            {
                Debug.LogError($"[CuboVR] No se pudo escuchar salas en el puerto {discoveryPort}: {e.Message}");
                listener?.Close();
                listener = null;
            }
            RoomsChanged?.Invoke();
        }

        public void StopBrowsing()
        {
            listener?.Close();
            listener = null;
            ReleaseMulticastLock();
            if (rooms.Count > 0) { rooms.Clear(); RoomsChanged?.Invoke(); }
        }

        public RoomInfo FindByCode(string code)
        {
            return rooms.TryGetValue(code, out var r) ? r : null;
        }

        // ---------------- Bucle ----------------
        void Update()
        {
            if (advertised != null && sender != null && Time.unscaledTime >= nextBroadcast)
            {
                nextBroadcast = Time.unscaledTime + broadcastInterval;
                Broadcast();
            }

            if (listener != null) Receive();
        }

        void Broadcast()
        {
            string msg = string.Join("|", Header, ProtocolVersion, advertised.code, Sanitize(advertised.name),
                advertised.port, advertised.players, advertised.maxPlayers);
            byte[] data = Encoding.UTF8.GetBytes(msg);
            foreach (var target in broadcastTargets)
            {
                try { sender.Send(data, data.Length, target); }
                catch (Exception) { /* una interfaz caída no debe detener las demás */ }
            }
        }

        void Receive()
        {
            bool changed = false;
            try
            {
                while (listener.Available > 0)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = listener.Receive(ref from);
                    var room = Parse(Encoding.UTF8.GetString(data), from.Address.ToString());
                    if (room == null) continue;
                    if (!rooms.TryGetValue(room.code, out var existing) ||
                        existing.players != room.players || existing.address != room.address || existing.name != room.name)
                        changed = true;
                    rooms[room.code] = room;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CuboVR] Error leyendo anuncios de sala: {e.Message}");
            }

            // Quitar salas que dejaron de anunciarse
            List<string> expired = null;
            foreach (var kv in rooms)
                if (Time.unscaledTime - kv.Value.lastSeen > roomTimeout)
                    (expired ??= new List<string>()).Add(kv.Key);
            if (expired != null) { foreach (var k in expired) rooms.Remove(k); changed = true; }

            if (changed) RoomsChanged?.Invoke();
        }

        RoomInfo Parse(string msg, string address)
        {
            var p = msg.Split('|');
            if (p.Length < 7 || p[0] != Header) return null;
            if (!int.TryParse(p[1], out int ver) || ver != ProtocolVersion) return null;
            if (!ushort.TryParse(p[4], out ushort port)) return null;
            int.TryParse(p[5], out int players);
            int.TryParse(p[6], out int max);
            return new RoomInfo
            {
                code = p[2], name = p[3], address = address, port = port,
                players = players, maxPlayers = max, lastSeen = Time.unscaledTime
            };
        }

        static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "Sala" : s.Replace("|", "/");

        /// <summary>255.255.255.255 + la dirección de broadcast de cada interfaz (Windows solo envía el
        /// broadcast global por una interfaz, así que también se envía a cada subred).</summary>
        List<IPEndPoint> GetBroadcastTargets()
        {
            var list = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, discoveryPort) };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] ip = ua.Address.GetAddressBytes();
                        byte[] mask = ua.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4) continue;
                        var bc = new byte[4];
                        for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                        var ep = new IPEndPoint(new IPAddress(bc), discoveryPort);
                        if (!list.Exists(e => e.Equals(ep))) list.Add(ep);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CuboVR] No se pudieron listar interfaces de red, se usa solo broadcast global: {e.Message}");
            }
            // Para pruebas en el mismo PC (Editor + build)
            list.Add(new IPEndPoint(IPAddress.Loopback, discoveryPort));
            return list;
        }

        // Android (Quest) filtra paquetes broadcast si no se pide un MulticastLock.
        void AcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "CuboVRDiscovery");
                multicastLock.Call("acquire");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CuboVR] No se obtuvo MulticastLock (puede que no se vean salas en el visor): {e.Message}");
            }
#endif
        }

        void ReleaseMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { multicastLock?.Call("release"); } catch (Exception) { }
            multicastLock?.Dispose();
            multicastLock = null;
#endif
        }

        void OnDestroy()
        {
            StopAdvertising();
            StopBrowsing();
        }
    }
}
