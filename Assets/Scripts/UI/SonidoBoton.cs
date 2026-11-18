using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Agrega sonido a un botón: uno al apuntarlo (hover) y otro al presionarlo.
/// Solo reproduce sonidos; no cambia lo que hace el botón (su On Click sigue igual).
/// Funciona con el rayo de los controles VR (XR UI) y con el mouse.
/// </summary>
[RequireComponent(typeof(Button))]
public class SonidoBoton : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Tooltip("Sonido al apuntar el botón (ej: ui_hover)")]
    public AudioClip sonidoApuntar;

    [Tooltip("Sonido al presionar el botón (ej: ui_click)")]
    public AudioClip sonidoClic;

    [Range(0f, 1f)]
    public float volumen = 0.7f;

    [Tooltip("Volumen del sonido al apuntar (más bajo que el clic para que no canse)")]
    [Range(0f, 1f)]
    public float volumenApuntar = 0.35f;

    static AudioSource fuente;
    Button boton;

    void Awake()
    {
        boton = GetComponent<Button>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (sonidoApuntar != null && boton.interactable)
            Fuente().PlayOneShot(sonidoApuntar, volumenApuntar);
    }

    // Se ejecuta aparte del On Click, así suena aunque la función del botón falle.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (sonidoClic != null && boton.interactable)
            Fuente().PlayOneShot(sonidoClic, volumen);
    }

    // Un solo AudioSource compartido por todos los botones (se crea solo).
    static AudioSource Fuente()
    {
        if (fuente == null)
        {
            var go = new GameObject("Sonidos UI");
            fuente = go.AddComponent<AudioSource>();
            fuente.playOnAwake = false;
            fuente.spatialBlend = 0f; // 2D: se escucha igual en cualquier lugar
        }
        return fuente;
    }
}
