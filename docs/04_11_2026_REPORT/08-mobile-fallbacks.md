# 08. Mobile app fallback paths

Only fallbacks that can be traced to app code are listed. A UI message or component does not prove every Android device will behave identically; camera, network and AR behavior depends on platform and permissions.

| Trigger | Implemented behavior | Main source |
|---|---|---|
| OCR returns no useful product text / text is ambiguous | The scan controller can offer a candidate shortlist, a name/type choice, or the manual-entry path rather than silently selecting a guess. | `GlowUp/Assets/_Project/Scripts/Scan/SmartScanFlowController.cs`, `ScanShortlistView.cs` |
| User taps “Type the product in instead” | Manual product entry path is exposed by the Smart Scan controller; exact required fields and final outcomes are **PARTIALLY TRACED** here. | `SmartScanFlowController.cs`, `TypedEntry.cs` |
| Camera frame is blurry, dark, glare-affected or moving | Frame quality gate discards poor frames and scan guidance asks the user to adjust the camera. | `SmartScanFlowController.cs`, `FrameQuality.cs`, `ScanGuidance.cs`, `ScanGuideOverlay.cs` |
| Product is absent from catalogue | Cached catalogue is checked first; online lookup can be attempted. If configured and eligible, the flow offers a photo snapshot read through Vercel; user can also choose manual entry. | `SmartScanFlowController.cs`, `ProductRepository.cs`, `ScanSnapshotController.cs`, `api/scan-snapshot.js` |
| Snapshot quota, network, or service fails | API has explicit error responses, limits and a one-time no-search fallback for a search-grounding 429. The mobile UI's exact mapping for every endpoint error is **PARTIALLY TRACED**. | `GlowUp-Admin_AI_Testing/api/scan-snapshot.js`, `ScanSnapshotController.cs` |
| Gemini scan endpoint times out, is rate-limited, unavailable or model is missing | Client retries transient errors with backoff; model-not-found advances to another configured model; terminal authorization/bad-request errors stop. It returns a user-facing failure result. | `GlowUp/Assets/_Project/Scripts/Scanning/Geminiapiclient.cs`, `Geminiapiconfig.cs` |
| Firebase initialization unavailable | Firebase bootstrap returns false and logs a warning; mobile repositories can use prior local data. Full behavior without any first-run cache is **PARTIALLY TRACED**. | `GlowUp/Assets/_Project/Scripts/Data/FirebaseBootstrap.cs`, `OfflineCache.cs` |
| No network / a cloud request gets no answer | Catalog repositories use cached lists; a failed network check pauses repeated list downloads briefly. Connectivity changes retrigger queued sync. | `GlowUp/Assets/_Project/Scripts/Net/ConnectivityMonitor.cs`, `Data/BrandRepository.cs`, `Data/ProductRepository.cs`, `Data/IngredientRepository.cs` |
| Firestore write while offline or not yet signed in | Write stays in persistent queue and retries when connectivity returns. Permanent failures can be dropped after configured attempts with a warning. | `GlowUp/Assets/_Project/Scripts/Data/SyncQueue.cs` |
| No product matched after a scan | User can retry catalogue lookup or start over; whether another scan matches depends on the loaded catalogue and online connectivity. | `SmartScanFlowController.cs` |
| Camera permission denied / camera startup issue in AR | `CameraWaitCard` provides state/action UI; controller listens for camera status. Exact Android permission prompt/retry sequence across all devices is **PARTIALLY TRACED**. | `GlowUp/Assets/_Project/Scripts/AR/CameraWaitCard.cs`, `CameraFeedController.cs`, `Common/CameraPermission.cs`, `ArTryOnFlowController.cs` |
| AR face tracking unavailable or lost | The AR controller can select live tracking or a guided preview mode. Brow try-on is explicitly unavailable in guided mode; the UI limits that area. | `GlowUp/Assets/_Project/Scripts/AR/ArTryOnFlowController.cs`, `ArFaceTrackingController.cs`, `FallbackMakeupOverlay.cs` |
| Product has no saved shades or no saved makeup | Product shade controls are hidden where appropriate; picker offers search and scan route, and displays a no-saved-makeup message. | `GlowUp/Assets/_Project/Scripts/AR/ArTryOnFlowController.cs`, `Data/ProductRepository.cs` |

## Paths not established in the inspected sources

- A dedicated OCR-permission denial recovery path: **NOT FOUND IN CODE** as a separate path. Camera permission behavior is described above.
- A guaranteed offline first-run onboarding or catalogue download: **NOT FOUND IN CODE**.
- A cached Firestore index or local queue behavior that makes every operation available offline: **NOT FOUND IN CODE**. Offline support is limited to explicit caches and queued writes.
