using System.Collections.Generic;
using System.Linq;
using CuboVR.Networking;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Ingreso del código de sala con cubos (zona Estudiante):
///  1. Al pulsar un botón del 0 al 9 sale un cubo pequeño con ese número.
///  2. El estudiante agarra el cubo y lo pone en uno de los 4 huecos (se queda fijo ahí).
///  3. Con los 4 huecos llenos se lee el código y se habilita el botón "Unirse a sala".
/// Los cubos que se dejan sueltos desaparecen solos después de un rato.
/// </summary>
public class CodigoConCubos : MonoBehaviour
{
    // Capa de interacción que solo tienen los cubos de número: así los huecos no aceptan otros objetos.
    public const int CapaCubosNumero = 30;
    // Capa que usan los rayos de las manos (en el prefab VR Player los rayos solo miran la capa 1).
    // Los cubos la tienen para poder agarrarlos de lejos con el rayo.
    public const int CapaRayo = 1;

    [Header("Botones 0-9 (posición = número)")]
    public Button[] botonesNumero = new Button[10];

    [Header("Huecos para los cubos (de izquierda a derecha)")]
    public XRSocketInteractor[] huecos = new XRSocketInteractor[4];

    [Header("Unirse")]
    public Button botonUnirse;
    public Text textoCodigo;

    [Header("Cubos")]
    [Tooltip("Material base (URP Lit) que se copia para cada número.")]
    public Material materialBase;
    [Tooltip("Dónde se guardan los cubos creados (se ocultan con la zona).")]
    public Transform contenedorCubos;
    public float tamanoCubo = 0.08f;
    [Tooltip("Qué tan lejos del botón aparece el cubo (hacia el jugador).")]
    public float distanciaSalida = 0.12f;
    [Tooltip("Segundos que dura un cubo suelto (ni en la mano ni en un hueco).")]
    public float segundosCuboSuelto = 25f;
    [Tooltip("Máximo de cubos sueltos a la vez; si hay más, se borra el más viejo.")]
    public int maximoCubosSueltos = 8;

    // Número de cada cubo y última vez que se usó.
    readonly Dictionary<XRGrabInteractable, int> numeroDelCubo = new Dictionary<XRGrabInteractable, int>();
    readonly Dictionary<XRGrabInteractable, float> ultimoUso = new Dictionary<XRGrabInteractable, float>();
    Material[] materialesNumero;
    string codigoActual = "";
    bool hayQueLeer;

    void Awake()
    {
        for (int i = 0; i < botonesNumero.Length; i++)
        {
            if (botonesNumero[i] == null) continue;
            int numero = i;
            botonesNumero[i].onClick.AddListener(() => CrearCubo(numero));
        }
        foreach (var hueco in huecos)
        {
            if (hueco == null) continue;
            hueco.interactionLayers = 1 << CapaCubosNumero;
            // Se lee en el siguiente frame, cuando el hueco ya actualizó qué cubo tiene.
            hueco.selectEntered.AddListener(_ => hayQueLeer = true);
            hueco.selectExited.AddListener(_ => hayQueLeer = true);
        }
        if (botonUnirse != null) botonUnirse.onClick.AddListener(Unirse);
        LeerCodigo();
    }

    void OnDisable()
    {
        // Si ya entró a la sala, se limpian los cubos para la próxima vez que vuelva al lobby.
        var conexion = ConnectionManager.Instance;
        if (conexion != null && conexion.State == ConnectionState.EnSala) BorrarTodosLosCubos();
    }

    // ---------------- 1. Crear cubo ----------------
    void CrearCubo(int numero)
    {
        Transform boton = botonesNumero[numero].transform;
        // Los canvas miran hacia -Z: el cubo sale hacia el jugador.
        Vector3 posicion = boton.position - boton.forward * distanciaSalida;

        var cubo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubo.name = "Cubo_" + numero;
        cubo.transform.SetParent(contenedorCubos != null ? contenedorCubos : transform, true);
        // La cara frontal del cubo (-Z) queda mirando al jugador, igual que el botón.
        cubo.transform.SetPositionAndRotation(posicion, Quaternion.LookRotation(boton.forward, Vector3.up));
        cubo.transform.localScale = Vector3.one * tamanoCubo;
        cubo.GetComponent<Renderer>().sharedMaterial = MaterialDelNumero(numero);

        // Flota en el aire (sin gravedad) y se frena solo.
        var cuerpo = cubo.AddComponent<Rigidbody>();
        cuerpo.useGravity = false;
        cuerpo.linearDamping = 4f;
        cuerpo.angularDamping = 4f;

        var agarre = cubo.AddComponent<XRGrabInteractable>();
        agarre.movementType = XRBaseInteractable.MovementType.Instantaneous;
        agarre.throwOnDetach = false;
        // Capa 0 (Default) = agarre directo con la mano; capa del rayo = agarre de lejos;
        // capa de cubos = los huecos lo aceptan.
        agarre.interactionLayers = 1 | (1 << CapaRayo) | (1 << CapaCubosNumero);
        // Al agarrarlo con el rayo, el cubo se queda en la punta del rayo (no salta a la mano).
        agarre.useDynamicAttach = true;
        agarre.selectEntered.AddListener(_ => ultimoUso[agarre] = Time.time);
        agarre.selectExited.AddListener(_ => ultimoUso[agarre] = Time.time);

        numeroDelCubo[agarre] = numero;
        ultimoUso[agarre] = Time.time;
        cuerpo.linearVelocity = -boton.forward * 0.25f; // pequeño "salto" hacia afuera

        LimitarCubosSueltos();
    }

    // ---------------- 2. Limpiar cubos sueltos ----------------
    void Update()
    {
        if (hayQueLeer)
        {
            hayQueLeer = false;
            LeerCodigo();
        }

        List<XRGrabInteractable> paraBorrar = null;
        foreach (var par in ultimoUso)
        {
            var cubo = par.Key;
            if (cubo == null) { (paraBorrar ??= new List<XRGrabInteractable>()).Add(cubo); continue; }
            // isSelected = está en una mano o en un hueco.
            if (!cubo.isSelected && Time.time - par.Value > segundosCuboSuelto)
                (paraBorrar ??= new List<XRGrabInteractable>()).Add(cubo);
        }
        if (paraBorrar != null) foreach (var c in paraBorrar) BorrarCubo(c);
    }

    void LimitarCubosSueltos()
    {
        var sueltos = ultimoUso.Where(p => p.Key != null && !p.Key.isSelected)
                               .OrderBy(p => p.Value).Select(p => p.Key).ToList();
        for (int i = 0; i < sueltos.Count - maximoCubosSueltos; i++) BorrarCubo(sueltos[i]);
    }

    void BorrarCubo(XRGrabInteractable cubo)
    {
        numeroDelCubo.Remove(cubo);
        ultimoUso.Remove(cubo);
        if (cubo != null) Destroy(cubo.gameObject);
    }

    void BorrarTodosLosCubos()
    {
        foreach (var cubo in ultimoUso.Keys.ToList()) BorrarCubo(cubo);
        LeerCodigo();
    }

    // ---------------- 3. Leer el código ----------------
    void LeerCodigo()
    {
        codigoActual = "";
        bool completo = true;
        foreach (var hueco in huecos)
        {
            var cubo = hueco != null && hueco.hasSelection
                ? hueco.firstInteractableSelected as XRGrabInteractable
                : null;
            if (cubo != null && numeroDelCubo.TryGetValue(cubo, out int numero))
                codigoActual += numero.ToString();
            else
            {
                codigoActual += "_";
                completo = false;
            }
        }

        if (textoCodigo != null)
            textoCodigo.text = completo ? "Código: " + string.Join(" ", codigoActual.ToCharArray())
                                        : "Pon un cubo en cada hueco   " + string.Join(" ", codigoActual.ToCharArray());
        if (botonUnirse != null) botonUnirse.interactable = completo;
        if (!completo) codigoActual = "";
    }

    void Unirse()
    {
        if (codigoActual.Length != huecos.Length || ConnectionManager.Instance == null) return;
        ConnectionManager.Instance.JoinByCode(codigoActual);
    }

    // ---------------- Textura con el número ----------------
    Material MaterialDelNumero(int numero)
    {
        if (materialesNumero == null) materialesNumero = new Material[10];
        if (materialesNumero[numero] != null) return materialesNumero[numero];

        var material = materialBase != null ? new Material(materialBase) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.mainTexture = DibujarNumero(numero);
        material.color = Color.white;
        materialesNumero[numero] = material;
        return material;
    }

    // Cada número dibujado en una cuadrícula de 5x7 (1 = pintado).
    static readonly string[][] Digitos =
    {
        new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" }, // 0
        new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" }, // 1
        new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" }, // 2
        new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" }, // 3
        new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" }, // 4
        new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" }, // 5
        new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" }, // 6
        new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" }, // 7
        new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" }, // 8
        new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" }, // 9
    };

    static Texture2D DibujarNumero(int numero)
    {
        const int tam = 64, escala = 6, borde = 3;
        Color fondo = new Color(0.95f, 0.97f, 1f);
        Color tinta = new Color(0.12f, 0.14f, 0.17f);
        Color marco = new Color(0.16f, 0.62f, 0.56f); // turquesa del estudiante

        var tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Numero_" + numero };
        var pixeles = new Color[tam * tam];
        for (int y = 0; y < tam; y++)
            for (int x = 0; x < tam; x++)
                pixeles[y * tam + x] = (x < borde || y < borde || x >= tam - borde || y >= tam - borde) ? marco : fondo;

        string[] filas = Digitos[numero];
        int x0 = (tam - 5 * escala) / 2, y0 = (tam - 7 * escala) / 2;
        for (int f = 0; f < 7; f++)
            for (int c = 0; c < 5; c++)
            {
                if (filas[f][c] != '1') continue;
                for (int dy = 0; dy < escala; dy++)
                    for (int dx = 0; dx < escala; dx++)
                    {
                        // El cubo de Unity tiene la cara frontal (-Z, la que mira al jugador)
                        // con la textura girada 180°, así que dibujamos el número ya girado:
                        // la columna 0 va a la derecha y la fila 0 abajo.
                        int x = x0 + (4 - c) * escala + dx;
                        int y = y0 + f * escala + dy;
                        pixeles[y * tam + x] = tinta;
                    }
            }
        tex.SetPixels(pixeles);
        tex.Apply();
        return tex;
    }
}
