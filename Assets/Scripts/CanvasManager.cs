
using System.IO;
using UnityEngine;

public class CanvasManager : MonoBehaviour
{
    [Header("Configuración del Lienzo")]
    [Tooltip("Arrastra aquí la RenderTexture que usas para esta cara del tablero")]
    public RenderTexture canvasTexture;

    [Tooltip("Nombre para identificar esta cara al guardar (ej: Cara_Frontal)")]
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

        // Se retorna la textura activa original
        RenderTexture.active = currentActive;

        UnityEngine.Debug.Log($"Lienzo {faceName} borrado.");
    }

    // Función para exportar la textura a un archivo PNG
    public void ExportCanvasToPNG()
    {
        if (canvasTexture == null) return;

        // Creamos una Texture2D temporal con las mismas dimensiones
        Texture2D tex = new Texture2D(canvasTexture.width, canvasTexture.height, TextureFormat.RGBA32, false);

        RenderTexture currentActive = RenderTexture.active;
        RenderTexture.active = canvasTexture;

        tex.ReadPixels(new Rect(0, 0, canvasTexture.width, canvasTexture.height), 0, 0);
        tex.Apply();

        RenderTexture.active = currentActive;

        // Codificamos a formato PNG
        byte[] bytes = tex.EncodeToPNG();

        Destroy(tex);

        // Generamos la ruta y el nombre del archivo con la fecha/hora actual
        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = $"{faceName}_{timeStamp}.png";

        // Application.persistentDataPath es la ruta segura para guardar datos en cualquier plataforma (PC, Quest, etc.)
        string savePath = Path.Combine(Application.persistentDataPath, fileName);

        File.WriteAllBytes(savePath, bytes);
        UnityEngine.Debug.Log($"Imagen guardada exitosamente en: {savePath}");
    }
}