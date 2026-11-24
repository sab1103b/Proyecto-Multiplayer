using CuboVR.Networking;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

/// <summary>
/// Botones antiguos "Create"/"Join". Ahora pasan por el ConnectionManager (sala LAN con código):
///  - Host  → crea una sala como profesor.
///  - Client → se une a la primera sala encontrada en la red.
/// Si no hay ConnectionManager en la escena, se comporta como antes (solo local).
/// </summary>
public class NetwrokUI : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;

    void Start()
    {
        if (hostButton != null) hostButton.onClick.AddListener(OnHost);
        if (clientButton != null) clientButton.onClick.AddListener(OnClient);
    }

    void OnHost()
    {
        if (ConnectionManager.Instance != null) ConnectionManager.Instance.CreateRoom();
        else NetworkManager.Singleton.StartHost();
    }

    void OnClient()
    {
        var cm = ConnectionManager.Instance;
        if (cm == null) { NetworkManager.Singleton.StartClient(); return; }

        cm.StartBrowsing();
        foreach (var room in cm.Discovery.Rooms)
        {
            if (!room.IsFull) { cm.JoinRoom(room); return; }
        }
        Debug.Log("[CuboVR] Aún no se encontró ninguna sala; usa la zona de Estudiante del lobby para elegir una.");
    }
}
