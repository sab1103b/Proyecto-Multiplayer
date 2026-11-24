using System.IO;
using System.Linq;
using CuboVR.Networking;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Consola del profesor (dentro del cubo). Maneja los apartados 2 y 3:
///
/// APARTADO 2 — TABLEROS:
///   Una ficha que solo se desliza sobre la mesa (ejes X y Z, sin salirse del área).
///   Si se suelta encima de "Tablero 1/2/3" encaja ahí y aparece la pantalla de ese tablero:
///     Importar  → lista de imágenes guardadas; la elegida se carga en el tablero (para todos).
///     Exportar  → guarda el tablero como PNG.
///     Eliminar  → borra el tablero (para todos).
///     Nueva     → exporta y luego borra, para empezar de nuevo.
///
/// APARTADO 3 — FIGURAS:
///   Un botón que al presionarlo muestra/oculta la pantalla de figuras (Canvas_Figuras).
///
/// (El apartado 1 es GrabadoraClase y el 4 es ManoLevantada.)
/// Lo arma Cubo VR > Construir consola del profesor.
/// </summary>
public class ConsolaProfesor : MonoBehaviour
{
    [Header("Tableros")]
    [Tooltip("Las caras de dibujo: Tablero 1, 2 y 3.")]
    public CanvasManager[] tableros;
    [Tooltip("Los espacios sobre la mesa donde se deja la ficha (mismo orden que los tableros).")]
    public Transform[] espacios;
    public Renderer[] marcasEspacios;
    public Color colorEspacio = new Color(0.25f, 0.29f, 0.33f);
    public Color colorEspacioElegido = new Color(0.9f, 0.62f, 0.2f);

    [Header("Ficha")]
    public XRSimpleInteractable ficha;
    [Tooltip("Área donde se puede mover la ficha (coordenadas locales de la consola, X y Z).")]
    public Vector2 limiteMinimo = new Vector2(-0.84f, -0.2f);
    public Vector2 limiteMaximo = new Vector2(-0.28f, 0.2f);
    [Tooltip("Qué tan cerca de un espacio hay que soltarla para que encaje (metros).")]
    public float radioEncaje = 0.09f;

    [Header("Pantalla del tablero")]
    public GameObject pantallaTablero;
    public Text tituloTablero;
    public Text textoEstado;
    public GameObject grupoAcciones;
    public Button botonImportar, botonExportar, botonEliminar, botonNueva;
    public GameObject grupoLista;
    public Button[] botonesLista;
    public Button botonVolver;

    [Header("Figuras")]
    public XRSimpleInteractable botonFiguras;
    public GameObject pantallaFiguras;

    int tableroElegido = -1;
    IXRSelectInteractor manoFicha;
    Vector3 desfaseFicha;    // para que la ficha no salte al centro de la mano/rayo
    float alturaFicha;
    string[] archivosLista = new string[0];

    void Awake()
    {
        if (pantallaTablero != null) pantallaTablero.SetActive(false);
        if (pantallaFiguras != null) pantallaFiguras.SetActive(false);

        if (botonImportar != null) botonImportar.onClick.AddListener(MostrarLista);
        if (botonExportar != null) botonExportar.onClick.AddListener(Exportar);
        if (botonEliminar != null) botonEliminar.onClick.AddListener(Eliminar);
        if (botonNueva != null) botonNueva.onClick.AddListener(Nueva);
        if (botonVolver != null) botonVolver.onClick.AddListener(() => MostrarAcciones(""));
        for (int i = 0; i < botonesLista.Length; i++)
        {
            int indice = i;
            botonesLista[i].onClick.AddListener(() => Importar(indice));
        }

        if (ficha != null)
        {
            alturaFicha = transform.InverseTransformPoint(ficha.transform.position).y;
            ficha.selectEntered.AddListener(AgarrarFicha);
            ficha.selectExited.AddListener(SoltarFicha);
        }
        if (botonFiguras != null) botonFiguras.selectEntered.AddListener(_ => AlternarFiguras());
        PintarEspacios();
    }

    void Update()
    {
        if (manoFicha != null && PuntoDeLaMano(manoFicha, out Vector3 punto))
            ficha.transform.position = Limitar(punto + desfaseFicha);
    }

    /// <summary>En una sala, solo el profesor usa la consola. Sin sala (pruebas) cualquiera.</summary>
    public static bool EsProfesorLocal()
    {
        var yo = VRNetworkPlayer.Local;
        return yo == null || yo.Role.Value == PlayerRole.Profesor;
    }

    // =====================================================================
    // FICHA
    // =====================================================================
    void AgarrarFicha(SelectEnterEventArgs args)
    {
        if (!EsProfesorLocal()) return;
        manoFicha = args.interactorObject;
        desfaseFicha = PuntoDeLaMano(manoFicha, out Vector3 punto) ? ficha.transform.position - punto : Vector3.zero;
        desfaseFicha = Vector3.ProjectOnPlane(desfaseFicha, transform.up);
    }

    void SoltarFicha(SelectExitEventArgs args)
    {
        if (args.interactorObject != manoFicha) return;
        manoFicha = null;

        // ¿Quedó encima de algún tablero? Entonces encaja en el centro de ese espacio.
        int cerca = -1;
        float mejor = radioEncaje;
        for (int i = 0; i < espacios.Length; i++)
        {
            Vector3 d = Vector3.ProjectOnPlane(espacios[i].position - ficha.transform.position, transform.up);
            if (d.magnitude < mejor) { mejor = d.magnitude; cerca = i; }
        }
        if (cerca >= 0) ficha.transform.position = Limitar(espacios[cerca].position);
        ElegirTablero(cerca);
    }

    /// <summary>
    /// Con la mano: la posición de la mano. Con el rayo: donde el rayo corta el plano de la mesa.
    /// </summary>
    bool PuntoDeLaMano(IXRSelectInteractor mano, out Vector3 punto)
    {
        punto = Vector3.zero;
        if (mano is XRRayInteractor rayo)
        {
            Transform origen = rayo.rayOriginTransform != null ? rayo.rayOriginTransform : rayo.transform;
            var plano = new Plane(transform.up, transform.TransformPoint(new Vector3(0, alturaFicha, 0)));
            var r = new Ray(origen.position, origen.forward);
            if (!plano.Raycast(r, out float distancia)) return false;
            punto = r.GetPoint(distancia);
            return true;
        }
        punto = mano.GetAttachTransform(ficha).position;
        return true;
    }

    /// <summary>Mantiene la ficha sobre la mesa y dentro del área permitida.</summary>
    Vector3 Limitar(Vector3 mundo)
    {
        Vector3 local = transform.InverseTransformPoint(mundo);
        local.x = Mathf.Clamp(local.x, limiteMinimo.x, limiteMaximo.x);
        local.z = Mathf.Clamp(local.z, limiteMinimo.y, limiteMaximo.y);
        local.y = alturaFicha;
        return transform.TransformPoint(local);
    }

    // =====================================================================
    // PANTALLA DEL TABLERO
    // =====================================================================
    void ElegirTablero(int indice)
    {
        tableroElegido = indice >= 0 && indice < tableros.Length ? indice : -1;
        PintarEspacios();
        if (pantallaTablero == null) return;
        pantallaTablero.SetActive(tableroElegido >= 0);
        if (tableroElegido < 0) return;
        if (tituloTablero != null) tituloTablero.text = "Tablero " + (tableroElegido + 1);
        MostrarAcciones("");
    }

    void PintarEspacios()
    {
        for (int i = 0; i < marcasEspacios.Length; i++)
            if (marcasEspacios[i] != null)
                marcasEspacios[i].material.color = i == tableroElegido ? colorEspacioElegido : colorEspacio;
    }

    void MostrarAcciones(string estado)
    {
        if (grupoAcciones != null) grupoAcciones.SetActive(true);
        if (grupoLista != null) grupoLista.SetActive(false);
        if (textoEstado != null) textoEstado.text = estado;
    }

    CanvasManager Tablero => tableroElegido >= 0 ? tableros[tableroElegido] : null;

    void Exportar()
    {
        if (Tablero == null || !EsProfesorLocal()) return;
        Tablero.ExportCanvasToPNG();
        MostrarAcciones("Imagen exportada");
    }

    void Eliminar()
    {
        if (Tablero == null || !EsProfesorLocal()) return;
        Tablero.ClearCanvas();
        MostrarAcciones("Tablero borrado");
    }

    void Nueva()
    {
        if (Tablero == null || !EsProfesorLocal()) return;
        Tablero.ExportCanvasToPNG();
        Tablero.ClearCanvas();
        MostrarAcciones("Guardado y listo para empezar de nuevo");
    }

    void MostrarLista()
    {
        if (Tablero == null || !EsProfesorLocal()) return;
        archivosLista = Directory.Exists(Application.persistentDataPath)
            ? new DirectoryInfo(Application.persistentDataPath).GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(botonesLista.Length)
                .Select(f => f.FullName)
                .ToArray()
            : new string[0];

        if (archivosLista.Length == 0)
        {
            MostrarAcciones("No hay imágenes guardadas todavía");
            return;
        }
        for (int i = 0; i < botonesLista.Length; i++)
        {
            bool hay = i < archivosLista.Length;
            botonesLista[i].gameObject.SetActive(hay);
            if (hay) botonesLista[i].GetComponentInChildren<Text>(true).text = NombreBonito(archivosLista[i]);
        }
        if (grupoAcciones != null) grupoAcciones.SetActive(false);
        if (grupoLista != null) grupoLista.SetActive(true);
        if (textoEstado != null) textoEstado.text = "Elige la imagen";
    }

    void Importar(int indice)
    {
        if (Tablero == null || indice >= archivosLista.Length) return;
        byte[] png = File.ReadAllBytes(archivosLista[indice]);
        Tablero.LoadFromPNG(png);
        // En sala, el profesor (host) se la manda a todos los estudiantes.
        var sync = NetworkCanvasSync.Instance;
        if (sync != null && sync.IsReady && sync.IsServer) sync.EnviarCaraATodos(Tablero);
        MostrarAcciones("Imagen importada");
    }

    /// <summary>"Cara_1_2026-11-24_10-03-11.png" → "Cara_1 · 24/11 10:03".</summary>
    static string NombreBonito(string ruta)
    {
        var info = new FileInfo(ruta);
        string nombre = Path.GetFileNameWithoutExtension(ruta);
        int corte = nombre.IndexOf("_20");
        if (corte > 0) nombre = nombre.Substring(0, corte);
        return $"{nombre} · {info.LastWriteTime:dd/MM HH:mm}";
    }

    // =====================================================================
    // FIGURAS
    // =====================================================================
    void AlternarFiguras()
    {
        if (pantallaFiguras == null || !EsProfesorLocal()) return;
        pantallaFiguras.SetActive(!pantallaFiguras.activeSelf);
    }
}
