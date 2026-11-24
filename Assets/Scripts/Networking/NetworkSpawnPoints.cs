using Unity.XR.CoreUtils;
using UnityEngine;

namespace CuboVR.Networking
{
    /// <summary>
    /// Puntos de aparición de la escena: lobby (antes de conectarse), interior del cubo (profesor)
    /// y exterior del cubo (estudiantes). También sabe mover el XR Origin local a cualquiera de ellos.
    /// </summary>
    public class NetworkSpawnPoints : MonoBehaviour
    {
        public static NetworkSpawnPoints Instance { get; private set; }

        [Tooltip("Donde aparecen todos al abrir la app (zona lobby).")]
        public Transform lobbySpawn;
        [Tooltip("Interior del cubo: donde aparece el profesor al crear la sala.")]
        public Transform profesorSpawn;
        [Tooltip("Exterior del cubo: un punto por estudiante (se reparten en orden).")]
        public Transform[] estudianteSpawns;

        [Tooltip("Mover al jugador al lobby al iniciar la escena.")]
        public bool startInLobby = true;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            if (startInLobby && lobbySpawn != null) TeleportLocalPlayer(lobbySpawn);
        }

        public Transform GetSpawn(PlayerRole role, int seatIndex)
        {
            switch (role)
            {
                case PlayerRole.Profesor:
                    return profesorSpawn;
                case PlayerRole.Estudiante:
                    if (estudianteSpawns == null || estudianteSpawns.Length == 0) return lobbySpawn;
                    return estudianteSpawns[Mathf.Abs(seatIndex) % estudianteSpawns.Length];
                default:
                    return lobbySpawn;
            }
        }

        public void TeleportToLobby()
        {
            if (lobbySpawn != null) TeleportLocalPlayer(lobbySpawn);
        }

        /// <summary>Mueve el XR Origin para que la cabeza del jugador quede sobre el punto, mirando hacia su forward.</summary>
        public static void TeleportLocalPlayer(Transform target)
        {
            if (target == null) return;
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("[CuboVR] No hay XR Origin en la escena para teletransportar.");
                return;
            }

            var cc = origin.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            origin.MatchOriginUpCameraForward(Vector3.up, forward.normalized);

            // Pies del jugador sobre el punto: la cámara queda a su altura real encima.
            Vector3 camPos = target.position + Vector3.up * origin.CameraInOriginSpaceHeight;
            origin.MoveCameraToWorldLocation(camPos);

            if (cc != null) cc.enabled = ccWasEnabled;
        }

        void OnDrawGizmos()
        {
            DrawPoint(lobbySpawn, Color.white);
            DrawPoint(profesorSpawn, new Color(0.9f, 0.62f, 0.2f));
            if (estudianteSpawns != null)
                foreach (var s in estudianteSpawns) DrawPoint(s, new Color(0.16f, 0.62f, 0.56f));
        }

        static void DrawPoint(Transform t, Color c)
        {
            if (t == null) return;
            Gizmos.color = c;
            Gizmos.DrawWireSphere(t.position + Vector3.up * 0.05f, 0.3f);
            Gizmos.DrawLine(t.position + Vector3.up * 0.05f, t.position + Vector3.up * 0.05f + t.forward * 0.6f);
        }
    }
}
