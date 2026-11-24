using System.Collections.Generic;
using CuboVR.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Levantar la mano" para pedir la palabra y pasar al cubo con el profesor.
///
/// ESTUDIANTE (en su propio dispositivo):
///   Hay una caja invisible encima de la cabeza. Si una mano entra ahí y se queda un momento,
///   se manda la petición al profesor. Bajar la mano y volver a subirla la manda otra vez
///   (el profesor ignora las repetidas).
///
/// PROFESOR (es el host, así que la cola vive en su dispositivo):
///   - Cola de manos levantadas, en orden de llegada.
///   - En la cara de control del cubo aparece un mini muñeco + "+N" (cuántos más esperan),
///     con los botones Aceptar y Negar.
///   - Negar: sale de la cola y no puede volver a pedir durante 3 minutos.
///   - Aceptar: el estudiante se teletransporta dentro del cubo. Solo cabe uno a la vez.
///   - Retirar: el estudiante vuelve a su puesto fuera del cubo.
///
/// Va en "Network Systems" (lo agrega Cubo VR > Configurar multijugador).
/// </summary>
public class ManoLevantada : NetworkBehaviour
{
    [Header("Detección (estudiante)")]
    [Tooltip("Ancho, alto y fondo de la caja invisible encima de la cabeza (metros).")]
    public Vector3 tamanoCaja = new Vector3(0.7f, 0.5f, 0.7f);
    [Tooltip("Qué tan arriba de los ojos empieza la caja (metros).")]
    public float alturaSobreCabeza = 0.1f;
    [Tooltip("Segundos que la mano debe quedarse arriba para contar (evita roces).")]
    public float segundosMantener = 0.4f;
    [Tooltip("Sin visor (simulador): mantener F sube la mano derecha y muestra la caja. Con visor real no hace nada.")]
    public bool teclaDePrueba = true;

    [Header("Reglas (profesor)")]
    [Tooltip("Segundos sin poder volver a pedir después de que el profesor niega.")]
    public float segundosEsperaAlNegar = 180f;
    [Tooltip("Punto dentro del cubo donde aparece el estudiante aceptado.")]
    public Transform puntoInvitado;

    [Header("Panel en la cara de control")]
    public GameObject panel;
    public Transform miniModelo;
    [Tooltip("Globo de diálogo encima del muñeco con \"+N\" (cuántos hay en la fila).")]
    public GameObject globo;
    public Text textoCola;
    public Text textoMensaje;
    public Button botonAceptar;
    public Button botonNegar;
    public Button botonRetirar;
    public AudioSource sonidoAviso;

    // ---- Estado del profesor (solo se usa en el servidor) ----
    readonly List<ulong> cola = new List<ulong>();
    readonly Dictionary<ulong, float> esperaHasta = new Dictionary<ulong, float>();
    ulong estudianteDentro;
    bool hayEstudianteDentro;

    // ---- Estado del estudiante (en su dispositivo) ----
    float tiempoArriba;
    bool yaAvisado;

    Vector3 escalaModelo;

    void Awake()
    {
        if (miniModelo != null) escalaModelo = miniModelo.localScale;
        if (botonAceptar != null) botonAceptar.onClick.AddListener(Aceptar);
        if (botonNegar != null) botonNegar.onClick.AddListener(Negar);
        if (botonRetirar != null) botonRetirar.onClick.AddListener(Retirar);
        if (panel != null) panel.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback += AlDesconectarse;
        ActualizarPanel();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= AlDesconectarse;
        cola.Clear();
        esperaHasta.Clear();
        hayEstudianteDentro = false;
        if (panel != null) panel.SetActive(false);
    }

    void OnEnable() { Application.onBeforeRender += SubirManoDePrueba; }
    void OnDisable() { Application.onBeforeRender -= SubirManoDePrueba; }

    void Update()
    {
        AnimarMiniModelo();
    }

    // En LateUpdate: los controles ya se movieron este cuadro, así la mano de prueba gana.
    void LateUpdate()
    {
        ModoPrueba();
        DetectarManoLevantada();
        MostrarCajaDePrueba();
    }

    // =====================================================================
    // 1. ESTUDIANTE: ¿hay una mano dentro de la caja sobre la cabeza?
    // =====================================================================
    void DetectarManoLevantada()
    {
        var yo = VRNetworkPlayer.Local;
        var rig = VRRigReferences.Instance;
        if (!IsSpawned || yo == null || rig == null || yo.Role.Value != PlayerRole.Estudiante) return;

        manoArriba = EnLaCaja(rig.head, rig.leftHand) || EnLaCaja(rig.head, rig.rightHand);
        if (!manoArriba)
        {
            tiempoArriba = 0f;
            yaAvisado = false; // al bajarla, la próxima vez vuelve a avisar
            return;
        }

        tiempoArriba += Time.deltaTime;
        if (!yaAvisado && tiempoArriba >= segundosMantener)
        {
            yaAvisado = true;
            PedirTurnoRpc();
        }
    }

    // =====================================================================
    // MODO PRUEBA (sin visor, con XR Device Simulator)
    //   Mantener F sube la mano derecha por encima de la cabeza y se ve la caja:
    //   roja = la mano no está dentro, verde = detectada.
    //   Si hay un visor real conectado, nada de esto se activa.
    // =====================================================================
    bool manoArriba;
    float subida;          // 0 = mano normal, 1 = mano arriba del todo
    GameObject cajaVisible;
    Material materialCaja;

    bool PruebaDisponible()
    {
        if (!teclaDePrueba || UnityEngine.XR.XRSettings.isDeviceActive) return false;
        var yo = VRNetworkPlayer.Local;
        return yo != null && yo.Role.Value == PlayerRole.Estudiante && VRRigReferences.Instance != null;
    }

    void ModoPrueba()
    {
        if (!PruebaDisponible()) { subida = 0f; return; }
        var teclado = UnityEngine.InputSystem.Keyboard.current;
        bool f = teclado != null && teclado.fKey.isPressed;
        subida = Mathf.MoveTowards(subida, f ? 1f : 0f, Time.deltaTime * 4f); // sube/baja en ~0,25 s
        SubirManoDePrueba();
    }

    /// <summary>Lleva la mano derecha (y la del avatar que ven los demás) hacia el centro de la caja.</summary>
    void SubirManoDePrueba()
    {
        if (subida <= 0f || !PruebaDisponible()) return;
        var rig = VRRigReferences.Instance;
        if (rig.head == null || rig.rightHand == null) return;

        Vector3 arriba = rig.head.position + Vector3.up * (alturaSobreCabeza + tamanoCaja.y * 0.5f)
                         + rig.head.right * 0.1f;
        Vector3 pos = Vector3.Lerp(rig.rightHand.position, arriba, subida);
        rig.rightHand.position = pos;

        var avatar = VRNetworkPlayer.Local != null ? VRNetworkPlayer.Local.rightHand : null;
        if (avatar != null) avatar.position = pos;
    }

    void MostrarCajaDePrueba()
    {
        bool ver = PruebaDisponible() && subida > 0f;
        if (!ver)
        {
            if (cajaVisible != null) cajaVisible.SetActive(false);
            return;
        }
        if (cajaVisible == null)
        {
            cajaVisible = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cajaVisible.name = "Caja_ManoLevantada (prueba)";
            Destroy(cajaVisible.GetComponent<Collider>());
            materialCaja = new Material(Shader.Find("Sprites/Default")); // admite transparencia
            cajaVisible.GetComponent<Renderer>().sharedMaterial = materialCaja;
        }
        cajaVisible.SetActive(true);
        var cabeza = VRRigReferences.Instance.head;
        cajaVisible.transform.SetPositionAndRotation(
            cabeza.position + Vector3.up * (alturaSobreCabeza + tamanoCaja.y * 0.5f), Quaternion.identity);
        cajaVisible.transform.localScale = tamanoCaja;
        materialCaja.color = manoArriba ? new Color(0.2f, 0.9f, 0.4f, 0.35f) : new Color(0.9f, 0.25f, 0.25f, 0.35f);
    }

    bool EnLaCaja(Transform cabeza, Transform mano)
    {
        if (cabeza == null || mano == null) return false;
        Vector3 centro = cabeza.position + Vector3.up * (alturaSobreCabeza + tamanoCaja.y * 0.5f);
        Vector3 d = mano.position - centro;
        return Mathf.Abs(d.x) <= tamanoCaja.x * 0.5f
            && Mathf.Abs(d.y) <= tamanoCaja.y * 0.5f
            && Mathf.Abs(d.z) <= tamanoCaja.z * 0.5f;
    }

    // =====================================================================
    // 2. PROFESOR: recibir la petición y manejar la cola
    // =====================================================================
    [Rpc(SendTo.Server)]
    void PedirTurnoRpc(RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        var jugador = JugadorDe(id);
        if (jugador == null || jugador.Role.Value != PlayerRole.Estudiante) return;
        if (cola.Contains(id)) return;                                   // ya está en la fila
        if (hayEstudianteDentro && estudianteDentro == id) return;       // ya está en el cubo
        if (esperaHasta.TryGetValue(id, out float hasta) && Time.time < hasta) return; // castigo de 3 min

        cola.Add(id);
        if (sonidoAviso != null) sonidoAviso.Play();
        ActualizarPanel();
    }

    void Aceptar()
    {
        if (!IsServer || cola.Count == 0 || hayEstudianteDentro) return;
        ulong id = cola[0];
        cola.RemoveAt(0);
        estudianteDentro = id;
        hayEstudianteDentro = true;
        MoverEstudianteRpc(true, RpcTarget.Single(id, RpcTargetUse.Temp));
        ActualizarPanel();
    }

    void Negar()
    {
        if (!IsServer || cola.Count == 0) return;
        ulong id = cola[0];
        cola.RemoveAt(0);
        esperaHasta[id] = Time.time + segundosEsperaAlNegar;
        ActualizarPanel();
    }

    void Retirar()
    {
        if (!IsServer || !hayEstudianteDentro) return;
        hayEstudianteDentro = false;
        MoverEstudianteRpc(false, RpcTarget.Single(estudianteDentro, RpcTargetUse.Temp));
        ActualizarPanel();
    }

    void AlDesconectarse(ulong id)
    {
        cola.Remove(id);
        esperaHasta.Remove(id);
        if (hayEstudianteDentro && estudianteDentro == id) hayEstudianteDentro = false;
        ActualizarPanel();
    }

    // =====================================================================
    // 3. ESTUDIANTE: el profesor lo mete o lo saca del cubo
    // =====================================================================
    [Rpc(SendTo.SpecifiedInParams)]
    void MoverEstudianteRpc(bool adentro, RpcParams rpcParams)
    {
        var puntos = NetworkSpawnPoints.Instance;
        var yo = VRNetworkPlayer.Local;
        if (puntos == null || yo == null) return;

        Transform destino = adentro
            ? (puntoInvitado != null ? puntoInvitado : puntos.profesorSpawn)
            : puntos.GetSpawn(PlayerRole.Estudiante, yo.SeatIndex.Value);
        NetworkSpawnPoints.TeleportLocalPlayer(destino);
    }

    // =====================================================================
    // 4. PANEL (solo lo ve el profesor)
    // =====================================================================
    void ActualizarPanel()
    {
        if (panel == null) return;
        bool mostrar = IsSpawned && IsServer && (cola.Count > 0 || hayEstudianteDentro);
        panel.SetActive(mostrar);
        if (!mostrar) return;

        bool hayCola = cola.Count > 0;
        if (miniModelo != null) miniModelo.gameObject.SetActive(hayCola);
        // Globo: "+N" = cuántas personas hay en la fila.
        if (globo != null) globo.SetActive(hayCola);
        if (textoCola != null) textoCola.text = "+" + cola.Count;
        if (botonAceptar != null)
        {
            botonAceptar.gameObject.SetActive(hayCola);
            botonAceptar.interactable = !hayEstudianteDentro; // solo uno dentro a la vez
        }
        if (botonNegar != null) botonNegar.gameObject.SetActive(hayCola);
        if (botonRetirar != null)
        {
            botonRetirar.gameObject.SetActive(hayEstudianteDentro);
            var texto = botonRetirar.GetComponentInChildren<Text>(true);
            if (texto != null && hayEstudianteDentro) texto.text = "Sacar del cubo a " + NombreDe(estudianteDentro);
        }

        // Etiqueta bajo el muñeco: quién levantó la mano (el primero de la fila).
        if (textoMensaje != null)
        {
            var etiqueta = textoMensaje.transform.parent != null ? textoMensaje.transform.parent.gameObject : textoMensaje.gameObject;
            etiqueta.SetActive(hayCola);
            if (hayCola) textoMensaje.text = NombreDe(cola[0]);
        }
    }

    void AnimarMiniModelo()
    {
        if (miniModelo == null || !miniModelo.gameObject.activeInHierarchy) return;
        // Late suavemente para llamar la atención.
        float pulso = 1f + 0.08f * Mathf.Sin(Time.time * 4f);
        miniModelo.localScale = escalaModelo * pulso;
    }

    // ---------------- Ayudantes ----------------
    VRNetworkPlayer JugadorDe(ulong id)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(id, out var cliente)) return null;
        return cliente.PlayerObject != null ? cliente.PlayerObject.GetComponent<VRNetworkPlayer>() : null;
    }

    string NombreDe(ulong id)
    {
        var j = JugadorDe(id);
        return j != null ? "Estudiante " + (j.SeatIndex.Value + 1) : "Un estudiante";
    }

    void OnDrawGizmos()
    {
        // En Play se ve la caja sobre la cabeza del jugador local (vista de escena).
        var rig = VRRigReferences.Instance;
        if (rig == null || rig.head == null) return;
        Gizmos.color = new Color(0.16f, 0.62f, 0.56f, 0.8f);
        Gizmos.DrawWireCube(rig.head.position + Vector3.up * (alturaSobreCabeza + tamanoCaja.y * 0.5f), tamanoCaja);
    }
}
