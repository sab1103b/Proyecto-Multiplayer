using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CuboVR.Networking
{
    /// <summary>
    /// Sincroniza las caras del cubo entre todos los jugadores.
    ///  - Los trazos NO se envían como píxeles: cada dispositivo tiene sus propias Draw Cameras y, como la
    ///    posición de los marcadores/borrador se replica (NetworkTransform), cada uno dibuja lo mismo localmente.
    ///  - "Borrar cara" se envía por RPC a todos.
    ///  - Quien entra tarde recibe una foto (PNG) de cada cara, en trozos pequeños.
    /// Va en un objeto de escena con NetworkObject (Network Systems).
    /// </summary>
    public class NetworkCanvasSync : NetworkBehaviour
    {
        public static NetworkCanvasSync Instance { get; private set; }

        [Tooltip("Las caras de dibujo del cubo (CanvasManager), en el mismo orden en todos los dispositivos.")]
        public CanvasManager[] faces;

        [Tooltip("Tamaño de cada trozo de imagen enviado al entrar tarde (debe ser menor que Max Payload Size del transporte, 6144).")]
        public int chunkSize = 4096;
        [Tooltip("Trozos enviados por frame (para no saturar la red).")]
        public int chunksPerFrame = 8;

        readonly Dictionary<int, byte[]> incoming = new Dictionary<int, byte[]>();

        public bool IsReady => IsSpawned && NetworkManager != null && NetworkManager.IsListening;

        void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // El cliente que acaba de entrar pide el estado actual del pizarrón.
            if (!IsServer) RequestSnapshotRpc();
        }

        // ---------------- Borrar ----------------
        /// <summary>Devuelve false si la cara no está registrada (entonces se borra solo local).</summary>
        public bool RequestClear(CanvasManager face)
        {
            int index = Array.IndexOf(faces, face);
            if (index < 0) return false;
            ClearFaceServerRpc(index);
            return true;
        }

        [Rpc(SendTo.Server)]
        void ClearFaceServerRpc(int faceIndex)
        {
            ClearFaceEveryoneRpc(faceIndex);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ClearFaceEveryoneRpc(int faceIndex)
        {
            if (faceIndex >= 0 && faceIndex < faces.Length && faces[faceIndex] != null)
                faces[faceIndex].ClearLocal();
        }

        // ---------------- Foto para quien entra tarde ----------------
        [Rpc(SendTo.Server)]
        void RequestSnapshotRpc(RpcParams rpcParams = default)
        {
            StartCoroutine(SendSnapshot(rpcParams.Receive.SenderClientId));
        }

        IEnumerator SendSnapshot(ulong clientId)
        {
            yield return new WaitForEndOfFrame();
            for (int f = 0; f < faces.Length; f++)
            {
                if (faces[f] == null) continue;
                byte[] png = faces[f].EncodeToPNG();
                if (png == null) continue;
                yield return SendFace(f, png, clientId);
            }
        }

        /// <summary>Manda una cara a un cliente (clientId) o a todos los que no son el servidor (null).</summary>
        IEnumerator SendFace(int f, byte[] png, ulong? clientId)
        {
            int total = png.Length;
            int sentThisFrame = 0;
            for (int offset = 0; offset < total; offset += chunkSize)
            {
                if (!IsServer) yield break;
                if (clientId.HasValue && !NetworkManager.ConnectedClients.ContainsKey(clientId.Value)) yield break;
                int len = Mathf.Min(chunkSize, total - offset);
                byte[] chunk = new byte[len];
                Buffer.BlockCopy(png, offset, chunk, 0, len);
                if (clientId.HasValue) SnapshotChunkRpc(f, total, offset, chunk, RpcTarget.Single(clientId.Value, RpcTargetUse.Temp));
                else SnapshotChunkRpc(f, total, offset, chunk, RpcTarget.NotServer);

                if (++sentThisFrame >= chunksPerFrame)
                {
                    sentThisFrame = 0;
                    yield return null;
                }
            }
        }

        // ---------------- Imagen importada por el profesor ----------------
        /// <summary>El profesor cargó una imagen en esta cara: se la manda a todos los estudiantes.</summary>
        public void EnviarCaraATodos(CanvasManager face)
        {
            int index = Array.IndexOf(faces, face);
            if (!IsServer || index < 0) return;
            byte[] png = face.EncodeToPNG();
            if (png != null) StartCoroutine(SendFace(index, png, null));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SnapshotChunkRpc(int faceIndex, int totalLength, int offset, byte[] data, RpcParams rpcParams)
        {
            if (faceIndex < 0 || faceIndex >= faces.Length || faces[faceIndex] == null) return;

            if (offset == 0 || !incoming.TryGetValue(faceIndex, out var buffer) || buffer.Length != totalLength)
            {
                buffer = new byte[totalLength];
                incoming[faceIndex] = buffer;
            }
            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);

            // Los RPC llegan en orden: el último trozo completa la imagen.
            if (offset + data.Length >= totalLength)
            {
                incoming.Remove(faceIndex);
                faces[faceIndex].LoadFromPNG(buffer);
            }
        }
    }
}
