using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CuboVR.Networking
{
    /// <summary>
    /// Para objetos agarrables compartidos (marcadores, borrador, figuras).
    /// Requiere NetworkObject + NetworkTransform con Authority Mode = Owner.
    ///  - Al agarrarlo, quien lo agarra pide ser dueño → su movimiento se replica a todos.
    ///  - En los demás clientes el Rigidbody queda cinemático para que la física no pelee con la red.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class NetworkGrabOwnership : NetworkBehaviour
    {
        XRGrabInteractable grab;
        Rigidbody rb;
        bool originalKinematic;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            rb = GetComponent<Rigidbody>();
            if (rb != null) originalKinematic = rb.isKinematic;
        }

        void OnEnable() => grab.selectEntered.AddListener(OnGrabbed);
        void OnDisable() => grab.selectEntered.RemoveListener(OnGrabbed);

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (!IsSpawned || IsOwner) return;
            RequestOwnershipRpc();
        }

        [Rpc(SendTo.Server)]
        void RequestOwnershipRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (NetworkObject.OwnerClientId != sender) NetworkObject.ChangeOwnership(sender);
        }

        void LateUpdate()
        {
            if (rb == null || !IsSpawned) return;
            // Mientras está agarrado, XRI controla el Rigidbody.
            if (grab.isSelected && IsOwner) return;
            bool desired = IsOwner ? originalKinematic : true;
            if (rb.isKinematic != desired) rb.isKinematic = desired;
        }

        public override void OnNetworkDespawn()
        {
            if (rb != null) rb.isKinematic = originalKinematic;
            base.OnNetworkDespawn();
        }
    }
}
