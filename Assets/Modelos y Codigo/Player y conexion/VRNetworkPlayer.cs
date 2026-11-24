using System;
using System.Collections.Generic;
using CuboVR.Networking;
using Unity.Netcode;
using UnityEngine;

public class VRNetworkPlayer : NetworkBehaviour
{
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    public List<Renderer> localRendererToHide;

    /// <summary>Rol asignado por el servidor (host = Profesor, el resto = Estudiante).</summary>
    public readonly NetworkVariable<PlayerRole> Role = new NetworkVariable<PlayerRole>(PlayerRole.Ninguno);
    /// <summary>Lugar asignado fuera del cubo (solo estudiantes).</summary>
    public readonly NetworkVariable<int> SeatIndex = new NetworkVariable<int>(0);

    /// <summary>El jugador de este dispositivo (null si no hay sala).</summary>
    public static VRNetworkPlayer Local { get; private set; }
    /// <summary>Se dispara cuando el jugador local recibe su rol (para activar/desactivar herramientas).</summary>
    public static event Action<PlayerRole> LocalRoleChanged;

    bool placed;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            bool isProfesor = OwnerClientId == NetworkManager.ServerClientId;
            Role.Value = isProfesor ? PlayerRole.Profesor : PlayerRole.Estudiante;
            if (!isProfesor && ConnectionManager.Instance != null)
                SeatIndex.Value = ConnectionManager.Instance.AssignStudentSeat();
        }

        if (IsOwner)
        {
            Local = this;
            foreach (var item in localRendererToHide)
            {
                if (item != null) item.enabled = false;
            }
            Role.OnValueChanged += OnRoleChanged;
            if (Role.Value != PlayerRole.Ninguno) OnRoleChanged(PlayerRole.Ninguno, Role.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            Role.OnValueChanged -= OnRoleChanged;
            if (Local == this) Local = null;
        }
        base.OnNetworkDespawn();
    }

    void OnRoleChanged(PlayerRole previous, PlayerRole current)
    {
        if (current == PlayerRole.Ninguno) return;

        // Primera vez: llevar al jugador a su lugar (profesor dentro del cubo, estudiante fuera).
        if (!placed && NetworkSpawnPoints.Instance != null)
        {
            placed = true;
            NetworkSpawnPoints.TeleportLocalPlayer(NetworkSpawnPoints.Instance.GetSpawn(current, SeatIndex.Value));
        }
        LocalRoleChanged?.Invoke(current);
    }

    void Update()
    {
        if (IsOwner && VRRigReferences.Instance != null)
        {
            head.transform.position = VRRigReferences.Instance.head.position;
            head.transform.rotation = VRRigReferences.Instance.head.rotation;

            leftHand.transform.position = VRRigReferences.Instance.leftHand.position;
            leftHand.transform.rotation = VRRigReferences.Instance.leftHand.rotation;

            rightHand.transform.position = VRRigReferences.Instance.rightHand.position;
            rightHand.transform.rotation = VRRigReferences.Instance.rightHand.rotation;
        }
    }
}
