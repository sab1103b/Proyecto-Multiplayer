using UnityEngine;
using UnityEngine.UI;

namespace CuboVR.Networking.UI
{
    /// <summary>
    /// Panel pequeño visible dentro de la sala: muestra el código (para dictárselo a los estudiantes),
    /// el rol y un botón para salir y volver al lobby.
    /// </summary>
    public class RoomStatusPanel : MonoBehaviour
    {
        [Tooltip("Contenido que se oculta cuando no hay sala.")]
        public GameObject content;
        public Text codeText;
        public Text roleText;
        public Button leaveButton;

        void OnEnable()
        {
            if (leaveButton != null) leaveButton.onClick.AddListener(Leave);
            VRNetworkPlayer.LocalRoleChanged += OnRole;
        }

        void Start()
        {
            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.StateChanged += OnState;
            Refresh();
        }

        void OnDisable()
        {
            if (leaveButton != null) leaveButton.onClick.RemoveListener(Leave);
            VRNetworkPlayer.LocalRoleChanged -= OnRole;
        }

        void OnDestroy()
        {
            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.StateChanged -= OnState;
        }

        void Leave() => ConnectionManager.Instance?.LeaveRoom();

        void OnState(ConnectionState s, string m) => Refresh();
        void OnRole(PlayerRole r) => Refresh();

        void Refresh()
        {
            var cm = ConnectionManager.Instance;
            bool inRoom = cm != null && cm.State == ConnectionState.EnSala;
            if (content != null) content.SetActive(inRoom);
            if (!inRoom) return;

            if (codeText != null) codeText.text = "Sala " + cm.CurrentRoomCode;
            if (roleText != null)
            {
                var local = VRNetworkPlayer.Local;
                var role = local != null ? local.Role.Value : PlayerRole.Ninguno;
                roleText.text = role == PlayerRole.Profesor ? "Profesor"
                              : role == PlayerRole.Estudiante ? "Estudiante" : "";
            }
        }
    }
}
