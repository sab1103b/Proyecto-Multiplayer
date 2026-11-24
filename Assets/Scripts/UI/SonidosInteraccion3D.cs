using System.Collections.Generic;
using CuboVR.Networking;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Sonidos de las interacciones 3D (lobby, consola del profesor, levantar la mano, salas).
///
/// No modifica ningún script ni la escena: se crea solo al darle Play y se "engancha" a los
/// componentes que ya existen (CodigoConCubos, ConsolaProfesor, GrabadoraClase, ManoLevantada,
/// ConnectionManager), escuchando sus eventos o mirando su estado.
///
/// Los sonidos se cargan de Assets/Resources/SonidosInteraccion3D/ (mismo nombre del campo).
/// Para quitarlo basta con borrar este script y esa carpeta.
/// </summary>
public class SonidosInteraccion3D : MonoBehaviour
{
    const string CarpetaSonidos = "SonidosInteraccion3D/";

    [Header("Lobby: código con cubos")]
    public AudioClip cuboAparece;
    public AudioClip cuboEncaja;
    public AudioClip cuboQuitar;
    public AudioClip codigoListo;

    [Header("Consola: ficha de tableros y botón de figuras")]
    public AudioClip fichaAgarrar;
    public AudioClip fichaEncaja;
    public AudioClip fichaSoltar;
    public AudioClip botonFisico;

    [Header("Consola: perilla de grabación")]
    public AudioClip perillaTic;
    public AudioClip grabarInicio;
    public AudioClip grabarPausa;
    [Tooltip("Cada cuántos grados suena un 'tic' al girar la perilla.")]
    public float gradosPorTic = 30f;

    [Header("Levantar la mano")]
    [Tooltip("Lo oye el estudiante cuando su mano levantada se envió.")]
    public AudioClip manoEnviada;
    [Tooltip("Reemplaza el aviso que oye el profesor cuando alguien levanta la mano.")]
    public AudioClip manoAviso;

    [Header("Salas")]
    public AudioClip teletransporte;
    [Tooltip("Cuando no se pudo crear o encontrar la sala.")]
    public AudioClip error;

    [Header("Volumen y distancia")]
    [Range(0f, 1f)] public float volumen = 0.8f;
    public float distanciaMinima = 0.6f;
    public float distanciaMaxima = 10f;
    [Tooltip("Si el jugador salta más de esto en un solo cuadro, se considera teletransporte (metros).")]
    public float saltoTeletransporte = 1.5f;

    // ---------------------------------------------------------------------
    // Arranque automático (sin tocar la escena)
    // ---------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CrearSolo()
    {
        if (FindFirstObjectByType<SonidosInteraccion3D>(FindObjectsInactive.Include) != null) return;
        var go = new GameObject("Sonidos Interaccion 3D");
        DontDestroyOnLoad(go);
        go.AddComponent<SonidosInteraccion3D>();
    }

    AudioSource[] fuentes;
    int siguienteFuente;
    readonly HashSet<Object> conectados = new HashSet<Object>();
    float proximaBusqueda;

    // Estado observado
    readonly List<CodigoConCubos> codigos = new List<CodigoConCubos>();
    readonly Dictionary<CodigoConCubos, bool> codigoCompleto = new Dictionary<CodigoConCubos, bool>();
    readonly List<(ConsolaProfesor consola, int cuadro)> fichasSoltadas = new List<(ConsolaProfesor, int)>();
    readonly List<GrabadoraClase> grabadoras = new List<GrabadoraClase>();
    readonly Dictionary<GrabadoraClase, float> anguloAnterior = new Dictionary<GrabadoraClase, float>();
    readonly Dictionary<GrabadoraClase, bool> grabandoAnterior = new Dictionary<GrabadoraClase, bool>();
    ManoLevantada mano;
    float tiempoManoArriba;
    bool manoYaSono;
    XROrigin origen;
    Vector3 posicionOrigen;
    ConnectionManager conexion;
    ConnectionState estadoAnterior;

    void Awake()
    {
        CargarSonidos();
        fuentes = new AudioSource[8];
        for (int i = 0; i < fuentes.Length; i++)
        {
            var go = new GameObject("Fuente_" + i);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = distanciaMinima;
            s.maxDistance = distanciaMaxima;
            fuentes[i] = s;
        }
    }

    void CargarSonidos()
    {
        cuboAparece ??= Cargar("cubo_aparece");
        cuboEncaja ??= Cargar("cubo_encaja");
        cuboQuitar ??= Cargar("cubo_quitar");
        codigoListo ??= Cargar("codigo_listo");
        fichaAgarrar ??= Cargar("ficha_agarrar");
        fichaEncaja ??= Cargar("ficha_encaja");
        fichaSoltar ??= Cargar("ficha_soltar");
        botonFisico ??= Cargar("boton_fisico");
        perillaTic ??= Cargar("perilla_tic");
        grabarInicio ??= Cargar("grabar_inicio");
        grabarPausa ??= Cargar("grabar_pausa");
        manoEnviada ??= Cargar("mano_enviada");
        manoAviso ??= Cargar("mano_aviso");
        teletransporte ??= Cargar("teletransporte");
        error ??= Cargar("ui_error");
    }

    static AudioClip Cargar(string nombre) => Resources.Load<AudioClip>(CarpetaSonidos + nombre);

    // ---------------------------------------------------------------------
    // Reproducir
    // ---------------------------------------------------------------------
    void Sonar(AudioClip clip, Vector3 posicion, float vol = 1f, bool variarTono = false)
    {
        if (clip == null) return;
        var s = fuentes[siguienteFuente];
        siguienteFuente = (siguienteFuente + 1) % fuentes.Length;
        s.transform.position = posicion;
        s.spatialBlend = 1f;
        s.pitch = variarTono ? Random.Range(0.95f, 1.05f) : 1f;
        s.PlayOneShot(clip, volumen * vol);
    }

    /// <summary>Sonido "en la cabeza" (sin posición), para lo que le pasa al propio jugador.</summary>
    void SonarCerca(AudioClip clip, float vol = 1f)
    {
        if (clip == null) return;
        var s = fuentes[siguienteFuente];
        siguienteFuente = (siguienteFuente + 1) % fuentes.Length;
        s.spatialBlend = 0f;
        s.pitch = 1f;
        s.PlayOneShot(clip, volumen * vol);
    }

    // ---------------------------------------------------------------------
    // Buscar y enganchar (cada 2 s, por si algo aparece más tarde)
    // ---------------------------------------------------------------------
    void Update()
    {
        if (Time.unscaledTime >= proximaBusqueda)
        {
            proximaBusqueda = Time.unscaledTime + 2f;
            Buscar();
        }

        RevisarCodigos();
        RevisarFichas();
        RevisarPerillas();
        RevisarTeletransporte();
    }

    void LateUpdate()
    {
        RevisarManoLevantada();
    }

    void Buscar()
    {
        foreach (var c in FindObjectsByType<CodigoConCubos>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (conectados.Add(c)) EngancharCodigo(c);
        foreach (var k in FindObjectsByType<ConsolaProfesor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (conectados.Add(k)) EngancharConsola(k);
        foreach (var g in FindObjectsByType<GrabadoraClase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (conectados.Add(g)) EngancharGrabadora(g);

        if (mano == null)
        {
            mano = FindFirstObjectByType<ManoLevantada>(FindObjectsInactive.Include);
            if (mano != null && mano.sonidoAviso != null && manoAviso != null)
                mano.sonidoAviso.clip = manoAviso;
        }

        if (conexion == null && ConnectionManager.Instance != null)
        {
            conexion = ConnectionManager.Instance;
            estadoAnterior = conexion.State;
            conexion.StateChanged += AlCambiarConexion;
        }

        if (origen == null)
        {
            origen = FindFirstObjectByType<XROrigin>();
            if (origen != null) posicionOrigen = origen.transform.position;
        }
    }

    void OnDestroy()
    {
        if (conexion != null) conexion.StateChanged -= AlCambiarConexion;
    }

    // ---------------------------------------------------------------------
    // 1. LOBBY: cubos de número
    // ---------------------------------------------------------------------
    void EngancharCodigo(CodigoConCubos c)
    {
        codigos.Add(c);
        codigoCompleto[c] = c.botonUnirse != null && c.botonUnirse.interactable;

        if (c.botonesNumero != null)
            foreach (var b in c.botonesNumero)
            {
                if (b == null) continue;
                var boton = b;
                boton.onClick.AddListener(() =>
                    Sonar(cuboAparece, boton.transform.position - boton.transform.forward * c.distanciaSalida, 0.8f, true));
            }

        if (c.huecos != null)
            foreach (var h in c.huecos)
            {
                if (h == null) continue;
                var hueco = h;
                hueco.selectEntered.AddListener(_ => { if (EnLobby()) Sonar(cuboEncaja, hueco.transform.position, 1f, true); });
                hueco.selectExited.AddListener(_ => { if (EnLobby() && hueco.isActiveAndEnabled) Sonar(cuboQuitar, hueco.transform.position, 0.6f); });
            }
    }

    void RevisarCodigos()
    {
        foreach (var c in codigos)
        {
            if (c == null || c.botonUnirse == null) continue;
            bool completo = c.botonUnirse.interactable;
            if (completo && !codigoCompleto[c] && c.isActiveAndEnabled)
                Sonar(codigoListo, c.botonUnirse.transform.position, 0.9f);
            codigoCompleto[c] = completo;
        }
    }

    bool EnLobby() => ConnectionManager.Instance == null || ConnectionManager.Instance.State != ConnectionState.EnSala;

    // ---------------------------------------------------------------------
    // 2. CONSOLA: ficha y botón de figuras
    // ---------------------------------------------------------------------
    void EngancharConsola(ConsolaProfesor k)
    {
        if (k.ficha != null)
        {
            k.ficha.selectEntered.AddListener(_ =>
            {
                if (ConsolaProfesor.EsProfesorLocal()) Sonar(fichaAgarrar, k.ficha.transform.position, 0.7f);
            });
            // Se revisa en el cuadro siguiente, cuando la consola ya decidió si encajó en un tablero.
            k.ficha.selectExited.AddListener(_ =>
            {
                if (ConsolaProfesor.EsProfesorLocal()) fichasSoltadas.Add((k, Time.frameCount));
            });
        }
        if (k.botonFiguras != null)
            k.botonFiguras.selectEntered.AddListener(_ =>
            {
                if (ConsolaProfesor.EsProfesorLocal()) Sonar(botonFisico, k.botonFiguras.transform.position);
            });
    }

    void RevisarFichas()
    {
        for (int i = fichasSoltadas.Count - 1; i >= 0; i--)
        {
            var (k, cuadro) = fichasSoltadas[i];
            if (Time.frameCount <= cuadro) continue;
            fichasSoltadas.RemoveAt(i);
            if (k == null || k.ficha == null) continue;
            bool encajo = k.pantallaTablero != null && k.pantallaTablero.activeSelf;
            Sonar(encajo ? fichaEncaja : fichaSoltar, k.ficha.transform.position, encajo ? 1f : 0.7f);
        }
    }

    // ---------------------------------------------------------------------
    // 3. CONSOLA: perilla STOP / REC
    // ---------------------------------------------------------------------
    void EngancharGrabadora(GrabadoraClase g)
    {
        if (g.perilla == null) return;
        grabadoras.Add(g);
        float a = g.perilla.transform.localEulerAngles.y;
        anguloAnterior[g] = a;
        grabandoAnterior[g] = EsRec(a);
    }

    void RevisarPerillas()
    {
        foreach (var g in grabadoras)
        {
            if (g == null || g.perilla == null) continue;
            float a = g.perilla.transform.localEulerAngles.y;
            float antes = anguloAnterior[g];
            anguloAnterior[g] = a;

            // Tic de trinquete cada vez que cruza una marca.
            if (gradosPorTic > 0f && Mathf.FloorToInt(a / gradosPorTic) != Mathf.FloorToInt(antes / gradosPorTic))
                Sonar(perillaTic, g.perilla.transform.position, 0.6f, true);

            // Al quedar quieta en un múltiplo de 90°, decide igual que GrabadoraClase.
            bool quieta = !g.perilla.isSelected && Mathf.Abs(Mathf.DeltaAngle(a, antes)) < 0.01f;
            bool enMarca = Mathf.Abs(Mathf.DeltaAngle(a, Mathf.Round(a / 90f) * 90f)) < 0.5f;
            if (!quieta || !enMarca) continue;

            bool rec = EsRec(a);
            if (rec != grabandoAnterior[g])
            {
                grabandoAnterior[g] = rec;
                Sonar(rec ? grabarInicio : grabarPausa, g.perilla.transform.position);
            }
        }
    }

    /// <summary>Misma regla de GrabadoraClase: ángulo/180 entero = STOP, decimal = REC.</summary>
    static bool EsRec(float grados)
    {
        int cuartos = Mathf.RoundToInt(Mathf.Repeat(grados, 360f) / 90f) % 4;
        return cuartos == 1 || cuartos == 3;
    }

    // ---------------------------------------------------------------------
    // 4. LEVANTAR LA MANO (lo que oye el estudiante al enviarla)
    // ---------------------------------------------------------------------
    void RevisarManoLevantada()
    {
        var yo = VRNetworkPlayer.Local;
        var rig = VRRigReferences.Instance;
        if (mano == null || !mano.IsSpawned || yo == null || rig == null || yo.Role.Value != PlayerRole.Estudiante)
        {
            tiempoManoArriba = 0f;
            manoYaSono = false;
            return;
        }

        bool arriba = EnLaCaja(rig.head, rig.leftHand) || EnLaCaja(rig.head, rig.rightHand);
        if (!arriba)
        {
            tiempoManoArriba = 0f;
            manoYaSono = false;
            return;
        }
        tiempoManoArriba += Time.deltaTime;
        if (!manoYaSono && tiempoManoArriba >= mano.segundosMantener)
        {
            manoYaSono = true;
            SonarCerca(manoEnviada, 0.8f);
        }
    }

    bool EnLaCaja(Transform cabeza, Transform m)
    {
        if (cabeza == null || m == null) return false;
        Vector3 tam = mano.tamanoCaja;
        Vector3 centro = cabeza.position + Vector3.up * (mano.alturaSobreCabeza + tam.y * 0.5f);
        Vector3 d = m.position - centro;
        return Mathf.Abs(d.x) <= tam.x * 0.5f && Mathf.Abs(d.y) <= tam.y * 0.5f && Mathf.Abs(d.z) <= tam.z * 0.5f;
    }

    // ---------------------------------------------------------------------
    // 5. SALAS: teletransporte y error al conectar
    // ---------------------------------------------------------------------
    void RevisarTeletransporte()
    {
        if (origen == null) return;
        Vector3 ahora = origen.transform.position;
        if ((ahora - posicionOrigen).sqrMagnitude > saltoTeletransporte * saltoTeletransporte)
            SonarCerca(teletransporte, 0.7f);
        posicionOrigen = ahora;
    }

    void AlCambiarConexion(ConnectionState estado, string mensaje)
    {
        bool intentando = estadoAnterior == ConnectionState.CreandoSala
                       || estadoAnterior == ConnectionState.BuscandoSala
                       || estadoAnterior == ConnectionState.Conectando;
        if (intentando && estado == ConnectionState.EnLobby) SonarCerca(error, 0.8f);
        estadoAnterior = estado;
    }
}
