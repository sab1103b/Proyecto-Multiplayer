using System.Collections.Generic;
using CuboVR.Networking;
using UnityEngine;

/// <summary>
/// Efectos visuales y de sonido para los círculos del lobby (Profesor / Estudiante).
///
/// Por cada ZonaLobby agrega, solo mientras se juega (no cambia la escena ni los scripts):
///   - Borde que "respira" suave desde afuera y se ilumina más fuerte al entrar.
///   - Una luz del color de la zona que se enciende al entrar.
///   - Partículas de luz que suben desde el borde (más al entrar).
///   - El cartel flota suavemente.
///   - Al entrar: el área de color se expande desde el centro y los paneles aparecen con un pequeño "pop".
///   - Sonido al entrar y al salir del círculo.
///
/// Se crea solo al darle Play. Para quitarlo basta con borrar este script.
/// </summary>
public class EfectosLobby3D : MonoBehaviour
{
    [Header("Sonidos (se cargan de Resources/SonidosInteraccion3D)")]
    public AudioClip sonidoEntrar;
    public AudioClip sonidoSalir;
    [Range(0f, 1f)] public float volumen = 0.75f;

    [Header("Luz")]
    public float intensidadAfuera = 0.4f;
    public float intensidadDentro = 2.5f;
    public float alcanceLuz = 3.5f;

    [Header("Borde")]
    public float anchoDentro = 0.075f;

    [Header("Halo (cortina de luz que sube del círculo)")]
    public float alturaHaloAfuera = 0.5f;
    public float alturaHaloDentro = 2.2f;
    [Range(0f, 1f)] public float opacidadHaloAfuera = 0.35f;
    [Range(0f, 1f)] public float opacidadHaloDentro = 0.8f;

    [Header("Partículas")]
    public float particulasAfuera = 6f;
    public float particulasDentro = 30f;
    public int rafagaAlEntrar = 70;

    [Header("Tamaño de letreros (para leerlos bien en VR desde lejos)")]
    [Tooltip("Multiplica el tamaño del cartel PROFESOR / ESTUDIANTE.")]
    public float escalaCarteles = 1.6f;
    [Tooltip("Multiplica el tamaño del título CUBO VR del lobby.")]
    public float escalaBienvenida = 1.8f;
    bool bienvenidaAgrandada;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CrearSolo()
    {
        if (FindFirstObjectByType<EfectosLobby3D>(FindObjectsInactive.Include) != null) return;
        var go = new GameObject("Efectos Lobby 3D");
        DontDestroyOnLoad(go);
        go.AddComponent<EfectosLobby3D>();
    }

    class Zona
    {
        public ZonaLobby zona;
        public Color color;
        public LineRenderer borde;
        public Material materialBorde;
        public float anchoBase;
        public Light luz;
        public Transform halo;
        public Material materialHalo;
        public ParticleSystem particulas;
        public Transform cartel;
        public Vector3 cartelBase;
        public Transform area;
        public Vector3 areaBase;
        public List<(Transform t, Vector3 escala)> paneles = new List<(Transform, Vector3)>();
        public bool dentro;
        public float tiempoEntrada = -10f;
        public float fase;
    }

    readonly List<Zona> zonas = new List<Zona>();
    readonly HashSet<ZonaLobby> conectadas = new HashSet<ZonaLobby>();
    float proximaBusqueda;
    AudioSource fuente;
    static Material materialParticula;

    void Awake()
    {
        sonidoEntrar ??= Resources.Load<AudioClip>("SonidosInteraccion3D/zona_entrar");
        sonidoSalir ??= Resources.Load<AudioClip>("SonidosInteraccion3D/zona_salir");
        fuente = gameObject.AddComponent<AudioSource>();
        fuente.playOnAwake = false;
        fuente.spatialBlend = 0f;
    }

    void Update()
    {
        if (Time.unscaledTime >= proximaBusqueda)
        {
            proximaBusqueda = Time.unscaledTime + 2f;
            if (!bienvenidaAgrandada)
            {
                var bienvenida = GameObject.Find("Canvas_Bienvenida");
                if (bienvenida != null)
                {
                    bienvenidaAgrandada = true;
                    bienvenida.transform.localScale *= escalaBienvenida;
                    bienvenida.transform.localPosition += Vector3.up * (0.13f * (escalaBienvenida - 1f));
                }
            }
            foreach (var z in FindObjectsByType<ZonaLobby>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (conectadas.Add(z)) Preparar(z);
        }

        var conexion = ConnectionManager.Instance;
        bool enLobby = conexion == null || conexion.State != ConnectionState.EnSala;

        foreach (var z in zonas)
        {
            if (z.zona == null) continue;
            bool dentro = z.zona.contenido != null && z.zona.contenido.activeSelf;
            if (dentro != z.dentro)
            {
                z.dentro = dentro;
                if (dentro) AlEntrar(z);
                else if (enLobby && sonidoSalir != null) fuente.PlayOneShot(sonidoSalir, volumen * 0.7f);
            }
            Animar(z, enLobby);
        }
    }

    // ---------------------------------------------------------------------
    void Preparar(ZonaLobby zl)
    {
        var z = new Zona { zona = zl, color = zl.color, fase = Random.value * 10f };

        var borde = zl.transform.Find("Borde");
        if (borde != null && borde.TryGetComponent(out LineRenderer linea))
        {
            z.borde = linea;
            z.materialBorde = linea.material; // copia propia: no cambia el material guardado
            z.anchoBase = linea.widthMultiplier;
        }

        z.cartel = zl.transform.Find("Cartel");
        if (z.cartel != null)
        {
            z.cartel.localScale *= escalaCarteles;
            // Se sube un poco para que al crecer no baje encima de los paneles.
            z.cartelBase = z.cartel.localPosition + Vector3.up * (0.11f * (escalaCarteles - 1f));
        }

        if (zl.contenido != null)
        {
            z.area = zl.contenido.transform.Find("Area_Color");
            if (z.area != null) z.areaBase = z.area.localScale;
            foreach (Transform hijo in zl.contenido.transform)
                if (hijo.GetComponent<Canvas>() != null) z.paneles.Add((hijo, hijo.localScale));
        }

        // Luz de la zona
        var luzGo = new GameObject("Luz_Zona");
        luzGo.transform.SetParent(zl.transform, false);
        luzGo.transform.localPosition = new Vector3(0, 0.6f, 0);
        z.luz = luzGo.AddComponent<Light>();
        z.luz.type = LightType.Point;
        z.luz.color = z.color;
        z.luz.range = alcanceLuz;
        z.luz.intensity = intensidadAfuera;
        z.luz.shadows = LightShadows.None;

        z.particulas = CrearParticulas(zl.transform, z.color, zl.radio);
        CrearHalo(z, zl.radio);
        zonas.Add(z);
    }

    ParticleSystem CrearParticulas(Transform padre, Color color, float radio)
    {
        var go = new GameObject("Particulas_Zona");
        go.transform.SetParent(padre, false);
        go.transform.localPosition = new Vector3(0, 0.03f, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        var emision = ps.emission;
        emision.rateOverTime = particulasAfuera;

        var forma = ps.shape;
        forma.shapeType = ParticleSystemShapeType.Circle;
        forma.radius = radio;
        forma.radiusThickness = 0f;          // solo desde el borde
        forma.rotation = new Vector3(90f, 0f, 0f); // círculo acostado en el piso

        var velocidad = ps.velocityOverLifetime;
        velocidad.enabled = true;
        velocidad.space = ParticleSystemSimulationSpace.World;
        velocidad.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocidad.y = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        velocidad.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var colorVida = ps.colorOverLifetime;
        colorVida.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0f, 1f) });
        colorVida.color = g;

        var render = go.GetComponent<ParticleSystemRenderer>();
        render.sharedMaterial = MaterialParticula();
        render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        render.receiveShadows = false;

        ps.Play();
        return ps;
    }

    // ---------------------------------------------------------------------
    // HALO: cilindro abierto (sin tapas) con un degradado que se desvanece hacia arriba
    // ---------------------------------------------------------------------
    static Mesh mallaHalo;
    static Texture2D texturaHalo;

    void CrearHalo(Zona z, float radio)
    {
        var go = new GameObject("Halo_Zona");
        go.transform.SetParent(z.zona.transform, false);
        go.transform.localPosition = new Vector3(0, 0.01f, 0);
        go.transform.localScale = new Vector3(radio, alturaHaloAfuera, radio);
        go.AddComponent<MeshFilter>().sharedMesh = MallaHalo();
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        // Sprites/Default: transparente y se ve por dentro y por fuera.
        z.materialHalo = new Material(Shader.Find("Sprites/Default")) { mainTexture = TexturaHalo(), name = "Halo_Zona" };
        var c = z.color; c.a = opacidadHaloAfuera;
        z.materialHalo.color = c;
        r.sharedMaterial = z.materialHalo;
        z.halo = go.transform;
    }

    static Mesh MallaHalo()
    {
        if (mallaHalo != null) return mallaHalo;
        const int lados = 72;
        var v = new Vector3[(lados + 1) * 2];
        var uv = new Vector2[v.Length];
        var tri = new int[lados * 6];
        for (int i = 0; i <= lados; i++)
        {
            float a = i * Mathf.PI * 2f / lados;
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i * 2] = p;                       // abajo
            v[i * 2 + 1] = p + Vector3.up;      // arriba (altura 1, se escala)
            float u = (float)i / lados * 6f;    // la textura se repite 6 veces alrededor
            uv[i * 2] = new Vector2(u, 0f);
            uv[i * 2 + 1] = new Vector2(u, 1f);
        }
        for (int i = 0; i < lados; i++)
        {
            int b = i * 2, t = i * 6;
            tri[t] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2;
            tri[t + 3] = b + 1; tri[t + 4] = b + 3; tri[t + 5] = b + 2;
        }
        mallaHalo = new Mesh { name = "HaloZona", vertices = v, uv = uv, triangles = tri };
        mallaHalo.RecalculateBounds();
        return mallaHalo;
    }

    static Texture2D TexturaHalo()
    {
        if (texturaHalo != null) return texturaHalo;
        const int w = 128, h = 128;
        texturaHalo = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "HaloDegradado", wrapMode = TextureWrapMode.Repeat };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            float fade = Mathf.Pow(1f - v, 2.2f);           // fuerte abajo, nada arriba
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w * Mathf.PI * 2f;
                // Rayos verticales suaves para que no se vea plano.
                float rayos = 0.65f + 0.2f * Mathf.Sin(u * 3f) + 0.15f * Mathf.Sin(u * 7f + 1.3f);
                float borde = Mathf.Clamp01(v * 25f);        // abajo empieza suave, sin corte duro
                px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(fade * rayos * (0.6f + 0.4f * borde)));
            }
        }
        texturaHalo.SetPixels(px);
        texturaHalo.Apply();
        return texturaHalo;
    }

    static Material MaterialParticula()
    {
        if (materialParticula != null) return materialParticula;
        const int tam = 64;
        var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { name = "PuntoSuave", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[tam * tam];
        for (int y = 0; y < tam; y++)
            for (int x = 0; x < tam; x++)
            {
                float dx = (x + 0.5f) / tam * 2f - 1f, dy = (y + 0.5f) / tam * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * tam + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        materialParticula = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, name = "Particula_Zona" };
        return materialParticula;
    }

    // ---------------------------------------------------------------------
    void AlEntrar(Zona z)
    {
        z.tiempoEntrada = Time.time;
        if (sonidoEntrar != null)
        {
            // El profesor suena un poco más grave que el estudiante.
            fuente.pitch = z.color.r > z.color.g ? 0.92f : 1f;
            fuente.PlayOneShot(sonidoEntrar, volumen);
        }
        if (z.particulas != null) z.particulas.Emit(rafagaAlEntrar);
        if (z.area != null) z.area.localScale = new Vector3(z.areaBase.x * 0.05f, z.areaBase.y, z.areaBase.z * 0.05f);
        foreach (var p in z.paneles) if (p.t != null) p.t.localScale = p.escala * 0.85f;
    }

    void Animar(Zona z, bool enLobby)
    {
        float t = Time.time + z.fase;
        float k = 1f - Mathf.Exp(-Time.deltaTime * 6f); // suavizado

        // Luz
        if (z.luz != null)
        {
            z.luz.enabled = enLobby;
            float objetivo = z.dentro ? intensidadDentro * (1f + 0.08f * Mathf.Sin(t * 3f)) : intensidadAfuera;
            z.luz.intensity = Mathf.Lerp(z.luz.intensity, objetivo, k);
        }

        // Borde: respira afuera, brilla adentro
        if (z.borde != null)
        {
            float ancho = z.dentro ? anchoDentro : z.anchoBase * (1f + 0.3f * Mathf.Sin(t * 2f));
            z.borde.widthMultiplier = Mathf.Lerp(z.borde.widthMultiplier, ancho, k);
            if (z.materialBorde != null)
            {
                Color c = z.dentro ? Color.Lerp(z.color, Color.white, 0.35f + 0.1f * Mathf.Sin(t * 4f)) : z.color;
                z.materialBorde.SetColor("_BaseColor", c);
                z.materialBorde.color = c;
            }
        }

        // Halo: bajito afuera, sube y se intensifica adentro; los rayos giran despacio.
        if (z.halo != null)
        {
            z.halo.gameObject.SetActive(enLobby);
            Vector3 e = z.halo.localScale;
            float alto = z.dentro ? alturaHaloDentro * (1f + 0.05f * Mathf.Sin(t * 2.5f))
                                  : alturaHaloAfuera * (1f + 0.15f * Mathf.Sin(t * 2f));
            e.y = Mathf.Lerp(e.y, alto, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            z.halo.localScale = e;
            if (z.materialHalo != null)
            {
                Color c = z.dentro ? Color.Lerp(z.color, Color.white, 0.2f) : z.color;
                float objetivoA = z.dentro ? opacidadHaloDentro : opacidadHaloAfuera;
                c.a = Mathf.Lerp(z.materialHalo.color.a, objetivoA, k);
                z.materialHalo.color = c;
            }
            z.halo.Rotate(0f, (z.dentro ? 12f : 4f) * Time.deltaTime, 0f, Space.Self);
        }

        // Partículas
        if (z.particulas != null)
        {
            var emision = z.particulas.emission;
            emision.rateOverTime = !enLobby ? 0f : z.dentro ? particulasDentro : particulasAfuera;
        }

        // Cartel flotando
        if (z.cartel != null)
            z.cartel.localPosition = z.cartelBase + Vector3.up * (0.03f * Mathf.Sin(t * 1.4f));

        // Animación de entrada (área que se expande y paneles que aparecen)
        float desde = Time.time - z.tiempoEntrada;
        if (z.dentro && desde < 0.6f)
        {
            float pa = Mathf.Clamp01(desde / 0.4f);
            float sobre = 1f + 0.08f * Mathf.Sin(pa * Mathf.PI); // pequeño rebote
            if (z.area != null)
            {
                float e = (1f - Mathf.Pow(1f - pa, 3f)) * sobre;
                z.area.localScale = new Vector3(z.areaBase.x * e, z.areaBase.y, z.areaBase.z * e);
            }
            float pp = Mathf.Clamp01((desde - 0.1f) / 0.3f);
            float ep = Mathf.Lerp(0.85f, 1f, 1f - Mathf.Pow(1f - pp, 3f));
            foreach (var p in z.paneles) if (p.t != null) p.t.localScale = p.escala * ep;
        }
        else if (z.dentro && desde < 0.7f)
        {
            if (z.area != null) z.area.localScale = z.areaBase;
            foreach (var p in z.paneles) if (p.t != null) p.t.localScale = p.escala;
        }
    }
}
