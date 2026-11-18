#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Aplica el estilo visual (Cubo VR) y los sonidos a los menús de la escena abierta:
/// Connection Canvas (rol), Borrar, Exportar y Canvas_Figuras.
/// Solo cambia lo visual: no toca On Click ni scripts del equipo. Se puede deshacer con Ctrl+Z.
/// Menú: Cubo VR > Aplicar estilo UI a la escena abierta
/// </summary>
public static class AplicarEstiloUI
{
    const string ROL = "Assets/UI/MenuRol/";
    const string HER = "Assets/UI/MenuHerramientas/";
    const string FUENTES = "Assets/UI/Fuentes/";
    const string AUDIO = "Assets/Audio/";

    static List<string> reporte;

    [MenuItem("Cubo VR/Aplicar estilo UI a la escena abierta")]
    public static void Aplicar()
    {
        reporte = new List<string>();
        PrepararSprites(ROL); PrepararSprites(HER);
        var todos = TodosLosObjetos();

        // ---- Menú de rol ----
        var rol = todos.FirstOrDefault(g => g.name == "Connection Canvas");
        if (rol != null) EstiloRol(rol); else reporte.Add("✗ No encontré 'Connection Canvas' (menú de rol)");

        // ---- Borrar / Exportar: se reconocen por el texto del encabezado ----
        foreach (var c in todos.Where(g => g.name.StartsWith("Connection Canvas (")))
        {
            var header = Hijo(c, "Header Text")?.GetComponent<Text>();
            string t = header != null ? header.text.ToLower() : "";
            if (t.Contains("borrar")) EstiloHerramienta(c, "borrar", "FFFFFF");
            else if (t.Contains("export")) EstiloHerramienta(c, "exportar", "05302C");
            else reporte.Add("? '" + c.name + "' no dice Borrar ni Exportar en su Header Text, lo dejé igual");
        }

        // ---- Figuras ----
        var fig = todos.FirstOrDefault(g => g.name == "Canvas_Figuras");
        if (fig != null) EstiloFiguras(fig); else reporte.Add("✗ No encontré 'Canvas_Figuras'");

        // ---- Sonido ambiente ----
        SonidoAmbiente(todos);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        string msg = string.Join("\n", reporte);
        Debug.Log("[Cubo VR] Estilo aplicado:\n" + msg);
        EditorUtility.DisplayDialog("Cubo VR", msg + "\n\nRevisa la escena y guarda con Ctrl+S.", "OK");
    }

    // ================= MENÚ DE ROL =================
    static void EstiloRol(GameObject c)
    {
        Undo.RegisterFullObjectHierarchyUndo(c, "Estilo UI");
        FondoPanel(c, ROL + "menu_panel.png");

        var header = Hijo(c, "Header Text");
        if (header != null)
        {
            var t = header.GetComponent<Text>();
            t.text = "Elige tu rol para comenzar"; t.font = F("Poppins-Regular"); t.fontSize = 14;
            t.color = Hex("CDDCF0"); t.alignment = TextAnchor.MiddleCenter;
            PonerRect(header, 0, 58, 260, 30);
        }
        Titulo(c, ROL + "titulo_cubo_vr.png", 0, 96, 190, 40);

        BotonRol(Hijo(c, "Button 2"), 8, "boton_profesor", "Profesor\n<size=11>Crear sala</size>", "3A2304");
        BotonRol(Hijo(c, "Button 1"), -66, "boton_estudiante", "Estudiante\n<size=11>Unirse a la sala</size>", "05302C");
        reporte.Add("✓ Menú de rol");
    }

    static void BotonRol(GameObject b, float y, string sprite, string texto, string color)
    {
        if (b == null) { reporte.Add("✗ Falta un botón del menú de rol"); return; }
        PonerRect(b, 0, y, 260, 62);
        ColoresClaros(b);
        ImagenYTexto(b, ROL + sprite + ".png", texto, color, TextAnchor.MiddleLeft, 18, 0.75f, 150, 56);
        Sonido(b, "ui_confirmar");
    }

    // ================= BORRAR / EXPORTAR =================
    static void EstiloHerramienta(GameObject c, string tipo, string colorTexto)
    {
        Undo.RegisterFullObjectHierarchyUndo(c, "Estilo UI");
        FondoPanel(c, HER + "panel_" + tipo + ".png");
        var header = Hijo(c, "Header Text"); if (header != null) header.SetActive(false);
        Titulo(c, HER + "titulo_" + tipo + ".png", 0, 100, 120, 30);

        float[] ys = { 42, -20, -82 };
        for (int i = 0; i < 3; i++)
        {
            var b = Hijo(c, "Button " + (i + 1));
            if (b == null) continue;
            PonerRect(b, 0, ys[i], 260, 56);
            ColoresClaros(b);
            ImagenYTexto(b, HER + "boton_" + tipo + ".png", null, colorTexto, TextAnchor.MiddleCenter, 18, 1f, 150, 40);
            Sonido(b, tipo == "borrar" ? "ui_borrar" : "ui_exportar");
        }
        reporte.Add("✓ Menú " + (tipo == "borrar" ? "Borrar" : "Exportar") + " (" + c.name + ")");
    }

    // ================= FIGURAS =================
    static void EstiloFiguras(GameObject c)
    {
        Undo.RegisterFullObjectHierarchyUndo(c, "Estilo UI");
        FondoPanel(c, HER + "panel_figuras.png");
        foreach (Transform t in c.transform)
            if (t.name == "Text (TMP)") t.gameObject.SetActive(false);
        Titulo(c, HER + "titulo_figuras.png", 0, 431, 530, 120);

        int n = 0;
        foreach (var b in c.GetComponentsInChildren<Button>(true))
        {
            var col = b.colors;
            col.normalColor = Color.white; col.highlightedColor = Hex("FFC861");
            col.pressedColor = Hex("C9822A"); col.selectedColor = Color.white;
            b.colors = col;
            Sonido(b.gameObject, "ui_click"); n++;
        }
        if (c.GetComponent<GraphicRaycaster>() == null) Undo.AddComponent<GraphicRaycaster>(c);
        reporte.Add("✓ Menú Figuras (" + n + " botones)");
    }

    // ================= SONIDO AMBIENTE =================
    static void SonidoAmbiente(List<GameObject> todos)
    {
        var go = todos.FirstOrDefault(g => g.name == "Sonido Ambiente");
        if (go == null)
        {
            go = new GameObject("Sonido Ambiente");
            Undo.RegisterCreatedObjectUndo(go, "Estilo UI");
        }
        var a = go.GetComponent<AudioSource>();
        if (a == null) a = Undo.AddComponent<AudioSource>(go);
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "Ambiente/ambiente_noche.ogg");
        a.playOnAwake = true; a.loop = true; a.volume = 0.35f; a.spatialBlend = 0f;
        reporte.Add(a.clip != null ? "✓ Sonido ambiente" : "✗ No encontré ambiente_noche.ogg");
    }

    // ================= AYUDANTES =================
    static void FondoPanel(GameObject c, string ruta)
    {
        var p = Hijo(c, "Panel"); if (p == null) return;
        var img = p.GetComponent<Image>(); if (img == null) return;
        img.sprite = S(ruta); img.color = Color.white; img.type = Image.Type.Simple;
    }

    static void Titulo(GameObject c, string ruta, float x, float y, float w, float h)
    {
        var t = Hijo(c, "Titulo");
        if (t == null)
        {
            t = new GameObject("Titulo", typeof(RectTransform), typeof(Image));
            t.layer = c.layer;
            Undo.RegisterCreatedObjectUndo(t, "Estilo UI");
            t.transform.SetParent(c.transform, false);
        }
        t.transform.SetAsLastSibling();
        var img = t.GetComponent<Image>(); img.sprite = S(ruta); img.preserveAspect = true; img.raycastTarget = false;
        PonerRect(t, x, y, w, h);
    }

    static void ColoresClaros(GameObject b)
    {
        var btn = b.GetComponent<Button>(); if (btn == null) return;
        var col = btn.colors;
        col.normalColor = Hex("E0E0E0"); col.highlightedColor = Color.white;
        col.pressedColor = Hex("B0B0B0"); col.selectedColor = Hex("E0E0E0");
        btn.colors = col;
    }

    // Estructura de los botones del equipo: Button > Image > (Text, Outline)
    static void ImagenYTexto(GameObject b, string sprite, string texto, string color, TextAnchor al, int tam, float interlineado, float w, float h)
    {
        var image = Hijo(b, "Image");
        if (image != null)
        {
            var img = image.GetComponent<Image>(); if (img != null) img.sprite = S(sprite);
            var outline = Hijo(image, "Outline"); if (outline != null) outline.SetActive(false);
        }
        var txtGo = image != null ? Hijo(image, "Text") : Hijo(b, "Text");
        var t = txtGo != null ? txtGo.GetComponent<Text>() : null;
        if (t == null) return;
        if (texto != null) t.text = texto;
        t.font = F("Poppins-SemiBold"); t.fontSize = tam; t.color = Hex(color);
        t.alignment = al; t.lineSpacing = interlineado; t.supportRichText = true;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        PonerRect(txtGo, 30, 0, w, h);
    }

    static void Sonido(GameObject b, string clic)
    {
        if (b.GetComponent<Button>() == null) return;
        var s = b.GetComponent<SonidoBoton>();
        if (s == null) s = Undo.AddComponent<SonidoBoton>(b);
        s.sonidoApuntar = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "UI/ui_hover.wav");
        s.sonidoClic = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "UI/" + clic + ".wav");
    }

    static void PonerRect(GameObject go, float x, float y, float w, float h)
    {
        var rt = go.transform as RectTransform; if (rt == null) return;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition3D = new Vector3(x, y, 0);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
    }

    static GameObject Hijo(GameObject padre, string nombre)
    {
        foreach (Transform t in padre.transform) if (t.name == nombre) return t.gameObject;
        return null;
    }

    static List<GameObject> TodosLosObjetos()
    {
        var lista = new List<GameObject>();
        foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) lista.Add(t.gameObject);
        return lista;
    }

    static Sprite S(string ruta)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
        if (s == null) reporte.Add("✗ Falta el sprite " + ruta);
        return s;
    }

    static Font F(string n) => AssetDatabase.LoadAssetAtPath<Font>(FUENTES + n + ".ttf");

    static void PrepararSprites(string carpeta)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { carpeta.TrimEnd('/') }))
        {
            var ti = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if (ti == null) continue;
            if (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
        }
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
}
#endif
