using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using TMPro;

public class ARTryOnManager : MonoBehaviour
{
    [Header("AR References")]
    [SerializeField] private GameObject arSessionObject;     // AR Session & AR Session Origin root object
    [SerializeField] private ARFaceManager arFaceManager;     // AR Foundation Face Manager component

    [Header("AR UI Overlays")]
    [SerializeField] private GameObject resultsPanel;         // The results panel to hide/show
    [SerializeField] private GameObject arTryOnOverlayPanel;  // UI controls while in AR mode (e.g., "Back to Results")
    [SerializeField] private TMP_Text arStatusText;

    private bool isArActive = false;

    void Start()
    {
        if (arSessionObject != null) arSessionObject.SetActive(false);
        if (arTryOnOverlayPanel != null) arTryOnOverlayPanel.SetActive(false);
    }

    // Called from ProductScanner.cs when OnARTryOnClicked() is triggered
    public void LaunchARTryOn()
    {
        Debug.Log("[ARTryOnManager] Activating AR Try-On module.");
        isArActive = true;

        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (arSessionObject != null) arSessionObject.SetActive(true);
        if (arTryOnOverlayPanel != null) arTryOnOverlayPanel.SetActive(true);

        if (arStatusText != null)
        {
            arStatusText.text = "Looking for face...";
        }
    }

    public void ExitARTryOn()
    {
        Debug.Log("[ARTryOnManager] Exiting AR Try-On module.");
        isArActive = false;

        if (arSessionObject != null) arSessionObject.SetActive(false);
        if (arTryOnOverlayPanel != null) arTryOnOverlayPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(true);
    }
}