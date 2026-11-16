# SafeAreaFix migration (DRY RUN: nothing changed) - 2026-11-12 00:09

## Player settings (Android, Unity 2022.3.62f3)
- Render outside safe area: already ON (display cutout mode = short edges) - unchanged
- Start in fullscreen (status bar hidden): ON - unchanged (the app is drawn under the status-bar area either way; the top inset covers it)
- Target API level: AndroidApiLevelAuto (Automatic = the highest installed SDK, android-36 here). Android 15+ (API 35+) forces edge-to-edge for apps that target it: the app draws under the system bars, which this system handles. Unity 2022.3 itself does not opt out.
- Default orientation: Portrait - unchanged (SafeAreaRoot also follows landscape and every rotation)

## Scenes
### AiSmartAnalysisResultScene: would change
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
  would move AiSmartAnalysisResultCard into SafeAreaRoot
### AiSmartAnalysisScene: would change
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
  would move AiSmartAnalysisCard into SafeAreaRoot
### ARTryOnScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  ArTryOnCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  ArTryOnCard: Content (content container (inset card))
  ScreenLight: Content (content container (inset card))
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  ProductPickerPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move ArTryOnCard into SafeAreaRoot
  would move ScreenLight into SafeAreaRoot
### LoadingScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  LoadingCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  LoadingCard: Content (content container (inset card))
  would move LoadingCard into SafeAreaRoot
### LogInScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  LogInCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  LogInCard: Content (content container (inset card))
  would move LogInCard into SafeAreaRoot
### OnboardingStep1Scene: would change
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  ConfirmEmailPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move OnboardingCard into SafeAreaRoot
### OnboardingStep2Scene: would change
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingStep2Card: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingStep2Card: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move OnboardingStep2Card into SafeAreaRoot
### OnboardingStep3Scene: would change
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingStep3Card: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingStep3Card: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move OnboardingStep3Card into SafeAreaRoot
### OnboardingStep4Scene: would change
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move OnboardingCard into SafeAreaRoot
### OnboardingStep5Scene: would change
- canvas Canvas (ScreenSpaceOverlay):
  OnboardingCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  OnboardingCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move OnboardingCard into SafeAreaRoot
### RecentScanActivitiesScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  LogInCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  LogInCard: Content (content container (inset card))
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  RenamePanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  AiInsightPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move LogInCard into SafeAreaRoot
### SettingsScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  SettingsCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  SettingsCard: Content (content container (inset card))
  ConfirmPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  DeleteAccountPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  LegalPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SupportPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move SettingsCard into SafeAreaRoot
### StartupScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  StartupCard: top margin 40 = bottom margin 40 (design margin, kept)
  PaperBackground: Background (full-stretch picture that touches every edge)
  StartupCard: Content (content container (inset card))
  would move StartupCard into SafeAreaRoot
### TermsAndConditionScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  PTCCard: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  PTCCard: Content (content container (inset card))
  LeaveSignUpPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move PTCCard into SafeAreaRoot
### UserDashboardScene: would change
- canvas Canvas (ScreenSpaceOverlay):
  UserDashboardPanel: top margin 40 = bottom margin 40 (design margin, kept)
  DarkBackground: Background (full-stretch picture that touches every edge)
  UserDashboardPanel: Content (content container (inset card))
  SwipeNavigation: KeepOnCanvas (draws nothing (logic holder))
  AccountPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  SignOutConfirmPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  AiInsightPanel: KeepOnCanvas (pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred)
  would move UserDashboardPanel into SafeAreaRoot

## Prefabs
- AllergenChipPrefab.prefab: no Canvas (a piece placed inside a screen): nothing to change
- ScanItemCardTemplate.prefab: no Canvas (a piece placed inside a screen): nothing to change
