using System.IO;
using CuboVR.Networking;
using UnityEngine;

public class CanvasManager : MonoBehaviour
{
    [Header("Configuración del Lienzo")]
    [Tooltip("Arrastra aquí la RenderTexture que usas para esta cara del tablero")]
    public RenderTexture canvasTexture;

    [Tooltip("Nombre para identificar esta cara al guardar (ej: Cara_Frontal)")]
    public string faceName = "Cara_1";

    // Función para limpiar toda la textura (botón "Borrar").
    // En sala multijugador se borra en todos los dispositivos.
    public void ClearCanvas()
    {
        var sync = NetworkCanvasSync.Instance;
        if (sync != null && sync.IsReady && sync.RequestClear(this)) return;
        ClearLocal();
    }

    // Limpia solo en este dispositivo (lo llama la sincronización de red).
    public void ClearLocal()
    {
        if (canvasTexture == null) return;

        // Guardamos la textura activa actual para no interrumpir otros procesos
        RenderTexture currentActive = RenderTexture.active;

        // Hacemos que nuestra textura sea la activa y la limpiamos con color transparente
        RenderTexture.active = canvasTexture;
        GL.Clear(true, true, Color.clear);

        // Se retorna la textura activa original
        RenderTexture.active = currentActive;

        Debug.Log($"Lienzo {faceName} borrado.");
    }

    // Lee el lienzo actual como PNG (para exportar o enviarlo a quien entra tarde a la sala).
    public byte[] EncodeToPNG()
    {
        if (canvasTexture == null) return null;

        Texture2D tex = new Texture2D(canvasTexture.width, canvasTexture.height, TextureFormat.RGBA32, false);

        RenderTexture currentActive = RenderTexture.active;
        RenderTexture.active = canvasTexture;
        tex.ReadPixels(new Rect(0, 0, canvasTexture.width, canvasTexture.height), 0, 0);
        tex.Apply();
        RenderTexture.active = currentActive;

        byte[] bytes = tex.EncodeToPNG();
        Destroy(tex);
        return bytes;
    }

    // Reemplaza el contenido del lienzo con una imagen PNG.
    public void LoadFromPNG(byte[] png)
    {
        if (canvasTexture == null || png == null || png.Length == 0) return;
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (tex.LoadImage(png))
        {
            ClearLocal();
            Graphics.Blit(tex, canvasTexture);
        }
        Destroy(tex);
    }

    // Función para exportar la textura a un archivo PNG
    public void ExportCanvasToPNG()
    {
        byte[] bytes = EncodeToPNG();
        if (bytes == null) return;

        // Generamos la ruta y el nombre del archivo con la fecha/hora actual
        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = $"{faceName}_{timeStamp}.png";

        // Application.persistentDataPath es la ruta segura para guardar datos en cualquier plataforma (PC, Quest, etc.)
        string savePath = Path.Combine(Application.persistentDataPath, fileName);

        File.WriteAllBytes(savePath, bytes);
        Debug.Log($"Imagen guardada exitosamente en: {savePath}");
    }
}
