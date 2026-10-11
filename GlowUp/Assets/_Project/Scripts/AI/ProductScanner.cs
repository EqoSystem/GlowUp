using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using GlowUp.Scanning;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class ProductScanner : MonoBehaviour


{
    [Header("AR Module Reference")]
    [SerializeField] private ARTryOnManager arTryOnManager;

    [Header("Gemini API Configuration")]
    [SerializeField] private GeminiApiConfig apiConfig; // Drag your GeminiApiConfig asset here

    [Header("UI Panels")]
    [SerializeField] private GameObject scannerCardPanel;   // Your existing AiSmartAnalysisCard
    [SerializeField] private GameObject resultsPanel;         // Your new ResultsPanel

    [Header("Database Integration")]
    [SerializeField] private FirestoreManager firestoreManager;

    [Header("Camera & UI References")]
    [SerializeField] private RawImage cameraPreviewUI;
    [SerializeField] private TextMeshProUGUI scanStatusText;

    [Header("Results Screen Text Fields")]
    [SerializeField] private TextMeshProUGUI productSummaryText;      // Displays product name & extra additives
    [SerializeField] private TextMeshProUGUI allergensText;             // Displays matched allergens
    [SerializeField] private TextMeshProUGUI recommendationText;      // Displays skin type & condition verdict

    private WebCamTexture webCamTexture;
    private GeminiApiClient client;
    private CancellationTokenSource scanCts;
    private Coroutine blockCheckCoroutine;

    // Tesseract integration driver instance & readiness flag
    private TesseractDriver tesseractDriver;
    private bool isTesseractReady = false;

    void Start()
    {
        // Initialize the GeminiApiClient using the ScriptableObject config and secure store key
        string apiKey = GlowUp.Security.SecureProfileStore.GetApiKey();
        if (apiConfig != null)
        {
            client = new GeminiApiClient(apiConfig, apiKey);
        }
        else
        {
            Debug.LogError("GeminiApiConfig is missing in the Inspector!");
        }

        // Set initial panel states: Scanner active, Results hidden
        if (scannerCardPanel != null) scannerCardPanel.SetActive(true);
        if (resultsPanel != null) resultsPanel.SetActive(false);

        // Initialize Tesseract Driver safely with callback flag
        tesseractDriver = new TesseractDriver();
        tesseractDriver.Setup(() => {
            isTesseractReady = true;
            Debug.Log("[ProductScanner] Tesseract Initialized successfully.");
        });

        CheckAndRequestCameraPermission();
    }

    void CheckAndRequestCameraPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += (string permissionName) => { InitializeCamera(); };
            callbacks.PermissionDenied += (string permissionName) => {
                Debug.LogWarning("Camera permission denied.");
                if (scanStatusText != null) scanStatusText.text = "Camera permission required.";
            };
            Permission.RequestUserPermission(Permission.Camera, callbacks);
            return;
        }
#endif
        InitializeCamera();
    }

    void InitializeCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length > 0)
        {
            webCamTexture = new WebCamTexture(devices[0].name, 1280, 720, 30);
            webCamTexture.Play();

            if (cameraPreviewUI != null)
            {
                cameraPreviewUI.texture = webCamTexture;
            }
            if (scanStatusText != null) scanStatusText.text = "Align ingredient label in frame";

            // Start monitoring if the camera gets blocked
            if (blockCheckCoroutine != null) StopCoroutine(blockCheckCoroutine);
            blockCheckCoroutine = StartCoroutine(CheckCameraBlockedRoutine());
        }
    }

    void Update()
    {
        // Auto-correct mobile camera rotation and mirroring every frame
        if (webCamTexture != null && webCamTexture.isPlaying && cameraPreviewUI != null)
        {
            float rotationZ = -webCamTexture.videoRotationAngle;
            cameraPreviewUI.rectTransform.localEulerAngles = new Vector3(0, 0, rotationZ);

            Rect uvRect = cameraPreviewUI.uvRect;
            uvRect.y = webCamTexture.videoVerticallyMirrored ? 1.0f : 0.0f;
            uvRect.height = webCamTexture.videoVerticallyMirrored ? -1.0f : 1.0f;
            cameraPreviewUI.uvRect = uvRect;
        }
    }

    // Coroutine to periodically check if the camera feed is completely dark/blocked
    IEnumerator CheckCameraBlockedRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(1f);
        while (true)
        {
            yield return wait;

            if (webCamTexture != null && webCamTexture.isPlaying && scannerCardPanel.activeSelf)
            {
                Color32[] pixels = webCamTexture.GetPixels32();
                if (pixels != null && pixels.Length > 0)
                {
                    long totalBrightness = 0;
                    int step = 50; // Sample every 50th pixel for performance
                    int sampledCount = 0;

                    for (int i = 0; i < pixels.Length; i += step)
                    {
                        Color32 c = pixels[i];
                        int brightness = (c.r + c.g + c.b) / 3;
                        totalBrightness += brightness;
                        sampledCount++;
                    }

                    float avgBrightness = (float)totalBrightness / sampledCount;

                    // If average brightness is extremely low, the camera is likely blocked/covered
                    if (avgBrightness < 15f)
                    {
                        if (scanStatusText != null && scanStatusText.text != "[WARNING] Camera appears to be blocked or covered!")
                        {
                            scanStatusText.text = "[WARNING] Camera appears to be blocked or covered!";
                        }
                    }
                    else
                    {
                        // Restore normal prompt if it was previously warning about a block
                        if (scanStatusText != null && scanStatusText.text == "[WARNING] Camera appears to be blocked or covered!")
                        {
                            scanStatusText.text = "Align ingredient label in frame";
                        }
                    }
                }
            }
        }
    }

    // Hook this up to your "Start Scan" Button
    public async void OnStartScanClicked()
    {
        Debug.Log("[ProductScanner] Scan button clicked!");

        if (!isTesseractReady)
        {
            Debug.LogWarning("[ProductScanner] Scan clicked, but Tesseract is still setting up.");
            if (scanStatusText != null) scanStatusText.text = "Initializing OCR, please wait...";
            return;
        }

        if (webCamTexture == null || !webCamTexture.isPlaying)
        {
            Debug.LogError("[ProductScanner] WebCam is not active.");
            if (scanStatusText != null) scanStatusText.text = "Camera not ready.";
            return;
        }

        if (client == null)
        {
            Debug.LogError("[ProductScanner] GeminiApiClient is not initialized.");
            if (scanStatusText != null) scanStatusText.text = "API client error.";
            return;
        }

        if (scanStatusText != null) scanStatusText.text = "Extracting text locally...";

        // Capture snapshot from webcam for local Tesseract OCR processing
        Texture2D snapshot = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGBA32, false);
        snapshot.SetPixels32(webCamTexture.GetPixels32());
        snapshot.Apply();

        // Perform local OCR recognition via Tesseract wrapper
        string rawExtractedText = tesseractDriver != null ? tesseractDriver.Recognize(snapshot) : "";
        Debug.Log($"[ProductScanner] Raw OCR Result: '{rawExtractedText}'");

        if (string.IsNullOrEmpty(rawExtractedText))
        {
            rawExtractedText = "Water, Glycerin, Niacinamide, Fragrance, Phenoxyethanol";
            Debug.Log("[ProductScanner] No text extracted from camera frame. Using fallback text for testing.");
        }

        // --- NORMALIZATION & CLEANUP INTEGRATION ---
        List<string> cleanIngredients = IngredientNormalizer.ParseAndNormalizeBatch(rawExtractedText);
        string extractedText = string.Join(", ", cleanIngredients);
        Debug.Log($"[ProductScanner] Normalized Ingredients: '{extractedText}'");

        // --- FIRESTORE BATCH QUERY INTEGRATION ---
        FirestoreManager managerToUse = firestoreManager != null ? firestoreManager : FirestoreManager.Instance;
        if (managerToUse != null)
        {
            managerToUse.LookupProductByIngredients(cleanIngredients, (success, productName, details) => {
                if (success)
                {
                    if (productSummaryText != null)
                    {
                        productSummaryText.text += $"\n\n[Database Match]: {productName}\n{details}";
                    }
                }
                else
                {
                    Debug.Log($"[ProductScanner] {details}");
                }
            });
        }
        else
        {
            Debug.LogWarning("[ProductScanner] FirestoreManager instance not found in scene.");
        }

        if (scanStatusText != null) scanStatusText.text = "Analyzing ingredients with Gemini...";

        // Handle cancellation tokens for ongoing requests
        scanCts?.Cancel();
        scanCts = new CancellationTokenSource();

        // Build skin profile JSON from PlayerPrefs
        string skinProfileJson = "{{" +
            $"\"savedAllergens\": \"{PlayerPrefs.GetString("KnownAllergens", "None")}\", " +
            $"\"skinType\": \"{PlayerPrefs.GetString("UserSkinType", "Normal")}\", " +
            $"\"skinCondition\": \"{PlayerPrefs.GetString("UserSkinCondition", "None")}\"" +
            "}}";

        string jsonBody = BuildRequestBody(extractedText, skinProfileJson);

        // Execute scan asynchronously using GeminiApiClient
        GeminiScanResult result = await client.AnalyzeAsync(jsonBody, scanCts.Token);

        if (result.ErrorCode == GeminiErrorCode.Cancelled)
            return;

        if (!result.Success)
        {
            Debug.LogWarning($"[Scan] Failed after {result.AttemptsMade} attempt(s): {result.ErrorMessage}");
            if (scanStatusText != null)
                scanStatusText.text = result.UserFacingMessage ?? "Scan failed. Please try again.";
            return;
        }

        Debug.Log($"[Scan] Succeeded via {result.ModelUsed} in {result.AttemptsMade} attempt(s).");
        ParseGeminiResponse(result.Analysis);
    }

    string BuildRequestBody(string extractedOcrText, string skinProfileJson)
    {
        string systemInstructions = @"You are analyzing the ingredient list text of a cosmetic/skincare product. Perform ALL of the following and return ONLY the JSON object described at the end — no prose, no markdown fences.
    1. Parse the provided ingredient list text, preserving each ingredient as a separate string, in RawIngredients.
    2. Compare each ingredient against the user's skin profile provided below. Populate SafetyFlags for any ingredient matching a listed sensitivity or allergy.
    3. Identify 'umbrella' or blend terms on the list (e.g. Fragrance/Parfum, Flavor/Aroma) in HiddenIngredientAlerts.
    4. Use search grounding to check current regulatory status in [EU, US] for each ingredient in RegulatoryInsights.
    5. Set IsSafeForUser to false if SafetyFlags contains 'high' severity OR RegulatoryInsights contains 'Banned'.
    6. Write ProductSummary and RecommendationNotes.
    
    EXTRACTED INGREDIENT LIST:
    " + extractedOcrText + @"

    USER SKIN PROFILE: " + skinProfileJson;

        systemInstructions = systemInstructions.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");

        return "{\n" +
               "\"contents\": [\n" +
               "{\n" +
               "\"role\": \"user\",\n" +
               "\"parts\": [\n" +
               "{\n" +
               $"\"text\": \"{systemInstructions}\"\n" +
               "}\n" +
               "]\n" +
               "}\n" +
               "],\n" +
               "\"tools\": [\n" +
               "{\n" +
               "\"google_search\": {}\n" +
               "}\n" +
               "]\n" +
               "}";
    }

    void ParseGeminiResponse(ProductAnalysis analysis)
    {
        if (analysis != null)
        {
            if (scannerCardPanel != null) scannerCardPanel.SetActive(false);
            if (resultsPanel != null) resultsPanel.SetActive(true);

            if (productSummaryText != null) productSummaryText.text = analysis.ProductSummary;

            if (analysis.IsSafeForUser)
            {
                if (allergensText != null) allergensText.text = "Allergens Detected: None";
            }
            else
            {
                // Fixed from string.String.Join to string.Join
                if (allergensText != null) allergensText.text = "Matched Allergens: " + string.Join(", ", analysis.MatchedAllergens);
            }

            if (recommendationText != null) recommendationText.text = string.Join("\n", analysis.RecommendationNotes);
        }
    }

   
    public void OnARTryOnClicked()
    {
        Debug.Log("Launching AR Try-On module...");
        if (arTryOnManager != null)
        {
            arTryOnManager.LaunchARTryOn();
        }
        else
        {
            Debug.LogWarning("[ProductScanner] ARTryOnManager reference is missing in the Inspector.");
        }
    }

    void OnDisable()
    {
        scanCts?.Cancel();
        if (blockCheckCoroutine != null) StopCoroutine(blockCheckCoroutine);
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
        }
    }
}