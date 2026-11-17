
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class GeometricShapeSpawner : MonoBehaviour
{
    [Header("1. Referencias XR")]
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("2. Interactores de las manos")]
    [SerializeField] private XRBaseInteractor leftHandInteractor;
    [SerializeField] private XRBaseInteractor rightHandInteractor;

    [Header("3. Ray Interactors para detectar la selección UI")]
    [SerializeField] private XRRayInteractor leftUIRay;
    [SerializeField] private XRRayInteractor rightUIRay;

    [Header("4. Prefabs 3D")]
    [SerializeField] private XRGrabInteractable[] shapePrefabs;


    public void SpawnShapeInHand(int shapeIndex)
    {
        // Buscar Interaction Manager automáticamente
        if (interactionManager == null)
            interactionManager = FindFirstObjectByType<XRInteractionManager>();

        // Validar figura seleccionada
        if (shapePrefabs == null ||
            shapeIndex < 0 ||
            shapeIndex >= shapePrefabs.Length ||
            shapePrefabs[shapeIndex] == null)
        {
            Debug.LogWarning("Figura no válida.");
            return;
        }

        // Identificar qué mano seleccionó el botón
        XRBaseInteractor targetHand = GetActiveHandInteractor();

        if (interactionManager == null || targetHand == null)
        {
            Debug.LogError("No se pudo identificar la mano o el Interaction Manager.");
            return;
        }

        // Soltar figura anterior de esa mano
        if (targetHand.hasSelection)
        {
            IXRSelectInteractable currentShape =
                targetHand.firstInteractableSelected;

            interactionManager.SelectExit(
                (IXRSelectInteractor)targetHand,
                currentShape
            );
        }

        // Obtener punto de agarre de la mano
        Transform attachPoint = targetHand.GetAttachTransform(null);

        // Crear figura directamente en la mano
        XRGrabInteractable newShape = Instantiate(
            shapePrefabs[shapeIndex],
            attachPoint.position,
            attachPoint.rotation
        );

        // Agarrar figura con la mano identificada
        interactionManager.SelectEnter(
            (IXRSelectInteractor)targetHand,
            (IXRSelectInteractable)newShape
        );

        Debug.Log("Figura generada en: " + targetHand.name);
    }


    private XRBaseInteractor GetActiveHandInteractor()
    {
        GameObject selectedButton =
            EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;

        // 1. Comprobar primero la mano IZQUIERDA
        if (selectedButton != null)
        {
            if (IsHandPointingAt(leftUIRay, selectedButton))
                return leftHandInteractor;

            if (IsHandPointingAt(rightUIRay, selectedButton))
                return rightHandInteractor;
        }

        // 2. Respaldo: comprobar botones bajo el Ray izquierdo
        if (IsHandOverAnyButton(leftUIRay))
            return leftHandInteractor;

        // 3. Comprobar botones bajo el Ray derecho
        if (IsHandOverAnyButton(rightUIRay))
            return rightHandInteractor;

        // 4. Si no detecta ninguno, utilizar la derecha
        return rightHandInteractor;
    }


    private bool IsHandPointingAt(
        XRRayInteractor ray,
        GameObject targetButton)
    {
        if (ray == null || targetButton == null)
            return false;

        if (!ray.TryGetUIModel(out TrackedDeviceModel model))
            return false;

        if (!model.currentRaycast.isValid ||
            model.currentRaycast.gameObject == null)
            return false;

        Button hitButton =
            model.currentRaycast.gameObject.GetComponentInParent<Button>();

        Button selected =
            targetButton.GetComponentInParent<Button>();

        return hitButton != null && hitButton == selected;
    }


    private bool IsHandOverAnyButton(XRRayInteractor ray)
    {
        if (ray == null)
            return false;

        if (!ray.TryGetUIModel(out TrackedDeviceModel model))
            return false;

        if (!model.currentRaycast.isValid ||
            model.currentRaycast.gameObject == null)
            return false;

        return model.currentRaycast.gameObject
            .GetComponentInParent<Button>() != null;
    }
}
