using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class GeometricShapeSpawner : MonoBehaviour
{
    [Header("1. Referencias XR")]
    [Tooltip("Puedes dejar esto en None; el script lo buscará solo al iniciar")]
    [SerializeField] private XRInteractionManager interactionManager;
    
    [Tooltip("Arrastra aquí el 'Right Direct Interactor' de tu Right Hand")]
    [SerializeField] private XRBaseInteractor handInteractor;

    [Header("2. Prefabs 3D de tus figuras")]
    [SerializeField] private XRGrabInteractable[] shapePrefabs;

    public void SpawnShapeInHand(int shapeIndex)
    {
        // Busca el XRInteractionManager automáticamente si no fue asignado manualmente
        if (interactionManager == null)
        {
            interactionManager = FindFirstObjectByType<XRInteractionManager>();
        }

        if (shapeIndex < 0 || shapeIndex >= shapePrefabs.Length)
        {
            Debug.LogWarning("El índice de la figura no existe en la lista Shape Prefabs.");
            return;
        }

        if (interactionManager == null || handInteractor == null)
        {
            Debug.LogError("No se encontró el XRInteractionManager en la escena o falta asignar Hand Interactor.");
            return;
        }

        // Si la mano ya tiene una figura agarrada, la suelta primero
        if (handInteractor.hasSelection)
        {
            IXRSelectInteractable currentShape = handInteractor.firstInteractableSelected;
            interactionManager.SelectExit((IXRSelectInteractor)handInteractor, currentShape);
        }

        // Obtiene la posición exacta de la mano derecha
        Transform attachPoint = handInteractor.GetAttachTransform(null);

        // Crea la figura 3D directamente en la mano
        XRGrabInteractable newShape = Instantiate(
            shapePrefabs[shapeIndex],
            attachPoint.position,
            attachPoint.rotation
        );

        // Fuerza a la mano a agarrar el objeto 3D al instante
        interactionManager.SelectEnter(
            (IXRSelectInteractor)handInteractor,
            (IXRSelectInteractable)newShape
        );
    }
}