using System;
using System.Collections;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CuboVR.Networking
{
    public enum ConnectionState
    {
        EnLobby,
        CreandoSala,
        BuscandoSala,
        Conectando,
        EnSala,
    }

    /// <summary>
    /// Punto único para crear y unirse a salas.
    ///  - Profesor: CreateRoom() → inicia Host escuchando en toda la red (0.0.0.0) y anuncia la sala por LAN.
    ///  - Estudiante: JoinRoom(sala) o JoinByCode("1234") → se conecta como Cliente a la IP del profesor.
    /// Un profesor por sala: el host es siempre el profesor y el servidor rechaza a quien no sea estudiante.
    /// </summary>
    [RequireComponent(typeof(RoomDiscovery))]
    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        const string PayloadPrefix = "CUBOVR-v1";

        [Header("Red")]
        [Tooltip("Puerto UDP del juego. Debe estar permitido en el firewall del PC del profesor.")]
        public ushort gamePort = 7777;
        [Tooltip("Máximo de estudiantes por sala.")]
        public int maxStudents = 20;
        [Tooltip("Nombre visible de la sala. Vacío = 'Sala de <dispositivo>'.")]
        public string roomName = "";
        [Tooltip("Segundos que se espera escuchar una sala al escribir su código.")]
        public float codeSearchTimeout = 4f;

        public ConnectionState State { get; private set; } = ConnectionState.EnLobby;
        public string CurrentRoomCode { get; private set; } = "";
        public string LastMessage { get; private set; } = "";
        public RoomDiscovery Discovery { get; private set; }

        /// <summary>(estado, mensaje para mostrar al usuario)</summary>
        public event Action<ConnectionState, string> StateChanged;

        NetworkManager nm;
        UnityTransport transport;
        int nextStudentSeat;
        Coroutine codeSearch;
        bool callbacksHooked;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Discovery = GetComponent<RoomDiscovery>();
        }

        void Start()
        {
            nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[CuboVR] No hay NetworkManager en la escena.");
                return;
            }
            transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null) transport = nm.GetComponent<UnityTransport>();
            HookCallbacks();
        }

        void HookCallbacks()
        {
            if (callbacksHooked || nm == null) return;
            nm.ConnectionApprovalCallback += ApproveConnection;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnTransportFailure += OnTransportFailure;
            callbacksHooked = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (callbacksHooked && nm != null)
            {
                nm.ConnectionApprovalCallback -= ApproveConnection;
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
                nm.OnTransportFailure -= OnTransportFailure;
            }
        }

        // =====================================================================
        //  PROFESOR
        // =====================================================================
        [Tooltip("Si el puerto está ocupado se prueban los siguientes (7778, 7779…). Los estudiantes reciben el puerto real con el anuncio de la sala.")]
        public int portAttempts = 10;

        public void CreateRoom()
        {
            if (!CanStart()) return;

            CancelCodeSearch();
            Discovery.StopBrowsing();
            StartCoroutine(CreateRoomRoutine());
        }

        IEnumerator CreateRoomRoutine()
        {
            CurrentRoomCode = GenerateCode();
            nm.NetworkConfig.ConnectionApproval = true;
            SetState(ConnectionState.CreandoSala, "Creando sala…");
            nextStudentSeat = 0;

            ushort port = 0;
            for (int i = 0; i < Mathf.Max(1, portAttempts); i++)
            {
                // Tras un intento fallido Netcode se apaga solo; hay que esperar a que termine.
                while (nm.ShutdownInProgress) yield return null;

                ushort candidate = (ushort)(gamePort + i);
                // El host se conecta a sí mismo por 127.0.0.1 pero escucha en todas las interfaces.
                transport.SetConnectionData("127.0.0.1", candidate, "0.0.0.0");
                if (nm.StartHost()) { port = candidate; break; }
                Debug.LogWarning($"[CuboVR] El puerto {candidate} está ocupado, probando el siguiente…");
                yield return null;
            }

            if (port == 0)
            {
                SetState(ConnectionState.EnLobby,
                    $"No se pudo crear la sala: los puertos {gamePort}–{gamePort + portAttempts - 1} están ocupados. Cierra otras copias de la app o reinicia Unity.");
                yield break;
            }

            string name = string.IsNullOrWhiteSpace(roomName) ? $"Sala de {SystemInfo.deviceName}" : roomName;
            Discovery.StartAdvertising(new RoomInfo
            {
                code = CurrentRoomCode,
                name = name,
                port = port,
                players = 1,
                maxPlayers = maxStudents + 1,
            });
            SetState(ConnectionState.EnSala, port == gamePort
                ? $"Sala creada. Código: {CurrentRoomCode}"
                : $"Sala creada (puerto {port}). Código: {CurrentRoomCode}");
        }

        // =====================================================================
        //  ESTUDIANTE
        // =====================================================================
        public void StartBrowsing() => Discovery.StartBrowsing();
        public void StopBrowsing() => Discovery.StopBrowsing();

        public void JoinRoom(RoomInfo room)
        {
            if (room == null || !CanStart()) return;
            if (room.IsFull)
            {
                SetState(ConnectionState.EnLobby, $"La sala {room.code} está llena.");
                return;
            }

            CancelCodeSearch();
            CurrentRoomCode = room.code;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes($"{PayloadPrefix}|{(byte)PlayerRole.Estudiante}|{room.code}");
            transport.SetConnectionData(room.address, room.port);

            SetState(ConnectionState.Conectando, $"Conectando a la sala {room.code}…");
            if (!nm.StartClient())
                SetState(ConnectionState.EnLobby, "No se pudo iniciar la conexión.");
        }

        public void JoinByCode(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0)
            {
                SetState(ConnectionState.EnLobby, "Escribe el código de la sala.");
                return;
            }
            if (!CanStart()) return;

            var room = Discovery.FindByCode(code);
            if (room != null) { JoinRoom(room); return; }

            CancelCodeSearch();
            codeSearch = StartCoroutine(SearchCodeRoutine(code));
        }

        IEnumerator SearchCodeRoutine(string code)
        {
            Discovery.StartBrowsing();
            SetState(ConnectionState.BuscandoSala, $"Buscando la sala {code}…");
            float end = Time.unscaledTime + codeSearchTimeout;
            while (Time.unscaledTime < end)
            {
                var room = Discovery.FindByCode(code);
                if (room != null)
                {
                    codeSearch = null;
                    JoinRoom(room);
                    yield break;
                }
                yield return null;
            }
            codeSearch = null;
            SetState(ConnectionState.EnLobby, $"No se encontró la sala {code}. Revisa el código y que estés en la misma red Wi-Fi que el profesor.");
        }

        void CancelCodeSearch()
        {
            if (codeSearch != null) StopCoroutine(codeSearch);
            codeSearch = null;
        }

        // =====================================================================
        //  SALIR
        // =====================================================================
        public void LeaveRoom()
        {
            CancelCodeSearch();
            Discovery.StopAdvertising();
            if (nm != null && (nm.IsListening || nm.IsClient || nm.IsServer)) nm.Shutdown();
            ReturnToLobby("Saliste de la sala.");
        }

        void ReturnToLobby(string message)
        {
            Discovery.StopAdvertising();
            CurrentRoomCode = "";
            if (NetworkSpawnPoints.Instance != null) NetworkSpawnPoints.Instance.TeleportToLobby();
            SetState(ConnectionState.EnLobby, message);
        }

        // =====================================================================
        //  CALLBACKS DE NETCODE
        // =====================================================================
        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = true;
            response.Pending = false;

            // El host (profesor) se aprueba a sí mismo.
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }

            string payload = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : "";
            string[] parts = payload.Split('|');
            if (parts.Length < 3 || parts[0] != PayloadPrefix)
            {
                Reject(response, "Versión de la app distinta a la del profesor.");
                return;
            }
            if (parts[1] != ((byte)PlayerRole.Estudiante).ToString())
            {
                Reject(response, "Esta sala ya tiene profesor.");
                return;
            }
            if (parts[2] != CurrentRoomCode)
            {
                Reject(response, "El código de sala no coincide.");
                return;
            }
            // ConnectedClientsIds incluye al host.
            if (nm.ConnectedClientsIds.Count - 1 >= maxStudents)
            {
                Reject(response, "La sala está llena.");
                return;
            }
            response.Approved = true;
        }

        static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = reason;
        }

        void OnClientConnected(ulong clientId)
        {
            if (nm.IsServer)
            {
                Discovery.UpdateAdvertisedPlayers(nm.ConnectedClientsIds.Count);
                return;
            }
            if (clientId == nm.LocalClientId)
                SetState(ConnectionState.EnSala, $"Conectado a la sala {CurrentRoomCode}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (nm.IsServer && clientId != NetworkManager.ServerClientId)
            {
                // Un estudiante salió; ConnectedClientsIds todavía puede incluirlo en este frame.
                StartCoroutine(UpdatePlayerCountNextFrame());
                return;
            }

            // Ya volvimos al lobby (salida voluntaria) o se está probando otro puerto: no pisar el mensaje.
            if (State == ConnectionState.EnLobby || State == ConnectionState.CreandoSala) return;

            // Somos cliente y nos desconectaron (profesor cerró la sala, rechazo o tiempo agotado).
            if (!nm.IsServer)
            {
                string reason = nm.DisconnectReason;
                if (string.IsNullOrEmpty(reason))
                    reason = State == ConnectionState.Conectando
                        ? "No se pudo conectar con el profesor (¿misma red? ¿firewall?)."
                        : "El profesor cerró la sala.";
                ReturnToLobby(reason);
            }
        }

        IEnumerator UpdatePlayerCountNextFrame()
        {
            yield return null;
            if (nm.IsServer) Discovery.UpdateAdvertisedPlayers(nm.ConnectedClientsIds.Count);
        }

        void OnTransportFailure()
        {
            // Mientras se crea la sala, un fallo solo significa "puerto ocupado": CreateRoomRoutine prueba otro.
            if (State == ConnectionState.CreandoSala) return;
            ReturnToLobby("Se perdió la conexión de red.");
        }

        // Al salir de Play (o cerrar la app) se cierra la red para no dejar el puerto ocupado.
        void OnApplicationQuit()
        {
            Discovery.StopAdvertising();
            Discovery.StopBrowsing();
            if (nm != null && nm.IsListening) nm.Shutdown(true);
        }

        /// <summary>El servidor reparte los lugares fuera del cubo entre los estudiantes.</summary>
        public int AssignStudentSeat() => nextStudentSeat++;

        // =====================================================================
        //  UTILIDADES
        // =====================================================================
        bool CanStart()
        {
            if (nm == null || transport == null)
            {
                SetState(ConnectionState.EnLobby, "Falta el NetworkManager o el Unity Transport en la escena.");
                return false;
            }
            if (nm.ShutdownInProgress)
            {
                SetState(ConnectionState.EnLobby, "Cerrando la conexión anterior, intenta de nuevo en un momento.");
                return false;
            }
            if (nm.IsListening || State == ConnectionState.Conectando || State == ConnectionState.CreandoSala)
            {
                Debug.LogWarning("[CuboVR] Ya hay una sesión activa; sal de la sala antes de crear o unirte a otra.");
                return false;
            }
            return true;
        }

        string GenerateCode()
        {
            // 4 dígitos: fácil de escribir con el teclado numérico en VR.
            for (int i = 0; i < 20; i++)
            {
                string c = UnityEngine.Random.Range(1000, 10000).ToString();
                if (Discovery.FindByCode(c) == null) return c;
            }
            return UnityEngine.Random.Range(1000, 10000).ToString();
        }

        void SetState(ConnectionState s, string message)
        {
            State = s;
            LastMessage = message;
            Debug.Log($"[CuboVR] {s}: {message}");
            StateChanged?.Invoke(s, message);
        }
    }
}
