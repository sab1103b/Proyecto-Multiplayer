using CuboVR.Networking;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Zona circular del lobby (Profesor o Estudiante).
/// Afuera solo se ve el borde del círculo y el cartel. Cuando la cabeza del jugador entra al círculo,
/// se muestra el "contenido": el área de color y los paneles de esa zona.
/// </summary>
public class ZonaLobby : MonoBehaviour
{
    [Tooltip("Radio del círculo en metros.")]
    public float radio = 1.5f;

    [Tooltip("Color de la zona (solo para verla en el editor).")]
    public Color color = Color.white;

    [Tooltip("Lo que aparece al entrar: el área de color y los paneles.")]
    public GameObject contenido;

    Transform cabeza;
    bool dentro;

    void Start()
    {
        if (contenido != null) contenido.SetActive(false);
    }

    void Update()
    {
        if (cabeza == null)
        {
            var origen = FindFirstObjectByType<XROrigin>();
            if (origen != null && origen.Camera != null) cabeza = origen.Camera.transform;
            else if (Camera.main != null) cabeza = Camera.main.transform;
            if (cabeza == null) return;
        }

        // Dentro de una sala ya no se muestran las zonas del lobby.
        var conexion = ConnectionManager.Instance;
        bool enLobby = conexion == null || conexion.State != ConnectionState.EnSala;

        // Distancia en el piso (sin contar la altura) entre la cabeza y el centro del círculo.
        Vector3 diferencia = cabeza.position - transform.position;
        diferencia.y = 0f;
        bool ahoraDentro = enLobby && diferencia.magnitude <= radio;

        if (ahoraDentro == dentro) return;
        dentro = ahoraDentro;
        if (contenido != null) contenido.SetActive(dentro);
    }

    // Dibuja el círculo en la vista de escena.
    void OnDrawGizmos()
    {
        Gizmos.color = color;
        const int lados = 48;
        Vector3 anterior = transform.position + new Vector3(radio, 0.02f, 0);
        for (int i = 1; i <= lados; i++)
        {
            float a = i * Mathf.PI * 2f / lados;
            Vector3 punto = transform.position + new Vector3(Mathf.Cos(a) * radio, 0.02f, Mathf.Sin(a) * radio);
            Gizmos.DrawLine(anterior, punto);
            anterior = punto;
        }
    }
}
