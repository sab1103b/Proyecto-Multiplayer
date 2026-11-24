using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CuboVR.Networking.UI
{
    /// <summary>
    /// Panel de la zona Estudiante:
    ///  - Lista de salas encontradas en la red (un botón por sala).
    ///  - Teclado numérico para escribir el código de la sala y unirse.
    /// Busca salas mientras el panel está visible.
    /// </summary>
    public class StudentRoomBrowser : MonoBehaviour
    {
        [Header("Lista de salas")]
        [Tooltip("Contenedor donde se crean los botones de sala.")]
        public Transform roomListContainer;
        [Tooltip("Botón plantilla (desactivado). Debe tener un Text hijo.")]
        public Button roomButtonTemplate;
        public Text emptyListText;
        public int maxRoomsShown = 4;

        [Header("Código")]
        public Text codeText;
        [Tooltip("Botones 0-9 en orden (índice = dígito).")]
        public Button[] digitButtons = new Button[10];
        public Button deleteButton;
        public Button joinButton;
        public int codeLength = 4;

        [Header("Estado")]
        public Text statusText;

        readonly List<Button> roomButtons = new List<Button>();
        string code = "";

        void OnEnable()
        {
            for (int i = 0; i < digitButtons.Length; i++)
            {
                if (digitButtons[i] == null) continue;
                int digit = i;
                digitButtons[i].onClick.AddListener(() => AddDigit(digit));
            }
            if (deleteButton != null) deleteButton.onClick.AddListener(DeleteDigit);
            if (joinButton != null) joinButton.onClick.AddListener(JoinWithCode);
            if (roomButtonTemplate != null) roomButtonTemplate.gameObject.SetActive(false);

            var cm = ConnectionManager.Instance;
            if (cm != null)
            {
                cm.StateChanged += OnState;
                cm.Discovery.RoomsChanged += RefreshList;
                cm.StartBrowsing();
            }
            SetStatus("Elige una sala o escribe su código.");
            code = "";
            RefreshCode();
            RefreshList();
        }

        void OnDisable()
        {
            foreach (var b in digitButtons) if (b != null) b.onClick.RemoveAllListeners();
            if (deleteButton != null) deleteButton.onClick.RemoveListener(DeleteDigit);
            if (joinButton != null) joinButton.onClick.RemoveListener(JoinWithCode);

            var cm = ConnectionManager.Instance;
            if (cm != null)
            {
                cm.StateChanged -= OnState;
                cm.Discovery.RoomsChanged -= RefreshList;
                // Seguir escuchando si se está buscando un código o conectando.
                if (cm.State == ConnectionState.EnLobby) cm.StopBrowsing();
            }
        }

        // ---------------- Código ----------------
        void AddDigit(int d)
        {
            if (code.Length >= codeLength) return;
            code += d.ToString();
            RefreshCode();
        }

        void DeleteDigit()
        {
            if (code.Length > 0) code = code.Substring(0, code.Length - 1);
            RefreshCode();
        }

        void RefreshCode()
        {
            if (codeText != null)
                codeText.text = code.PadRight(codeLength, '_').Aggregate("", (s, c) => s + c + " ").TrimEnd();
            if (joinButton != null) joinButton.interactable = code.Length == codeLength;
        }

        void JoinWithCode()
        {
            if (ConnectionManager.Instance == null) { SetStatus("Falta el ConnectionManager en la escena."); return; }
            ConnectionManager.Instance.JoinByCode(code);
        }

        // ---------------- Lista ----------------
        void RefreshList()
        {
            foreach (var b in roomButtons) if (b != null) Destroy(b.gameObject);
            roomButtons.Clear();

            var cm = ConnectionManager.Instance;
            var rooms = cm != null
                ? cm.Discovery.Rooms.OrderBy(r => r.code).Take(maxRoomsShown).ToList()
                : new List<RoomInfo>();

            if (emptyListText != null)
            {
                emptyListText.gameObject.SetActive(rooms.Count == 0);
                emptyListText.text = "Buscando salas en la red…";
            }
            if (roomButtonTemplate == null || roomListContainer == null) return;

            foreach (var room in rooms)
            {
                var b = Instantiate(roomButtonTemplate, roomListContainer);
                b.gameObject.SetActive(true);
                b.gameObject.name = "Sala_" + room.code;
                var label = b.GetComponentInChildren<Text>(true);
                if (label != null)
                    label.text = $"{room.name}   ·   Código {room.code}   ·   {room.players - 1}/{room.maxPlayers - 1}";
                b.interactable = !room.IsFull;
                var r = room;
                b.onClick.AddListener(() => ConnectionManager.Instance.JoinRoom(r));
                roomButtons.Add(b);
            }
        }

        void OnState(ConnectionState state, string message)
        {
            bool idle = state == ConnectionState.EnLobby;
            foreach (var b in roomButtons) if (b != null) b.interactable = idle;
            if (joinButton != null) joinButton.interactable = idle && code.Length == codeLength;
            SetStatus(message);
        }

        void SetStatus(string msg)
        {
            if (statusText != null && !string.IsNullOrEmpty(msg)) statusText.text = msg;
        }
    }
}
