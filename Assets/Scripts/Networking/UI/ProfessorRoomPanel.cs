using UnityEngine;
using UnityEngine.UI;

namespace CuboVR.Networking.UI
{
    /// <summary>Panel de la zona Profesor: un botón para crear la sala y un texto de estado.</summary>
    public class ProfessorRoomPanel : MonoBehaviour
    {
        public Button createButton;
        public Text statusText;

        void OnEnable()
        {
            if (createButton != null) createButton.onClick.AddListener(OnCreate);
            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.StateChanged += OnState;
                OnState(ConnectionManager.Instance.State, "");
            }
            if (statusText != null) statusText.text = "Al crear la sala entrarás al cubo como profesor.";
        }

        void OnDisable()
        {
            if (createButton != null) createButton.onClick.RemoveListener(OnCreate);
            if (ConnectionManager.Instance != null) ConnectionManager.Instance.StateChanged -= OnState;
        }

        void OnCreate()
        {
            if (ConnectionManager.Instance == null)
            {
                if (statusText != null) statusText.text = "Falta el ConnectionManager en la escena.";
                return;
            }
            ConnectionManager.Instance.CreateRoom();
        }

        void OnState(ConnectionState state, string message)
        {
            if (createButton != null) createButton.interactable = state == ConnectionState.EnLobby;
            if (statusText != null && !string.IsNullOrEmpty(message)) statusText.text = message;
        }
    }
}
