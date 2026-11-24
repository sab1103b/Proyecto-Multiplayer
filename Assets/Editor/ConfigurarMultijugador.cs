#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using CuboVR.Networking;
using CuboVR.Networking.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Arma el multijugador en la escena abierta (pensado para Start Scene 1):
///  1. NetworkManager: aprobación de conexión + escucha en toda la red (0.0.0.0).
///  2. "Connection Manager" (crear/unirse a salas + descubrimiento LAN).
///  3. "Network Systems" (figuras y borrado en red + foto del pizarrón para quien entra tarde).
///  4. Puntos de aparición: lobby, interior del cubo (profesor), exterior (estudiantes).
///  5. Zona Lobby con dos círculos: Profesor (crear sala) y Estudiante (lista de salas + código con cubos).
///     Si el Lobby ya existe se reconstruye en el mismo lugar.
///  6. Panel de sala en la muñeca izquierda (código + salir).
///  7. Rayos: el joystick acerca/aleja lo que se agarra con el rayo (ManipularConRayo en el XR Origin).
///  8. Marcadores, borrador y prefabs de figuras con NetworkObject + NetworkTransform (Owner).
/// Se puede ejecutar varias veces: reutiliza lo que ya existe. Todo se puede deshacer con Ctrl+Z
/// (excepto los cambios a prefabs de figuras y Player Settings).
/// Menú: Cubo VR > Configurar multijugador (lobby + salas)
/// </summary>
public static class ConfigurarMultijugador
{
    const string FUENTES = "Assets/UI/Fuentes/";
    const string AUDIO = "Assets/Audio/UI/";
    const string PREFABS_RED = "Assets/DefaultNetworkPrefabs.asset";
    const float U = 0.001f; // 1 unidad de canvas = 1 mm

    static readonly Color CardBg = Hex("1E242B", 0.92f);
    static readonly Color Profesor = Hex("E69D35");
    static readonly Color Estudiante = Hex("2A9D8F");
    static readonly Color Peligro = Hex("C94A4A");
    static readonly Color Gris = Hex("3A434D");
    static readonly Color TextoSuave = Hex("CDDCF0");

    static Font fBold, fSemi, fReg;
    static AudioClip sHover, sClick, sConfirmar;
    static Sprite redondeado;
    static List<string> reporte;

    [MenuItem("Cubo VR/Configurar multijugador (lobby + salas)")]
    public static void Configurar()
    {
        reporte = new List<string>();
        CargarRecursos();

        var nm = Object.FindFirstObjectByType<NetworkManager>();
        if (nm == null)
        {
            EditorUtility.DisplayDialog("Cubo VR", "No hay NetworkManager en la escena abierta. Abre Start Scene 1.", "OK");
            return;
        }

        ConfigurarNetworkManager(nm);
        var centro = CentroDelCubo(out float radioCubo);
        var conexion = CrearConnectionManager();
        CrearNetworkSystems();
        var spawns = CrearSpawnPoints(centro, radioCubo);
        CrearLobby(spawns);
        CrearPanelSala();
        ConfigurarManoLevantada();
        AgregarManipulacionConRayo();
        ConfigurarObjetosCompartidos();
        ConfigurarPrefabsFiguras();

        PlayerSettings.Android.forceInternetPermission = true;
        reporte.Add("✓ Android: permiso de Internet forzado (necesario en el visor)");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = spawns.gameObject;
        string msg = string.Join("\n", reporte);
        Debug.Log("[Cubo VR] Multijugador configurado:\n" + msg);
        EditorUtility.DisplayDialog("Cubo VR",
            msg + "\n\nRevisa la posición de 'Lobby' y de 'Spawn Points' (los gizmos naranja/turquesa) y guarda la escena con Ctrl+S.", "OK");
    }

    // =====================================================================
    // 1. NETWORK MANAGER
    // =====================================================================
    static void ConfigurarNetworkManager(NetworkManager nm)
    {
        Undo.RecordObject(nm, "Configurar multijugador");
        nm.NetworkConfig.ConnectionApproval = true;
        EditorUtility.SetDirty(nm);

        var utp = nm.GetComponent<UnityTransport>();
        if (utp != null)
        {
            Undo.RecordObject(utp, "Configurar multijugador");
            var d = utp.ConnectionData;
            d.ServerListenAddress = "0.0.0.0";
            utp.ConnectionData = d;
            EditorUtility.SetDirty(utp);
            reporte.Add($"✓ NetworkManager: aprobación activada, escucha en 0.0.0.0:{d.Port}");
        }
        else reporte.Add("✗ El NetworkManager no tiene UnityTransport");
    }

    // =====================================================================
    // 2-3. CONNECTION MANAGER Y NETWORK SYSTEMS
    // =====================================================================
    static ConnectionManager CrearConnectionManager()
    {
        var cm = Object.FindFirstObjectByType<ConnectionManager>();
        if (cm == null)
        {
            var go = new GameObject("Connection Manager");
            Undo.RegisterCreatedObjectUndo(go, "Configurar multijugador");
            go.AddComponent<RoomDiscovery>();
            cm = go.AddComponent<ConnectionManager>();
            reporte.Add("✓ Creado 'Connection Manager'");
        }
        else reporte.Add("• 'Connection Manager' ya existía");
        return cm;
    }

    static void CrearNetworkSystems()
    {
        var go = GameObject.Find("Network Systems");
        if (go == null)
        {
            go = new GameObject("Network Systems");
            Undo.RegisterCreatedObjectUndo(go, "Configurar multijugador");
        }
        if (go.GetComponent<NetworkObject>() == null) Undo.AddComponent<NetworkObject>(go);

        var shapes = go.GetComponent<NetworkShapeSpawner>();
        if (shapes == null) shapes = Undo.AddComponent<NetworkShapeSpawner>(go);
        shapes.shapeSpawner = Object.FindFirstObjectByType<GeometricShapeSpawner>(FindObjectsInactive.Include);
        if (shapes.shapeSpawner == null) reporte.Add("✗ No encontré GeometricShapeSpawner (figuras)");

        var sync = go.GetComponent<NetworkCanvasSync>();
        if (sync == null) sync = Undo.AddComponent<NetworkCanvasSync>(go);
        sync.faces = Object.FindObjectsByType<CanvasManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.canvasTexture != null)
            .OrderBy(c => c.gameObject.name)
            .ToArray();
        EditorUtility.SetDirty(shapes);
        EditorUtility.SetDirty(sync);
        reporte.Add($"✓ 'Network Systems': figuras en red + {sync.faces.Length} caras sincronizadas ({string.Join(", ", sync.faces.Select(f => f.gameObject.name))})");
    }

    // =====================================================================
    // 4. PUNTOS DE APARICIÓN
    // =====================================================================
    static Vector3 CentroDelCubo(out float radio)
    {
        var caras = Object.FindObjectsByType<CanvasManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        radio = 2f;
        if (caras.Length == 0)
        {
            reporte.Add("? No encontré las caras del cubo: puse el centro en (0,0,0), muévelo a mano");
            return Vector3.zero;
        }
        Vector3 c = Vector3.zero;
        foreach (var k in caras) c += k.transform.position;
        c /= caras.Length;
        radio = Mathf.Max(1.5f, caras.Max(k => Vector3.Distance(Flat(k.transform.position), Flat(c))));
        c.y = 0f;
        return c;
    }

    static NetworkSpawnPoints CrearSpawnPoints(Vector3 centro, float radioCubo)
    {
        var sp = Object.FindFirstObjectByType<NetworkSpawnPoints>();
        if (sp != null)
        {
            reporte.Add("• 'Spawn Points' ya existía (no lo moví)");
            return sp;
        }
        var root = new GameObject("Spawn Points");
        Undo.RegisterCreatedObjectUndo(root, "Configurar multijugador");
        sp = root.AddComponent<NetworkSpawnPoints>();

        sp.profesorSpawn = Punto(root.transform, "Profesor_Spawn (interior del cubo)", centro, Vector3.forward);

        int n = 8;
        float r = radioCubo + 2.5f;
        sp.estudianteSpawns = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector3 p = centro + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
            sp.estudianteSpawns[i] = Punto(root.transform, $"Estudiante_Spawn_{i + 1}", p, centro - p);
        }
        reporte.Add($"✓ 'Spawn Points': profesor en el centro del cubo, 8 estudiantes alrededor (radio {r:0.0} m)");
        return sp;
    }

    static Transform Punto(Transform padre, string nombre, Vector3 pos, Vector3 mirar)
    {
        var t = new GameObject(nombre).transform;
        t.SetParent(padre, false);
        t.position = pos;
        mirar.y = 0;
        if (mirar.sqrMagnitude > 0.001f) t.rotation = Quaternion.LookRotation(mirar.normalized, Vector3.up);
        return t;
    }

    // =====================================================================
    // 5. LOBBY
    // =====================================================================
    static void CrearLobby(NetworkSpawnPoints spawns)
    {
        // Si ya existe se vuelve a construir (así se aplican los cambios de diseño).
        var existente = GameObject.Find("Lobby");
        Vector3 origen = (spawns.profesorSpawn != null ? spawns.profesorSpawn.position : Vector3.zero) + new Vector3(0, 0, -25f);
        Quaternion giro = Quaternion.identity;
        if (existente != null)
        {
            origen = existente.transform.position; // se respeta donde lo hayas movido
            giro = existente.transform.rotation;
            Undo.DestroyObjectImmediate(existente);
        }

        var lobby = new GameObject("Lobby");
        Undo.RegisterCreatedObjectUndo(lobby, "Configurar multijugador");
        lobby.transform.SetPositionAndRotation(origen, giro);

        // Suelo
        var suelo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        suelo.name = "Suelo_Lobby";
        suelo.transform.SetParent(lobby.transform, false);
        suelo.transform.localScale = new Vector3(1.4f, 1, 1.4f);
        suelo.GetComponent<Renderer>().sharedMaterial = CrearMaterial("Lobby_Suelo", Hex("2B333C"));
        suelo.AddComponent<TeleportationArea>();

        // Punto de aparición (mirando hacia las zonas)
        var spawn = new GameObject("Lobby_Spawn").transform;
        spawn.SetParent(lobby.transform, false);
        spawn.localPosition = new Vector3(0, 0, -3f);
        spawn.localRotation = Quaternion.identity;
        spawns.lobbySpawn = spawn;
        EditorUtility.SetDirty(spawns);

        // Bienvenida
        var bienvenida = CanvasMundo("Canvas_Bienvenida", lobby.transform, new Vector3(0, 2.6f, 6f), 1400, 260, false);
        Txt(bienvenida, "Titulo", "CUBO VR", fBold, 110, Color.white, 0, 10, 1400, 130, TextAnchor.MiddleCenter);
        Txt(bienvenida, "Subtitulo", "Camina hacia un círculo para elegir tu rol", fReg, 52, TextoSuave, 0, 150, 1400, 80, TextAnchor.MiddleCenter);

        // Zona Profesor (izquierda) y Zona Estudiante (derecha)
        var contenidoProf = CrearZona(lobby.transform, "Zona_Profesor", new Vector3(-2.5f, 0, 2.5f), Profesor, "PROFESOR", "Crear sala");
        var contenidoEst = CrearZona(lobby.transform, "Zona_Estudiante", new Vector3(2.5f, 0, 2.5f), Estudiante, "ESTUDIANTE", "Unirse a una sala");

        ConstruirZonaProfesor(contenidoProf);
        ConstruirZonaEstudiante(contenidoEst);
        reporte.Add(existente != null
            ? "✓ 'Lobby' reconstruido en el mismo lugar"
            : "✓ 'Lobby' creado 25 m detrás del cubo (muévelo donde quieras: todo lo de adentro se mueve con él)");
    }

    const float RadioZona = 1.5f;

    /// <summary>Crea el círculo de la zona y devuelve el objeto "Contenido" (lo que aparece al entrar).</summary>
    static GameObject CrearZona(Transform lobby, string nombre, Vector3 pos, Color color, string titulo, string subtitulo)
    {
        var zona = new GameObject(nombre);
        zona.transform.SetParent(lobby, false);
        zona.transform.localPosition = pos;
        var zl = zona.AddComponent<ZonaLobby>();
        zl.radio = RadioZona;
        zl.color = color;

        // Borde del círculo: siempre visible para saber dónde está la zona.
        var borde = new GameObject("Borde");
        borde.transform.SetParent(zona.transform, false);
        borde.transform.localPosition = new Vector3(0, 0.015f, 0);
        var linea = borde.AddComponent<LineRenderer>();
        linea.useWorldSpace = false;
        linea.loop = true;
        linea.widthMultiplier = 0.035f;
        linea.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        linea.sharedMaterial = CrearMaterial("Lobby_Borde_" + titulo, color, true);
        const int puntos = 72;
        linea.positionCount = puntos;
        for (int i = 0; i < puntos; i++)
        {
            float a = i * Mathf.PI * 2f / puntos;
            linea.SetPosition(i, new Vector3(Mathf.Cos(a) * RadioZona, 0, Mathf.Sin(a) * RadioZona));
        }

        // Cartel: siempre visible
        var cartel = CanvasMundo("Cartel", zona.transform, new Vector3(0, 2.35f, 1.4f), 900, 220, false);
        var fondo = Img(cartel, "Fondo", 0, 0, 900, 220, color);
        Txt(fondo, "Titulo", titulo, fBold, 90, Color.white, 0, 10, 900, 120, TextAnchor.MiddleCenter);
        Txt(fondo, "Subtitulo", subtitulo, fSemi, 50, Color.white, 0, 125, 900, 80, TextAnchor.MiddleCenter);

        // Contenido: el área de color y los paneles (solo se ven estando dentro)
        var contenido = new GameObject("Contenido");
        contenido.transform.SetParent(zona.transform, false);
        zl.contenido = contenido;

        var area = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        area.name = "Area_Color";
        area.transform.SetParent(contenido.transform, false);
        area.transform.localScale = new Vector3(RadioZona * 2f, 0.004f, RadioZona * 2f);
        area.transform.localPosition = new Vector3(0, 0.006f, 0);
        Object.DestroyImmediate(area.GetComponent<Collider>()); // no estorba al teletransporte
        area.GetComponent<Renderer>().sharedMaterial = CrearMaterial("Lobby_Area_" + titulo, color, true);
        return contenido;
    }

    static void ConstruirZonaProfesor(GameObject contenido)
    {
        var canvas = CanvasMundo("Panel_CrearSala", contenido.transform, new Vector3(0, 1.35f, 1.4f), 700, 460, true);
        var card = Img(canvas, "Fondo", 0, 0, 700, 460, CardBg);
        Txt(card, "Titulo", "Crear sala", fBold, 56, Color.white, 40, 30, 620, 70, TextAnchor.MiddleLeft);
        Txt(card, "Descripcion", "Entrarás al cubo como profesor.\nLos estudiantes se unen con el código de la sala.",
            fReg, 30, TextoSuave, 40, 110, 620, 90, TextAnchor.UpperLeft);
        var btn = Boton(card, "Btn_CrearSala", "Crear sala", Profesor, Hex("3A2304"), 40, 220, 620, 110, 48, sConfirmar);
        var estado = Txt(card, "Txt_Estado", "", fReg, 28, TextoSuave, 40, 360, 620, 70, TextAnchor.MiddleCenter);

        var p = canvas.AddComponent<ProfessorRoomPanel>();
        p.createButton = btn;
        p.statusText = estado;
    }

    static void ConstruirZonaEstudiante(GameObject contenido)
    {
        // ---- Al frente: lista de salas en la red
        var canvas = CanvasMundo("Panel_Salas", contenido.transform, new Vector3(0, 1.6f, 1.4f), 760, 620, true);
        var card = Img(canvas, "Fondo", 0, 0, 760, 620, CardBg);
        Txt(card, "Titulo", "Unirse a una sala", fBold, 52, Color.white, 40, 26, 680, 70, TextAnchor.MiddleLeft);
        Txt(card, "Lbl_Salas", "SALAS EN LA RED  ·  o arma el código con los cubos", fSemi, 24, TextoSuave, 40, 105, 680, 40, TextAnchor.MiddleLeft);
        var lista = Vacio(card, "Lista_Salas", 40, 155, 680, 360);
        var vlg = lista.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12; vlg.childControlHeight = true; vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
        var vacio = Txt(card, "Txt_SinSalas", "Buscando salas en la red…", fReg, 28, TextoSuave, 40, 165, 680, 60, TextAnchor.MiddleLeft);

        var plantilla = Boton(lista, "Plantilla_Sala", "Sala", Estudiante, Color.white, 0, 0, 680, 80, 28, sConfirmar);
        var le = plantilla.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 80; le.minHeight = 80;
        plantilla.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
        plantilla.gameObject.SetActive(false);
        var estado = Txt(card, "Txt_Estado", "", fReg, 26, TextoSuave, 40, 530, 680, 70, TextAnchor.MiddleCenter);

        var lista_ = canvas.AddComponent<StudentRoomBrowser>();
        lista_.roomListContainer = lista.transform;
        lista_.roomButtonTemplate = plantilla;
        lista_.emptyListText = vacio;
        lista_.statusText = estado;
        lista_.digitButtons = new Button[0]; // el código se arma con cubos (CodigoConCubos)

        // ---- A la derecha: columna de botones 0-9 (cada uno suelta un cubo con su número)
        Vector3 posNumeros = new Vector3(0.62f, 1.15f, 0.85f);
        var numeros = CanvasMundo("Botones_Numeros", contenido.transform, posNumeros, 140, 1000, true);
        numeros.transform.localRotation = Quaternion.LookRotation(new Vector3(posNumeros.x, 0, posNumeros.z).normalized, Vector3.up);
        Img(numeros, "Fondo", 0, 0, 140, 1000, CardBg);
        var botones = new Button[10];
        for (int n = 0; n <= 9; n++)
            botones[n] = Boton(numeros, "Btn_" + n, n.ToString(), Estudiante, Color.white, 12, 18 + n * 96, 116, 86, 48, sClick);

        // ---- Al frente, abajo: mesa con 4 huecos
        var mesa = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesa.name = "Mesa_Codigo";
        mesa.transform.SetParent(contenido.transform, false);
        mesa.transform.localPosition = new Vector3(0, 0.9f, 0.75f);
        mesa.transform.localScale = new Vector3(0.66f, 0.04f, 0.22f);
        mesa.GetComponent<Renderer>().sharedMaterial = CrearMaterial("Lobby_Mesa", Gris);

        var huecos = new XRSocketInteractor[4];
        for (int i = 0; i < 4; i++)
        {
            var hueco = new GameObject("Hueco_" + (i + 1));
            hueco.transform.SetParent(contenido.transform, false);
            hueco.transform.localPosition = new Vector3(-0.225f + i * 0.15f, 0.965f, 0.75f);
            hueco.transform.localRotation = Quaternion.identity; // el cubo queda con su número mirando al jugador
            var col = hueco.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.07f;
            var socket = hueco.AddComponent<XRSocketInteractor>();
            socket.interactionLayers = 1 << CodigoConCubos.CapaCubosNumero;
            socket.showInteractableHoverMeshes = false;
            huecos[i] = socket;

            // Marca visual del hueco sobre la mesa
            var marca = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marca.name = "Marca";
            marca.transform.SetParent(hueco.transform, false);
            marca.transform.localPosition = new Vector3(0, -0.042f, 0);
            marca.transform.localScale = new Vector3(0.1f, 0.004f, 0.1f);
            Object.DestroyImmediate(marca.GetComponent<Collider>());
            marca.GetComponent<Renderer>().sharedMaterial = CrearMaterial("Lobby_Hueco", Estudiante, true);
        }

        // ---- Encima de la mesa: código leído + botón Unirse
        var unirseCanvas = CanvasMundo("Panel_Unirse", contenido.transform, new Vector3(0, 1.2f, 0.86f), 640, 190, true);
        unirseCanvas.transform.localRotation = Quaternion.Euler(15f, 0, 0);
        var fondoUnirse = Img(unirseCanvas, "Fondo", 0, 0, 640, 190, CardBg);
        var texto = Txt(fondoUnirse, "Txt_Codigo", "Pon un cubo en cada hueco", fSemi, 30, Color.white, 20, 8, 600, 70, TextAnchor.MiddleCenter);
        var unirse = Boton(fondoUnirse, "Btn_Unirse", "Unirse a sala", Estudiante, Color.white, 20, 90, 600, 84, 40, sConfirmar);
        unirse.interactable = false;

        var contenedor = new GameObject("Cubos");
        contenedor.transform.SetParent(contenido.transform, false);

        var codigo = contenido.AddComponent<CodigoConCubos>();
        codigo.botonesNumero = botones;
        codigo.huecos = huecos;
        codigo.botonUnirse = unirse;
        codigo.textoCodigo = texto;
        codigo.contenedorCubos = contenedor.transform;
        codigo.materialBase = CrearMaterial("Lobby_CuboNumero", Color.white);
    }

    // =====================================================================
    // 6. PANEL DE SALA (muñeca izquierda)
    // =====================================================================
    static void CrearPanelSala()
    {
        if (Object.FindFirstObjectByType<RoomStatusPanel>(FindObjectsInactive.Include) != null)
        {
            reporte.Add("• Panel de sala ya existía");
            return;
        }
        var rig = Object.FindFirstObjectByType<VRRigReferences>();
        Transform mano = rig != null ? rig.leftHand : null;

        var root = new GameObject("Panel_Sala");
        Undo.RegisterCreatedObjectUndo(root, "Configurar multijugador");
        if (mano != null)
        {
            root.transform.SetParent(mano, false);
            root.transform.localPosition = new Vector3(0, 0.06f, -0.02f);
            root.transform.localRotation = Quaternion.Euler(60, 0, 0);
        }

        var canvas = CanvasMundo("Canvas", root.transform, Vector3.zero, 420, 230, true);
        canvas.transform.localScale = Vector3.one * 0.0004f; // pequeño: va en la muñeca
        var card = Img(canvas, "Fondo", 0, 0, 420, 230, CardBg);
        var codigo = Txt(card, "Txt_Codigo", "Sala 0000", fBold, 52, Color.white, 24, 16, 372, 70, TextAnchor.MiddleLeft);
        var rol = Txt(card, "Txt_Rol", "Profesor", fReg, 32, TextoSuave, 24, 84, 372, 44, TextAnchor.MiddleLeft);
        var salir = Boton(card, "Btn_Salir", "Salir de la sala", Peligro, Color.white, 24, 140, 372, 70, 32, sClick);

        var p = root.AddComponent<RoomStatusPanel>();
        p.content = canvas;
        p.codeText = codigo;
        p.roleText = rol;
        p.leaveButton = salir;
        reporte.Add(mano != null
            ? "✓ Panel de sala (código + salir) en la muñeca izquierda"
            : "? Panel de sala creado suelto: no encontré VRRigReferences.leftHand, colócalo a mano");
    }

    // =====================================================================
    // 6b. LEVANTAR LA MANO (cola de estudiantes para entrar al cubo)
    // =====================================================================
    const string MODELO_MANO = "Assets/Modelos y Codigo/Otros/PEPL.prefab";

    [MenuItem("Cubo VR/Crear panel de mano levantada")]
    static void MenuManoLevantada()
    {
        reporte = new List<string>();
        CargarRecursos();
        ConfigurarManoLevantada();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Cubo VR", string.Join("\n", reporte) + "\n\nGuarda la escena con Ctrl+S.", "OK");
    }

    static void ConfigurarManoLevantada()
    {
        var sistemas = GameObject.Find("Network Systems");
        var spawns = Object.FindFirstObjectByType<NetworkSpawnPoints>();
        if (sistemas == null || spawns == null || spawns.profesorSpawn == null)
        {
            reporte.Add("? Mano levantada: falta 'Network Systems' o 'Spawn Points' (corre primero Configurar multijugador)");
            return;
        }
        var mano = sistemas.GetComponent<ManoLevantada>();
        if (mano == null) mano = Undo.AddComponent<ManoLevantada>(sistemas);

        // Punto dentro del cubo para el estudiante aceptado (a un lado del profesor).
        var invitado = spawns.transform.Find("Invitado_Spawn (interior del cubo)");
        if (invitado == null)
        {
            var p = spawns.profesorSpawn;
            invitado = Punto(spawns.transform, "Invitado_Spawn (interior del cubo)", p.position + p.right * 0.9f, p.forward);
            Undo.RegisterCreatedObjectUndo(invitado.gameObject, "Mano levantada");
        }
        mano.puntoInvitado = invitado;

        // Panel: se reconstruye debajo de la cara de control (Canvas_Figuras).
        //        Si ya existía, se respeta donde lo hayas movido.
        var viejo = GameObject.Find("Panel_ManoLevantada");
        Vector3 posVieja = Vector3.zero;
        Quaternion rotVieja = Quaternion.identity;
        if (viejo != null)
        {
            posVieja = viejo.transform.position;
            rotVieja = viejo.transform.rotation;
            Undo.DestroyObjectImmediate(viejo);
        }

        var root = new GameObject("Panel_ManoLevantada");
        Undo.RegisterCreatedObjectUndo(root, "Mano levantada");
        var control = GameObject.Find("Canvas_Figuras");
        if (viejo != null)
        {
            root.transform.SetPositionAndRotation(posVieja, rotVieja);
        }
        else if (control != null)
        {
            var rt = control.GetComponent<RectTransform>();
            float medioAlto = rt != null ? rt.rect.height * rt.lossyScale.y * 0.5f : 0.6f;
            // Debajo de la cara de control, un poco hacia el profesor (los canvas miran hacia -Z).
            root.transform.SetPositionAndRotation(
                control.transform.position - control.transform.up * (medioAlto + 0.4f) - control.transform.forward * 0.03f,
                control.transform.rotation);
        }
        else
        {
            var p = spawns.profesorSpawn;
            root.transform.SetPositionAndRotation(p.position + p.forward * 1.2f + Vector3.up * 1.3f,
                Quaternion.LookRotation(p.forward, Vector3.up));
            reporte.Add("? No encontré 'Canvas_Figuras': dejé el panel frente al profesor, muévelo a la cara de control");
        }

        // Diseño (sin fondo): el muñeco al centro, encima un globo de diálogo con "+N"
        // y delante dos botones redondos: X (negar) y ✓ (aceptar). Abajo el nombre del estudiante.
        // El canvas va delante del muñeco (hacia el profesor) para que los botones lo tapen un poco.
        const float W = 520, H = 660;
        var canvas = CanvasMundo("Canvas", root.transform, new Vector3(0, 0, -0.12f), W, H, true);
        var circulo = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        // Globo de diálogo (arriba a la derecha de la cabeza del muñeco)
        var globo = Img(canvas, "Globo", 290, 10, 200, 120, Color.white);
        var cola = Txt(globo, "Txt_Cola", "+1", fBold, 66, Profesor, 0, 0, 200, 120, TextAnchor.MiddleCenter);
        // Colita del globo: un cuadrado girado 45° que asoma por abajo, apuntando al muñeco.
        var colita = Img(globo, "Colita", 0, 0, 40, 40, Color.white);
        colita.transform.SetAsFirstSibling(); // detrás del texto
        colita.GetComponent<Image>().sprite = null;
        var colitaRt = (RectTransform)colita.transform;
        colitaRt.pivot = new Vector2(0.5f, 0.5f);
        colitaRt.anchoredPosition = new Vector2(55, -112);
        colitaRt.localRotation = Quaternion.Euler(0, 0, 45);

        // Botones redondos delante del muñeco
        const float D = 140;
        var negar = Boton(canvas, "Btn_Negar", "", Peligro, Color.white, 95, 330, D, D, 10, sClick);
        var aceptar = Boton(canvas, "Btn_Aceptar", "", Estudiante, Color.white, W - 95 - D, 330, D, D, 10, sConfirmar);
        foreach (var b in new[] { negar, aceptar })
        {
            var img = b.GetComponent<Image>();
            img.sprite = circulo;
            img.type = Image.Type.Simple;
        }
        // X: dos barras cruzadas
        Barra(negar.gameObject, 0, 0, 74, 16, 45);
        Barra(negar.gameObject, 0, 0, 74, 16, -45);
        // ✓ (chulo): barra corta + barra larga
        Barra(aceptar.gameObject, -18, -8, 38, 16, -45);
        Barra(aceptar.gameObject, 12, 2, 66, 16, 47);

        // Nombre de quien levantó la mano
        var etiqueta = Img(canvas, "Etiqueta", 60, 490, W - 120, 60, CardBg);
        var mensaje = Txt(etiqueta, "Txt_Mensaje", "Estudiante 1", fSemi, 32, Color.white, 0, 0, W - 120, 60, TextAnchor.MiddleCenter);

        // Sacar del cubo al estudiante que está dentro
        var retirar = Boton(canvas, "Btn_Retirar", "Sacar del cubo", Profesor, Color.white, 20, 570, W - 40, 80, 30, sClick);

        // Mini muñeco 3D al centro, detrás de los botones.
        Transform pivote = null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MODELO_MANO);
        if (prefab != null)
        {
            pivote = new GameObject("MiniModelo").transform;
            pivote.SetParent(root.transform, false);
            pivote.localPosition = new Vector3(0, 0.07f, -0.03f);
            pivote.localRotation = Quaternion.Euler(0, 180, 0); // mirando al profesor

            var modelo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            modelo.transform.SetParent(pivote, false);
            foreach (var c in modelo.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var rends = modelo.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float escala = 0.30f / Mathf.Max(0.001f, b.size.y); // ~30 cm de alto
                modelo.transform.localScale *= escala;
                // Centrar el muñeco en el pivote (así late desde su centro).
                b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                modelo.transform.position += pivote.position - b.center;

                // Los botones (el canvas) van siempre por delante del muñeco, con 5 cm de aire.
                Vector3 f = root.transform.forward;
                float medioFondo = Mathf.Abs(f.x) * b.extents.x + Mathf.Abs(f.y) * b.extents.y + Mathf.Abs(f.z) * b.extents.z;
                var pc = canvas.transform.localPosition;
                pc.z = pivote.localPosition.z - medioFondo - 0.05f;
                canvas.transform.localPosition = pc;
            }
        }
        else reporte.Add($"? No encontré el muñeco en '{MODELO_MANO}'");

        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.clip = sConfirmar;

        mano.panel = root;
        mano.miniModelo = pivote;
        mano.globo = globo;
        mano.textoCola = cola;
        mano.textoMensaje = mensaje;
        mano.botonAceptar = aceptar;
        mano.botonNegar = negar;
        mano.botonRetirar = retirar;
        mano.sonidoAviso = audio;
        EditorUtility.SetDirty(mano);
        reporte.Add("✓ Mano levantada: panel bajo la cara de control + punto para el invitado dentro del cubo");
    }

    /// <summary>Barra blanca centrada en el botón (para dibujar la X y el chulo). x, y desde el centro.</summary>
    static void Barra(GameObject boton, float x, float y, float largo, float grosor, float angulo)
    {
        var go = new GameObject("Trazo", typeof(RectTransform));
        go.layer = boton.layer;
        go.transform.SetParent(boton.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(largo, grosor);
        rt.localRotation = Quaternion.Euler(0, 0, angulo);
        var img = go.AddComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;
    }

    // =====================================================================
    // RAYOS: acercar / alejar con el joystick
    // =====================================================================
    static void AgregarManipulacionConRayo()
    {
        var origen = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origen == null) { reporte.Add("? No encontré el XR Origin para configurar los rayos"); return; }
        if (origen.GetComponent<ManipularConRayo>() == null) Undo.AddComponent<ManipularConRayo>(origen.gameObject);
        reporte.Add("✓ Rayos: joystick acerca/aleja y gira lo agarrado (pausa la locomoción de esa mano)");
    }

    // =====================================================================
    // 7. OBJETOS COMPARTIDOS
    // =====================================================================
    static void ConfigurarObjetosCompartidos()
    {
        var nombres = new[] { "Marcador 1", "Marcador 2", "Marcador 3", "Borrador" };
        var todos = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g.scene == SceneManager.GetActiveScene())
            .ToList();
        foreach (var nombre in nombres)
        {
            var go = todos.FirstOrDefault(g => g.name == nombre);
            if (go == null) { reporte.Add($"? No encontré '{nombre}'"); continue; }
            var grab = go.GetComponentInChildren<XRGrabInteractable>(true);
            if (grab == null) { reporte.Add($"? '{nombre}' no tiene XRGrabInteractable, no lo puse en red"); continue; }
            HacerCompartido(grab.gameObject, true);
            reporte.Add($"✓ '{nombre}' compartido en red");
        }
        ConfigurarPizarras();
    }

    // =====================================================================
    // 7b. PIZARRAS: SOLO PINTAN MARCADORES Y BORRADOR
    // =====================================================================
    const string CAPA_TINTA = "Tinta";

    [MenuItem("Cubo VR/Arreglar pizarras (solo pintan marcadores y borrador)")]
    static void ArreglarPizarras()
    {
        reporte = new List<string>();
        ConfigurarPizarras();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Cubo VR", string.Join("\n", reporte) + "\n\nGuarda la escena con Ctrl+S.", "OK");
    }

    /// <summary>
    /// Cada pizarra tiene una "Draw Camera" ortográfica que copia a su textura todo lo que cruza su
    /// franja delgada. Antes veía casi todas las capas, así que manos, cubos o figuras también se pintaban.
    /// Ahora los marcadores, el borrador y las propias pizarras van en la capa "Tinta" y las cámaras
    /// solo ven esa capa.
    /// </summary>
    static void ConfigurarPizarras()
    {
        int tinta = CrearCapa(CAPA_TINTA);
        if (tinta < 0) { reporte.Add("? No pude crear la capa 'Tinta' (no hay capas libres)"); return; }

        var todos = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g.scene == SceneManager.GetActiveScene())
            .ToList();

        // 1. Marcadores y borrador (con todos sus hijos) a la capa Tinta.
        foreach (var nombre in new[] { "Marcador 1", "Marcador 2", "Marcador 3", "Borrador" })
        {
            var go = todos.FirstOrDefault(g => g.name == nombre);
            if (go == null) continue;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(t.gameObject, "Capa Tinta");
                t.gameObject.layer = tinta;
            }
        }

        // 2. Las cámaras de las pizarras solo ven la capa Tinta.
        int camaras = 0;
        foreach (var cam in todos.Select(g => g.GetComponent<Camera>()).Where(c => c != null))
        {
            if (!cam.orthographic || cam.targetTexture == null || !cam.name.Contains("Draw")) continue;
            Undo.RecordObject(cam, "Cámara de pizarra");
            cam.cullingMask = 1 << tinta;
            EditorUtility.SetDirty(cam);
            camaras++;

            // La pizarra (el objeto padre de la cámara) también va en Tinta: la cámara no borra
            // entre cuadros, y lo ya pintado se conserva porque la cámara vuelve a dibujar la
            // propia pizarra con su textura. Si no la ve, cada cuadro empieza en blanco y no pinta.
            var pizarra = cam.transform.parent;
            if (pizarra != null && pizarra.GetComponent<Renderer>() != null)
            {
                Undo.RecordObject(pizarra.gameObject, "Capa Tinta");
                pizarra.gameObject.layer = tinta;
            }
        }

        // 3. El chequeo de "estoy en el suelo" del jugador ignora la capa Tinta
        //    (si no, un marcador debajo de la cabeza contaría como piso).
        foreach (var gravedad in Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(gravedad);
            var mascara = so.FindProperty("m_SphereCastLayerMask");
            if (mascara == null) continue;
            mascara.intValue &= ~(1 << tinta);
            so.ApplyModifiedProperties();
        }

        // 4. Las manos y rayos deben seguir "viendo" la capa Tinta para poder agarrar.
        //    Las manos del VR Player solo detectaban la capa Default, así que se les suma Tinta.
        int manos = 0;
        foreach (var comp in todos.SelectMany(g => g.GetComponents<MonoBehaviour>()))
        {
            if (comp == null || comp.GetType().Namespace == null ||
                !comp.GetType().Namespace.StartsWith("UnityEngine.XR.Interaction.Toolkit")) continue;
            var so = new SerializedObject(comp);
            bool cambio = false;
            foreach (var campo in new[] { "m_PhysicsLayerMask", "m_RaycastMask" })
            {
                var mascara = so.FindProperty(campo);
                if (mascara == null || mascara.propertyType != SerializedPropertyType.LayerMask) continue;
                // Solo a las que ya detectan objetos normales (Default); no tocamos máscaras especiales.
                if ((mascara.intValue & 1) == 0 || (mascara.intValue & (1 << tinta)) != 0) continue;
                mascara.intValue |= 1 << tinta;
                cambio = true;
            }
            if (cambio) { so.ApplyModifiedProperties(); manos++; }
        }

        reporte.Add($"✓ Pizarras: {camaras} cámaras solo pintan la capa 'Tinta' (marcadores y borrador)");
        reporte.Add($"✓ Manos/rayos que ahora pueden agarrar objetos de la capa 'Tinta': {manos}");
    }

    /// <summary>Devuelve el número de la capa; si no existe la crea en el primer espacio libre (8 a 31).</summary>
    static int CrearCapa(string nombre)
    {
        int existente = LayerMask.NameToLayer(nombre);
        if (existente >= 0) return existente;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var capas = tagManager.FindProperty("layers");
        for (int i = 8; i < capas.arraySize; i++)
        {
            var capa = capas.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(capa.stringValue)) continue;
            capa.stringValue = nombre;
            tagManager.ApplyModifiedProperties();
            return i;
        }
        return -1;
    }

    static void HacerCompartido(GameObject go, bool undo)
    {
        if (go.GetComponent<NetworkObject>() == null)
        {
            if (undo) Undo.AddComponent<NetworkObject>(go); else go.AddComponent<NetworkObject>();
        }
        var nt = go.GetComponent<NetworkTransform>();
        if (nt == null) nt = undo ? Undo.AddComponent<NetworkTransform>(go) : go.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        EditorUtility.SetDirty(nt);
        if (go.GetComponent<NetworkGrabOwnership>() == null)
        {
            if (undo) Undo.AddComponent<NetworkGrabOwnership>(go); else go.AddComponent<NetworkGrabOwnership>();
        }
    }

    static void ConfigurarPrefabsFiguras()
    {
        var spawner = Object.FindFirstObjectByType<GeometricShapeSpawner>(FindObjectsInactive.Include);
        if (spawner == null || spawner.ShapePrefabs == null) return;

        var lista = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PREFABS_RED);
        if (lista != null)
        {
            // Quitar entradas rotas (prefabs borrados) que causan errores al conectar.
            foreach (var roto in lista.PrefabList.Where(p => p == null || p.Prefab == null).ToList())
                lista.Remove(roto);
        }

        foreach (var fig in spawner.ShapePrefabs.Where(f => f != null))
        {
            string ruta = AssetDatabase.GetAssetPath(fig);
            if (string.IsNullOrEmpty(ruta)) { reporte.Add($"? La figura '{fig.name}' no es un prefab"); continue; }

            var contenido = PrefabUtility.LoadPrefabContents(ruta);
            HacerCompartido(contenido, false);
            PrefabUtility.SaveAsPrefabAsset(contenido, ruta);
            PrefabUtility.UnloadPrefabContents(contenido);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            if (lista != null && !lista.Contains(prefab)) lista.Add(new NetworkPrefab { Prefab = prefab });
            reporte.Add($"✓ Figura '{fig.name}' en red");
        }
        if (lista != null) EditorUtility.SetDirty(lista);
        else reporte.Add("✗ No encontré " + PREFABS_RED + ": agrega las figuras a la lista de Network Prefabs a mano");
        AssetDatabase.SaveAssets();
    }

    // =====================================================================
    // AYUDANTES DE UI
    // =====================================================================
    static void CargarRecursos()
    {
        fBold = AssetDatabase.LoadAssetAtPath<Font>(FUENTES + "Poppins-Bold.ttf");
        fSemi = AssetDatabase.LoadAssetAtPath<Font>(FUENTES + "Poppins-SemiBold.ttf");
        fReg = AssetDatabase.LoadAssetAtPath<Font>(FUENTES + "Poppins-Regular.ttf");
        var porDefecto = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (fBold == null) fBold = porDefecto;
        if (fSemi == null) fSemi = porDefecto;
        if (fReg == null) fReg = porDefecto;
        sHover = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_hover.wav");
        sClick = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_click.wav");
        sConfirmar = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_confirmar.wav");
        redondeado = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    static GameObject CanvasMundo(string nombre, Transform padre, Vector3 posLocal, float w, float h, bool interactivo)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;
        go.transform.SetParent(padre, false);
        go.transform.localPosition = posLocal;
        // Los canvas miran hacia -Z de su padre (hacia el jugador que llega desde el spawn).
        go.transform.localRotation = Quaternion.identity;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 2;
        if (interactivo)
        {
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one * U;
        return go;
    }

    static void Tam(GameObject canvas, float w, float h) => ((RectTransform)canvas.transform).sizeDelta = new Vector2(w, h);

    static RectTransform Rect(GameObject padre, string nombre, float x, float y, float w, float h, out GameObject go)
    {
        go = new GameObject(nombre, typeof(RectTransform));
        go.layer = padre.layer;
        go.transform.SetParent(padre.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static GameObject Vacio(GameObject padre, string nombre, float x, float y, float w, float h)
    {
        Rect(padre, nombre, x, y, w, h, out var go);
        return go;
    }

    static GameObject Img(GameObject padre, string nombre, float x, float y, float w, float h, Color color)
    {
        Rect(padre, nombre, x, y, w, h, out var go);
        var img = go.AddComponent<Image>();
        img.sprite = redondeado;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 0.25f; // bordes más redondeados
        img.color = color;
        img.raycastTarget = false;
        return go;
    }

    static Text Txt(GameObject padre, string nombre, string texto, Font f, int tam, Color color, float x, float y, float w, float h, TextAnchor al)
    {
        Rect(padre, nombre, x, y, w, h, out var go);
        var t = go.AddComponent<Text>();
        t.text = texto; t.font = f; t.fontSize = tam; t.color = color; t.alignment = al;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Button Boton(GameObject padre, string nombre, string texto, Color fondo, Color colTexto, float x, float y, float w, float h, int tamTexto, AudioClip clic)
    {
        var go = Img(padre, nombre, x, y, w, h, fondo);
        var img = go.GetComponent<Image>();
        img.raycastTarget = true;
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        var c = b.colors;
        c.normalColor = Color.white; c.highlightedColor = new Color(1.15f, 1.15f, 1.15f); c.pressedColor = Hex("B0B0B0");
        c.selectedColor = Color.white; c.disabledColor = new Color(1, 1, 1, 0.35f); c.fadeDuration = 0.1f; c.colorMultiplier = 1.2f;
        b.colors = c;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        var s = go.AddComponent<SonidoBoton>();
        s.sonidoApuntar = sHover; s.sonidoClic = clic;
        Txt(go, "Texto", texto, fSemi, tamTexto, colTexto, 24, 0, w - 48, h, TextAnchor.MiddleCenter);
        return b;
    }

    static Material CrearMaterial(string nombre, Color color, bool sinLuz = false)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Material")) AssetDatabase.CreateFolder("Assets", "Material");
        string ruta = $"Assets/Material/{nombre}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat != null) return mat;
        var shader = Shader.Find(sinLuz ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        AssetDatabase.CreateAsset(mat, ruta);
        return mat;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    static Color Hex(string h, float a = 1f)
    {
        ColorUtility.TryParseHtmlString("#" + h, out var c);
        c.a = a;
        return c;
    }
}
#endif
