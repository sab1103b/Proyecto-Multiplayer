# 04. SCRIPTS BASE Y ARQUITECTURA DE CÓDIGO EN UNITY (C#)

## 1. Convenciones de Proyecto
- **Namespace / Carpetas:** `Assets/Scripts/` (`Core/`, `Drawing/`, `Networking/`, `UI/`).
- **Dependencias:** `Unity.XR.Interaction.Toolkit`, `Unity.Netcode.Components`, `UnityEngine.XR`.

## 2. Scripts Implementados (Base Actual)

### `CanvasManager.cs` (Limpieza y Exportación de RenderTexture por Cara)
Ubicado en cada superficie de dibujo del cubo para gestionar su memoria gráfica y exportación a PNG:
```csharp
using System;
using System.IO;
using UnityEngine;

public class CanvasManager : MonoBehaviour
{
    [Header("Configuración del Lienzo")]
    [Tooltip("Arrastra aquí la RenderTexture que usas para esta cara del tablero")]
    public RenderTexture canvasTexture;

    [Tooltip("Nombre para identificar esta cara al guardar (ej. Cara_Frontal)")]
    public string faceName = "Cara_1";

    // Función para limpiar toda la textura (hacerla transparente)
    public void ClearCanvas()
    {
        if (canvasTexture == null) return;

        // Guardamos la textura activa actual para no interrumpir otros procesos
        RenderTexture currentActive = RenderTexture.active;

        // Hacemos que nuestra textura sea la activa y la limpiamos con color transparente
        RenderTexture.active = canvasTexture;
        GL.Clear(true, true, Color.clear);

        // Restauramos la textura activa previa
        RenderTexture.active = currentActive;
    }

    // Función para exportar el contenido actual de la RenderTexture a PNG
    public void ExportCanvasToPNG()
    {
        if (canvasTexture == null) return;

        RenderTexture currentActive = RenderTexture.active;
        RenderTexture.active = canvasTexture;

        Texture2D tex = new Texture2D(canvasTexture.width, canvasTexture.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, canvasTexture.width, canvasTexture.height), 0, 0);
        tex.Apply();

        RenderTexture.active = currentActive;

        byte[] bytes = tex.EncodeToPNG();
        Destroy(tex);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{faceName}_{timestamp}.png";
        string path = Path.Combine(Application.persistentDataPath, fileName);

        File.WriteAllBytes(path, bytes);
        Debug.Log($"[CuboVR] Lienzo exportado exitosamente en: {path}");
    }
}
```
