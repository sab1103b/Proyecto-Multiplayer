using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Mover objetos agarrados con el rayo usando el joystick de esa mano:
///  - Joystick adelante / atrás  → alejar / acercar el objeto.
///  - Joystick izquierda / derecha → girar el objeto.
/// Mientras un rayo sostiene algo, se pausa el caminar/girar/teletransporte de ESA mano,
/// para que el joystick no mueva al jugador al mismo tiempo. Al soltar, todo vuelve a la normalidad.
/// Va en el XR Origin (lo agrega Cubo VR > Configurar multijugador).
/// </summary>
public class ManipularConRayo : MonoBehaviour
{
    [Tooltip("Velocidad para acercar/alejar el objeto (metros por segundo).")]
    public float velocidadAcercar = 1.5f;

    [Tooltip("Velocidad de giro del objeto (grados por segundo).")]
    public float velocidadGiro = 120f;

    [Tooltip("Pausar caminar/girar de esa mano mientras el rayo sostiene un objeto.")]
    public bool pausarLocomocion = true;

    // Acciones de locomoción que se pausaron por cada rayo (para reactivarlas al soltar).
    readonly Dictionary<XRRayInteractor, List<InputAction>> accionesPausadas = new Dictionary<XRRayInteractor, List<InputAction>>();
    InputActionAsset[] archivosDeAcciones;

    void Start()
    {
        var gestor = FindFirstObjectByType<InputActionManager>();
        archivosDeAcciones = gestor != null ? gestor.actionAssets.Where(a => a != null).ToArray() : new InputActionAsset[0];

        // Los rayos de teletransporte no se tocan.
        var rayosTeleport = new HashSet<XRRayInteractor>(
            FindObjectsByType<TeleportationActivator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(t => t.teleportInteractor).Where(t => t != null));

        foreach (var rayo in GetComponentsInChildren<XRRayInteractor>(true))
        {
            const int capaTeleport = 31;
            if (rayosTeleport.Contains(rayo) || (rayo.interactionLayers.value & (1 << capaTeleport)) != 0) continue;

            string mano = ManoDe(rayo);
            if (mano == null)
            {
                Debug.LogWarning($"[CuboVR] No sé de qué mano es el rayo '{rayo.name}', no le configuro acercar/alejar.");
                continue;
            }

            rayo.manipulateAttachTransform = true;
            rayo.translateSpeed = velocidadAcercar;
            rayo.rotateSpeed = velocidadGiro;
            AsignarJoystick(rayo.translateManipulationInput, $"XRI {mano} Interaction/Translate Manipulation");
            AsignarJoystick(rayo.rotateManipulationInput, $"XRI {mano} Interaction/Rotate Manipulation");

            var esteRayo = rayo;
            bool izquierda = mano == "Left";
            rayo.selectEntered.AddListener(_ => PausarLocomocion(esteRayo, izquierda));
            rayo.selectExited.AddListener(_ => ReanudarLocomocion(esteRayo));
        }
    }

    // ---------------- Joystick del rayo ----------------
    void AsignarJoystick(XRInputValueReader<Vector2> lector, string nombreAccion)
    {
        if (lector == null || AccionDe(lector) != null) return; // ya tenía una acción asignada

        InputAction accion = null;
        foreach (var archivo in archivosDeAcciones)
        {
            accion = archivo.FindAction(nombreAccion);
            if (accion != null) break;
        }
        if (accion == null)
        {
            Debug.LogWarning($"[CuboVR] No encontré la acción '{nombreAccion}' en el Input Action Manager.");
            return;
        }
        lector.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
        lector.inputActionReference = InputActionReference.Create(accion);
        accion.Enable();
    }

    // ---------------- Pausar / reanudar locomoción ----------------
    void PausarLocomocion(XRRayInteractor rayo, bool izquierda)
    {
        if (!pausarLocomocion) return;
        ReanudarLocomocion(rayo);

        var lectores = new List<XRInputValueReader>();
        foreach (var mover in GetComponentsInChildren<ContinuousMoveProvider>(true))
            lectores.Add(izquierda ? mover.leftHandMoveInput : mover.rightHandMoveInput);
        foreach (var girar in GetComponentsInChildren<ContinuousTurnProvider>(true))
            lectores.Add(izquierda ? girar.leftHandTurnInput : girar.rightHandTurnInput);
        foreach (var girar in GetComponentsInChildren<SnapTurnProvider>(true))
            lectores.Add(izquierda ? girar.leftHandTurnInput : girar.rightHandTurnInput);

        var pausadas = new List<InputAction>();
        foreach (var lector in lectores)
        {
            var accion = AccionDe(lector);
            if (accion != null && accion.enabled && !pausadas.Contains(accion))
            {
                accion.Disable();
                pausadas.Add(accion);
            }
        }

        // El botón que activa el teletransporte en esta mano (script TeleportationActivator).
        foreach (var tele in GetComponentsInChildren<TeleportationActivator>(true))
        {
            if (tele.rayInteractor != rayo) continue;
            var accion = tele.teleportActivatorAction.action;
            if (accion != null && accion.enabled && !pausadas.Contains(accion))
            {
                accion.Disable();
                pausadas.Add(accion);
            }
        }
        accionesPausadas[rayo] = pausadas;
    }

    void ReanudarLocomocion(XRRayInteractor rayo)
    {
        if (!accionesPausadas.TryGetValue(rayo, out var pausadas)) return;
        foreach (var accion in pausadas) accion.Enable();
        accionesPausadas.Remove(rayo);
    }

    void OnDisable()
    {
        foreach (var rayo in accionesPausadas.Keys.ToList()) ReanudarLocomocion(rayo);
    }

    // ---------------- Ayudantes ----------------
    static InputAction AccionDe(XRInputValueReader lector)
    {
        if (lector == null) return null;
        switch (lector.inputSourceMode)
        {
            case XRInputValueReader.InputSourceMode.InputActionReference:
                return lector.inputActionReference != null ? lector.inputActionReference.action : null;
            case XRInputValueReader.InputSourceMode.InputAction:
                return lector.inputAction != null && lector.inputAction.bindings.Count > 0 ? lector.inputAction : null;
            default:
                return null;
        }
    }

    /// <summary>"Left" o "Right" según la configuración del rayo o el nombre de sus objetos padre.</summary>
    static string ManoDe(XRRayInteractor rayo)
    {
        if (rayo.handedness == InteractorHandedness.Left) return "Left";
        if (rayo.handedness == InteractorHandedness.Right) return "Right";
        for (var t = rayo.transform; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("left")) return "Left";
            if (n.Contains("right")) return "Right";
        }
        return null;
    }
}
