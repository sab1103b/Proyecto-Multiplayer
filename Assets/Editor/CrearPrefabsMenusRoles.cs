#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crea los prefabs visuales del Panel del Profesor y del Panel del Estudiante
/// (solo diseño: los botones tienen el On Click vacío para que el equipo conecte las funciones).
/// Menú: Cubo VR > Crear prefabs de menús de rol
/// </summary>
public static class CrearPrefabsMenusRoles
{
    const string SPR = "Assets/UI/MenusRoles/";
    const string FUENTES = "Assets/UI/Fuentes/";
    const string AUDIO = "Assets/Audio/UI/";
    const string SALIDA = "Assets/UI/Prefabs/";
    const float PX = 4f; // pixeles de diseño por unidad de canvas

    static Font fBold, fSemi, fReg;
    static AudioClip sHover, sClick, sConfirmar, sError;

    [MenuItem("Cubo VR/Crear prefabs de menús de rol")]
    public static void Crear()
    {
        PrepararSprites();
        fBold = AssetDatabase.LoadAssetAtPath<Font>(FUENTES + "Poppins-Bold.ttf");
        fSemi = AssetDatabase.LoadAssetAtPath<Font>(FUENTES + "Poppins-SemiBold.ttf");
        fReg  = AssetDatabase.LoadAssetAtPath<Font>(FUENTES + "Poppins-Regular.ttf");
        sHover = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_hover.wav");
        sClick = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_click.wav");
        sConfirmar = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_confirmar.wav");
        sError = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO + "ui_error.wav");
        if (!AssetDatabase.IsValidFolder("Assets/UI/Prefabs")) AssetDatabase.CreateFolder("Assets/UI", "Prefabs");

        Guardar(PanelProfesor(), SALIDA + "PanelProfesor.prefab");
        Guardar(PanelEstudiante(), SALIDA + "PanelEstudiante.prefab");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Cubo VR", "Listo: se crearon PanelProfesor y PanelEstudiante en Assets/UI/Prefabs.", "OK");
    }

    // ---------------- PANEL PROFESOR ----------------
    static GameObject PanelProfesor()
    {
        var root = CanvasRaiz("PanelProfesor", 1120, 1300);
        Img(root, "Fondo", "panel_profesor", 0, 0, 1120, 1300);
        Icono(root, "Icono", "icono_persona", 90, 70, 72, "F2A93B");
        Txt(root, "Txt_Titulo", "Panel del profesor", fBold, 54, "FFFFFF", 186, 66, 600, 80, TextAnchor.MiddleLeft);
        Txt(root, "Txt_Cara", "Cara 1 de 4", fReg, 34, "F2C98A", 760, 66, 270, 80, TextAnchor.MiddleRight);

        Txt(root, "Lbl_Grabacion", "GRABACIÓN DE VOZ", fSemi, 28, "8FA3C4", 90, 190, 600, 40, TextAnchor.MiddleLeft);
        var estado = Img(root, "Estado_Grabacion", "estado_rojo", 90, 250, 560, 96);
        Icono(estado, "Punto", "icono_grabando", 30, 28, 40, "FF7A7A");
        Txt(estado, "Txt_Estado", "Grabando  00:00", fSemi, 36, "FFB3B3", 86, 8, 460, 80, TextAnchor.MiddleLeft);
        BotonRedondo(root, "Btn_Detener", "boton_redondo_rojo", "icono_detener", 770, 238, sClick);
        BotonRedondo(root, "Btn_Grabar", "boton_redondo_gris", "icono_mic", 910, 238, sClick);

        Txt(root, "Lbl_Herramientas", "HERRAMIENTAS", fSemi, 28, "8FA3C4", 90, 400, 600, 40, TextAnchor.MiddleLeft);
        Boton(root, "Btn_BorrarCara", "boton_gris", "icono_borrar", "FFFFFF", "Borrar cara", "FFFFFF", 38, 90, 456, 455, 140, sClick);
        Boton(root, "Btn_NuevoCubo", "boton_dorado", "icono_nuevo_cubo", "3A2304", "Nuevo cubo", "3A2304", 38, 575, 456, 455, 140, sClick);

        Txt(root, "Lbl_Manos", "MANOS LEVANTADAS", fSemi, 28, "8FA3C4", 90, 640, 600, 40, TextAnchor.MiddleLeft);
        var badge = Img(root, "Contador", "estado_dorado", 940, 628, 90, 64);
        Txt(badge, "Txt_Contador", "2", fBold, 36, "F2A93B", 0, 0, 90, 64, TextAnchor.MiddleCenter);

        var lista = Vacio(root, "Lista_Manos", 90, 700, 940, 480);
        for (int i = 0; i < 2; i++)
        {
            var fila = Img(lista, "Fila_Estudiante", "fila_lista", 0, i * 170, 940, 140);
            Icono(fila, "Icono_Mano", "icono_mano", 34, 34, 72, "F2A93B");
            Txt(fila, "Txt_Nombre", "Estudiante " + (i == 0 ? "1" : "3"), fSemi, 40, "FFFFFF", 130, 20, 520, 100, TextAnchor.MiddleLeft);
            BotonRedondo(fila, "Btn_Aceptar", "boton_redondo_verde", "icono_aceptar", 690, 10, sConfirmar);
            BotonRedondo(fila, "Btn_Rechazar", "boton_redondo_rojo", "icono_rechazar", 810, 10, sError);
        }
        Txt(root, "Txt_Ayuda", "Aceptar = el estudiante entra al cubo", fReg, 28, "8FA3C4", 0, 1170, 1120, 50, TextAnchor.MiddleCenter);
        return root;
    }

    // ---------------- PANEL ESTUDIANTE ----------------
    static GameObject PanelEstudiante()
    {
        var root = CanvasRaiz("PanelEstudiante", 1120, 900);
        Img(root, "Fondo", "panel_estudiante", 0, 0, 1120, 900);
        Icono(root, "Icono", "icono_persona", 90, 70, 72, "6FE3D8");
        Txt(root, "Txt_Titulo", "Estudiante", fBold, 54, "FFFFFF", 186, 66, 600, 80, TextAnchor.MiddleLeft);
        Txt(root, "Txt_Modo", "Observando", fReg, 34, "A9EFE8", 760, 66, 270, 80, TextAnchor.MiddleRight);

        Boton(root, "Btn_LevantarMano", "boton_turquesa", "icono_mano", "05302C", "Levantar la mano", "05302C", 54, 90, 230, 940, 200, sConfirmar);

        var estado = Img(root, "Estado_Solicitud", "estado_turquesa", 90, 480, 940, 110);
        Icono(estado, "Punto", "icono_grabando", 36, 37, 36, "6FE3D8");
        Txt(estado, "Txt_Estado", "Esperando respuesta del profesor…", fSemi, 36, "A9EFE8", 92, 10, 820, 90, TextAnchor.MiddleLeft);

        Boton(root, "Btn_MisNotas", "boton_gris", "icono_notas", "FFFFFF", "Mis notas", "FFFFFF", 38, 90, 640, 455, 130, sClick);
        Boton(root, "Btn_CuboPequeno", "boton_gris", "icono_cubo", "FFFFFF", "Cubo pequeño", "FFFFFF", 38, 575, 640, 455, 130, sClick);
        return root;
    }

    // ---------------- AYUDANTES ----------------
    static GameObject CanvasRaiz(string nombre, float w, float h)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 3;
        go.AddComponent<GraphicRaycaster>();
        var tdgr = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (tdgr != null) go.AddComponent(tdgr);
        else Debug.LogWarning("No se encontró TrackedDeviceGraphicRaycaster: agrégalo a mano al prefab para que funcione con el rayo VR.");
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(w / PX, h / PX);
        rt.localScale = Vector3.one * 0.0045f;
        return go;
    }

    static RectTransform Rect(GameObject padre, string nombre, float x, float y, float w, float h, out GameObject go)
    {
        go = new GameObject(nombre, typeof(RectTransform));
        go.layer = padre.layer;
        go.transform.SetParent(padre.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x / PX, -y / PX);
        rt.sizeDelta = new Vector2(w / PX, h / PX);
        return rt;
    }

    static GameObject Vacio(GameObject padre, string nombre, float x, float y, float w, float h)
    {
        Rect(padre, nombre, x, y, w, h, out var go); return go;
    }

    static GameObject Img(GameObject padre, string nombre, string sprite, float x, float y, float w, float h)
    {
        Rect(padre, nombre, x, y, w, h, out var go);
        var img = go.AddComponent<Image>();
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SPR + sprite + ".png");
        if (img.sprite == null) Debug.LogWarning("Falta el sprite " + sprite);
        img.raycastTarget = false;
        return go;
    }

    static GameObject Icono(GameObject padre, string nombre, string sprite, float x, float y, float tam, string color)
    {
        var go = Img(padre, nombre, sprite, x, y, tam, tam);
        var img = go.GetComponent<Image>(); img.color = Hex(color); img.preserveAspect = true;
        return go;
    }

    static Text Txt(GameObject padre, string nombre, string texto, Font f, int tamPx, string color, float x, float y, float w, float h, TextAnchor al)
    {
        Rect(padre, nombre, x, y, w, h, out var go);
        var t = go.AddComponent<Text>();
        t.text = texto; t.font = f; t.fontSize = Mathf.RoundToInt(tamPx / PX); t.color = Hex(color);
        t.alignment = al; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Button ConfigBoton(GameObject go, AudioClip clic)
    {
        var img = go.GetComponent<Image>(); img.raycastTarget = true;
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        var c = b.colors;
        c.normalColor = Hex("E0E0E0"); c.highlightedColor = Color.white; c.pressedColor = Hex("B0B0B0");
        c.selectedColor = Hex("E0E0E0"); c.fadeDuration = 0.1f; b.colors = c;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        var s = go.AddComponent<SonidoBoton>(); s.sonidoApuntar = sHover; s.sonidoClic = clic;
        return b;
    }

    static GameObject Boton(GameObject padre, string nombre, string sprite, string icono, string colIcono, string texto, string colTexto, int tamTexto, float x, float y, float w, float h, AudioClip clic)
    {
        var go = Img(padre, nombre, sprite, x, y, w, h);
        ConfigBoton(go, clic);
        float isz = h * 0.42f, ix = h * 0.32f;
        Icono(go, "Icono", icono, ix, (h - 8 - isz) / 2 - 2, isz, colIcono);
        Txt(go, "Texto", texto, fSemi, tamTexto, colTexto, ix + isz + 22, 0, w - (ix + isz + 22) - 40, h - 10, TextAnchor.MiddleLeft);
        return go;
    }

    static GameObject BotonRedondo(GameObject padre, string nombre, string sprite, string icono, float x, float y, AudioClip clic)
    {
        var go = Img(padre, nombre, "boton_" + sprite.Replace("boton_", ""), x, y, 120, 120);
        ConfigBoton(go, clic);
        Icono(go, "Icono", icono, 33, 29, 54, "FFFFFF");
        return go;
    }

    static void Guardar(GameObject go, string ruta)
    {
        PrefabUtility.SaveAsPrefabAsset(go, ruta);
        UnityEngine.Object.DestroyImmediate(go);
    }

    static void PrepararSprites()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SPR.TrimEnd('/') }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            if (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
        }
    }

    static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString("#" + h, out var c); return c;
    }
}
#endif
