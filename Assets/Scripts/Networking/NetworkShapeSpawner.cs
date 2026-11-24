using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CuboVR.Networking
{
    /// <summary>
    /// Crea las figuras 3D en red. El cliente pide la figura → el servidor la instancia y la "spawnea"
    /// con el cliente como dueño → el cliente la recibe y la pone en su mano.
    /// Va en un objeto de escena con NetworkObject (Network Systems).
    /// </summary>
    public class NetworkShapeSpawner : NetworkBehaviour
    {
        public static NetworkShapeSpawner Instance { get; private set; }

        [Tooltip("Spawner local que tiene la lista de prefabs de figuras.")]
        public GeometricShapeSpawner shapeSpawner;

        int nextRequestId;
        readonly Dictionary<int, Action<GameObject>> pending = new Dictionary<int, Action<GameObject>>();

        void Awake()
        {
            Instance = this;
            if (shapeSpawner == null) shapeSpawner = FindFirstObjectByType<GeometricShapeSpawner>();
        }

        public bool IsReady => IsSpawned && NetworkManager != null && NetworkManager.IsListening;

        /// <summary>Pide una figura; onSpawned se llama en este cliente cuando ya existe en red.</summary>
        public void RequestShape(int shapeIndex, Vector3 position, Quaternion rotation, Action<GameObject> onSpawned)
        {
            int id = ++nextRequestId;
            pending[id] = onSpawned;
            SpawnShapeRpc(shapeIndex, position, rotation, id);
        }

        [Rpc(SendTo.Server)]
        void SpawnShapeRpc(int shapeIndex, Vector3 position, Quaternion rotation, int requestId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var prefab = shapeSpawner != null ? shapeSpawner.GetShapePrefab(shapeIndex) : null;
            if (prefab == null)
            {
                Debug.LogWarning($"[CuboVR] Figura {shapeIndex} no válida.");
                return;
            }

            var instance = Instantiate(prefab.gameObject, position, rotation);
            var netObj = instance.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[CuboVR] El prefab '{prefab.name}' no tiene NetworkObject. Ejecuta 'Cubo VR > Configurar multijugador'.");
                Destroy(instance);
                return;
            }
            netObj.SpawnWithOwnership(sender);
            ShapeSpawnedRpc(netObj.NetworkObjectId, requestId, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ShapeSpawnedRpc(ulong networkObjectId, int requestId, RpcParams rpcParams)
        {
            if (!pending.TryGetValue(requestId, out var callback)) return;
            pending.Remove(requestId);
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var obj))
                callback?.Invoke(obj.gameObject);
        }
    }
}
