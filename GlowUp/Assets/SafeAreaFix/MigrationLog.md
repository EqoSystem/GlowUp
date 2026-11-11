# SafeAreaFix migration  - 2026-11-12 00:10

## Player settings (Android, Unity 2022.3.62f3)
- Render outside safe area: already ON (display cutout mode = short edges) - unchanged
- Start in fullscreen (status bar hidden): ON - unchanged (the app is drawn under the status-bar area either way; the top inset covers it)
- Target API level: AndroidApiLevelAuto (Automatic = the highest installed SDK, android-36 here). Android 15+ (API 35+) forces edge-to-edge for apps that target it: the app draws under the system bars, which this system handles. Unity 2022.3 itself does not opt out.
- Default orientation: Portrait - unchanged (SafeAreaRoot also follows landscape and every rotation)

Created Assets/SafeAreaFix/Resources/SafeAreaConfig.asset (top only, debug overlay on in development builds).
## Scenes
Backed up Assets/_Project/Scenes/AiSmartAnalysisResultScene.unity -> Assets/SafeAreaFix/Backup/AiSmartAnalysisResultScene.unity.20261112-001010.bak
### AiSmartAnalysisResultScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  AiSmartAnalysisResultCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  AiSmartAnalysisResultCard: Content (content container (inset card))
  AlternativesPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  RenamePanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  IngredientSheet: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  ScanAgainPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SafetyAlertPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  AiInsightPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved AiSmartAnalysisResultCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/AiSmartAnalysisScene.unity -> Assets/SafeAreaFix/Backup/AiSmartAnalysisScene.unity.20261112-001010.bak
### AiSmartAnalysisScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  AiSmartAnalysisCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  AiSmartAnalysisCard: Content (content container (inset card))
  StartOverPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  ShortlistPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  ManualEntryPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  BarcodeCardPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SnapshotPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  moved AiSmartAnalysisCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/ARTryOnScene.unity -> Assets/SafeAreaFix/Backup/ARTryOnScene.unity.20261112-001011.bak
### ARTryOnScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  ArTryOnCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  ArTryOnCard: Content (content container (inset card))
  ScreenLight: Content (content container (inset card))
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  ProductPickerPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved ArTryOnCard into SafeAreaRoot
  moved ScreenLight into SafeAreaRoot
Backed up Assets/_Project/Scenes/LoadingScene.unity -> Assets/SafeAreaFix/Backup/LoadingScene.unity.20261112-001012.bak
### LoadingScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  LoadingCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  LoadingCard: Content (content container (inset card))
  moved LoadingCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/LogInScene.unity -> Assets/SafeAreaFix/Backup/LogInScene.unity.20261112-001013.bak
### LogInScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  LogInCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  LogInCard: Content (content container (inset card))
  moved LogInCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/OnboardingStep1Scene.unity -> Assets/SafeAreaFix/Backup/OnboardingStep1Scene.unity.20261112-001014.bak
### OnboardingStep1Scene: changed
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  ConfirmEmailPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved OnboardingCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/OnboardingStep2Scene.unity -> Assets/SafeAreaFix/Backup/OnboardingStep2Scene.unity.20261112-001015.bak
### OnboardingStep2Scene: changed
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingStep2Card: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingStep2Card: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved OnboardingStep2Card into SafeAreaRoot
Backed up Assets/_Project/Scenes/OnboardingStep3Scene.unity -> Assets/SafeAreaFix/Backup/OnboardingStep3Scene.unity.20261112-001016.bak
### OnboardingStep3Scene: changed
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingStep3Card: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingStep3Card: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved OnboardingStep3Card into SafeAreaRoot
Backed up Assets/_Project/Scenes/OnboardingStep4Scene.unity -> Assets/SafeAreaFix/Backup/OnboardingStep4Scene.unity.20261112-001016.bak
### OnboardingStep4Scene: changed
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved OnboardingCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/OnboardingStep5Scene.unity -> Assets/SafeAreaFix/Backup/OnboardingStep5Scene.unity.20261112-001017.bak
### OnboardingStep5Scene: changed
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved OnboardingCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/RecentScanActivitiesScene.unity -> Assets/SafeAreaFix/Backup/RecentScanActivitiesScene.unity.20261112-001018.bak
### RecentScanActivitiesScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  LogInCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  LogInCard: Content (content container (inset card))
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  RenamePanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  AiInsightPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved LogInCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/SettingsScene.unity -> Assets/SafeAreaFix/Backup/SettingsScene.unity.20261112-001019.bak
### SettingsScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  SettingsCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  SettingsCard: Content (content container (inset card))
  ConfirmPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  DeleteAccountPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  LegalPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SupportPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved SettingsCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/StartupScene.unity -> Assets/SafeAreaFix/Backup/StartupScene.unity.20261112-001020.bak
### StartupScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  StartupCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  StartupCard: Content (content container (inset card))
  moved StartupCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/TermsAndConditionScene.unity -> Assets/SafeAreaFix/Backup/TermsAndConditionScene.unity.20261112-001021.bak
### TermsAndConditionScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  PTCCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  PTCCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved PTCCard into SafeAreaRoot
Backed up Assets/_Project/Scenes/UserDashboardScene.unity -> Assets/SafeAreaFix/Backup/UserDashboardScene.unity.20261112-001021.bak
### UserDashboardScene: changed
- canvas Canvas (ScreenSpaceOverlay):
  UserDashboardPanel: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  UserDashboardPanel: Content (content container (inset card))
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  AccountPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SignOutConfirmPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  AiInsightPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  moved UserDashboardPanel into SafeAreaRoot

## Prefabs
- AllergenChipPrefab.prefab: no Canvas (a piece placed inside a screen): nothing to change
- ScanItemCardTemplate.prefab: no Canvas (a piece placed inside a screen): nothing to change
