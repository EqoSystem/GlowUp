# SafeAreaFix: audit report (12 November 2026, before any change)

**Project:** Unity 2022.3.62f3, Android, URP 14. **UI system: uGUI** (Canvas + RectTransform in all 15 scenes; no UIDocument / PanelSettings anywhere), so the plan stays uGUI.

**Player settings found:** Start in fullscreen ON (status bar hidden), Render outside safe area ON (Unity draws under the camera hole; display cutout mode = short edges), Target API Automatic (highest installed SDK: android-36, so Android 15+ edge-to-edge applies), Min API 23, portrait.

## What causes the inconsistency

Every screen has the SAME structure: `Canvas` > full-stretch background picture (`DarkBackground` / `PaperBackground`) > the screen's card, inset 40 canvas units on every side > pop-ups (OverlayPanel).
Because the app draws under the status bar and the camera hole, a 40-unit margin (about 13 dp) is less than a punch hole (about 25 to 35 dp): the card's top edge and header sit **under the hole** on most screens (category b).
On the three screens with the bottom bar (Dashboard, Recent Scans, AR Try-On) an earlier fix (`SafeAreaFitter`, 11 November) pushed only those cards down by the safe area, which shows the background **as a strip above the card** (category a). One shared system replaces both.

| Screen | Category | Why |
|---|---|---|
| UserDashboardScene, RecentScanActivitiesScene, ARTryOnScene | (a) gap above header, plus (d) for AR (camera preview inside the card) | card pushed down only here by SafeAreaFitter at run time |
| LogInScene, OnboardingStep1-5, TermsAndConditionScene, SettingsScene, AiSmartAnalysisScene, AiSmartAnalysisResultScene, LoadingScene, StartupScene | (b) content collides with the cutout | card 40 units from the top, app drawn under the hole |
| AiSmartAnalysisScene, ARTryOnScene | (d) special: camera picture (RawImage) inside the card | the picture shrinks with the card; it is not a full-bleed preview |
| every scene's pop-ups (OverlayPanel), ToastLayer, UiServices > TransitionCanvas | (d) special: pop-ups / toast / loading sheet | dim layer full-bleed; box centred; the toast already places itself from Screen.safeArea |
| Prefabs AllergenChipPrefab, ScanItemCardTemplate | (c) not screens (no Canvas) | placed inside screens |

Hardcoded top offsets: none that compensate for the cutout. The 40-unit card margin is equal at top and bottom (design margin) and is kept.

## Canvases

| File | Root canvas | Render mode | Canvas Scaler | Full-stretch backgrounds | Safe-area handling in the file | Full-stretch content containers (inset from top, canvas units) | Children with their own Canvas (overlays) |
|---|---|---|---|---|---|---|---|
| ARTryOnScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | PaperBackground | none in the file | ArTryOnCard (40 from top), ScreenLight (-0 from top), SwipeNavigation (-0 from top) | - |
| AiSmartAnalysisResultScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | AiSmartAnalysisResultCard (40 from top) | - |
| AiSmartAnalysisScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | AiSmartAnalysisCard (40 from top), SwipeNavigation (-0 from top) | - |
| LoadingScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | PaperBackground | none in the file | LoadingCard (40 from top) | - |
| LogInScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | LogInCard (40 from top) | - |
| OnboardingStep1Scene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | OnboardingCard (40 from top) | - |
| OnboardingStep2Scene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | OnboardingStep2Card (40 from top) | - |
| OnboardingStep3Scene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | OnboardingStep3Card (40 from top) | - |
| OnboardingStep4Scene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | OnboardingCard (40 from top) | - |
| OnboardingStep5Scene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | OnboardingCard (40 from top) | - |
| RecentScanActivitiesScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | LogInCard (40 from top), SwipeNavigation (-0 from top) | - |
| SettingsScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | PaperBackground | none in the file | SettingsCard (40 from top) | - |
| StartupScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | PaperBackground | none in the file | StartupCard (40 from top) | - |
| TermsAndConditionScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | PTCCard (40 from top) | - |
| UserDashboardScene.unity | Canvas | Screen Space Overlay | Scale With Screen Size 1080x1920, match 0 | DarkBackground | none in the file | UserDashboardPanel (40 from top), SwipeNavigation (-0 from top) | - |

### ARTryOnScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| PaperBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| ArTryOnCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, PillCorners, ThemedGraphic, CanvasGroup |
| ScreenLight | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, ScreenLightFrame, GeneratedByBuilder |
| SwipeNavigation | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | SwipeSceneNavigator, GeneratedByBuilder |
| ProductPickerPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |

### AiSmartAnalysisResultScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| AiSmartAnalysisResultCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | VerticalLayoutGroup, Image, PillCorners, ThemedGraphic |
| AlternativesPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| RenamePanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| IngredientSheet | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder, IngredientSheet |
| ScanAgainPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| SafetyAlertPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| AiInsightPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder, AiInsightPanel |

### AiSmartAnalysisScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| AiSmartAnalysisCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | VerticalLayoutGroup, Image, PillCorners, ThemedGraphic, CanvasGroup |
| StartOverPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| ShortlistPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder, ScanShortlistView |
| ManualEntryPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| BarcodeCardPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder, ScanBarcodeCardView |
| SnapshotPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| SwipeNavigation | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | SwipeSceneNavigator, GeneratedByBuilder |

### LoadingScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| PaperBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| LoadingCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, PillCorners, ThemedGraphic |

### LogInScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| LogInCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | VerticalLayoutGroup, Image, ThemedGraphic, PillCorners |

### OnboardingStep1Scene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| OnboardingCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, MB, ThemedGraphic, PillCorners |
| LeaveSignUpPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |
| ConfirmEmailPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |

### OnboardingStep2Scene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| OnboardingStep2Card | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, MB, ThemedGraphic, PillCorners |
| LeaveSignUpPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |

### OnboardingStep3Scene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| OnboardingStep3Card | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, MB, ThemedGraphic, PillCorners |
| LeaveSignUpPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |

### OnboardingStep4Scene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| OnboardingCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, MB, ThemedGraphic, PillCorners |
| LeaveSignUpPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |

### OnboardingStep5Scene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| OnboardingCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, MB, ThemedGraphic, PillCorners |
| LeaveSignUpPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |

### RecentScanActivitiesScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| LogInCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | VerticalLayoutGroup, Image, PillCorners, ThemedGraphic, CanvasGroup |
| SwipeNavigation | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | SwipeSceneNavigator, GeneratedByBuilder |
| RenamePanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| AiInsightPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder, AiInsightPanel |

### SettingsScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| PaperBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| SettingsCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, PillCorners, ThemedGraphic |
| ConfirmPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| DeleteAccountPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| LegalPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| SupportPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |

### StartupScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| PaperBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| StartupCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | Image, PillCorners, ThemedGraphic |

### TermsAndConditionScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| PTCCard | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | VerticalLayoutGroup, Image, ThemedGraphic, PillCorners |
| LeaveSignUpPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel |

### UserDashboardScene.unity > Canvas

| Child | Active | Anchor min / max | Anchored pos | Size delta | Components |
|---|---|---|---|---|---|
| DarkBackground | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | Image, ThemedGraphic |
| UserDashboardPanel | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (-80.0, -80.0) | VerticalLayoutGroup, Image, PillCorners, ThemedGraphic, CanvasGroup |
| SwipeNavigation | yes | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | SwipeSceneNavigator, GeneratedByBuilder |
| AccountPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| SignOutConfirmPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder |
| AiInsightPanel | no | (0.0, 0.0) / (1.0, 1.0) | (0.0, 0.0) | (0.0, 0.0) | CanvasGroup, OverlayPanel, GeneratedByBuilder, AiInsightPanel |


## Layout groups, scroll views and raw images whose size changes

With the top inset applied (example: a 2400-px-tall phone with a 110 px hole area = 110 canvas units on this 1080-wide layout), everything stretched with the card loses that height; the rest keeps its size.

### ARTryOnScene.unity: 19 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| ControlsScrollView | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Swatches | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Swatches | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| FinishTabs | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AreaTabs | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| BeforeDisplay | RawImage | height shrinks by the top inset (stretched with the card) |
| CameraDisplay | RawImage | height shrinks by the top inset (stretched with the card) |

### AiSmartAnalysisResultScene.unity: 47 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| AiSmartAnalysisResultCard | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ResultScrollView | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| DermatologistCard | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ShadeCard | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SwatchRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| WhyCard | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| IngredientsCard | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SourcesBlock | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SourcesBody | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SourceLinks | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ChipGrid | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| CompatibilityCard | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AgeIndicator | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ConcernsIndicator | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AllergensIndicator | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SkinTypeIndicator | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ProductCard | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| FdaBadge | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| FdaDetails | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ConfirmRow | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Buttons | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| BadgeRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| PendingApprovalBadge | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| VerdictRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ProductHead | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HeaderContainer | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### AiSmartAnalysisScene.unity: 17 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| AiSmartAnalysisCard | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| ControlBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| RightControls | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| LeftControls | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| FoundChips | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ChipRowBottom | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ChipRowTop | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| CameraDisplay | RawImage | height shrinks by the top inset (stretched with the card) |
| ResultsPanelCard | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| HeaderContainer | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HeaderContainer | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| CameraFeedDisplay | RawImage | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### LoadingScene.unity: 1 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### LogInScene.unity: 2 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| LogInCard | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| SavedAccountsPanel | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### OnboardingStep1Scene.unity: 15 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| DaysGrid | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| WeekdayHeader | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| FormScrollView | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| HelpBubble | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ProgressBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### OnboardingStep2Scene.unity: 8 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| ConditionsPanel_Scroll | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SkinTypeTiles | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| InfoBadge | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| InfoBadge | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| InfoBadge | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| InfoBadge | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ProgressBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### OnboardingStep3Scene.unity: 2 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| SkinToneTiles | GridLayoutGroup | height shrinks by the top inset (stretched with the card) |
| ProgressBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### OnboardingStep4Scene.unity: 3 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| AllergenContainerCard_Scroll | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ProgressBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### OnboardingStep5Scene.unity: 20 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| DaysGrid | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| WeekdayHeader | GridLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AllergenChipsContainer | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| SummaryScrollView | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_Allergens | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_Conditions | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_SkinTone | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_SkinType | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_Email | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_Age | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_Birthday | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_LastName | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_MiddleName | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SummaryRow_FirstName | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ProgressBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### RecentScanActivitiesScene.unity: 15 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| LogInCard | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ScanListSkeleton | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| ScanHistoryList | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Buttons | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ScanHistoryScrollView | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### SettingsScene.unity: 132 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| SettingsScrollView | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AccountActionsSection | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Panel | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AccountActionsBlock | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Header | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AboutSection | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Panel | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SupportRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| LegalRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Header | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| NotificationsSection | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Panel | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| RuleUpdatesRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ReminderTimeRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Template | ScrollRect | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| DailyReminderRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Line | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SafetyAlertsSection | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Panel | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| TriggerGroup | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_contact | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_melasma | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_seborrheic | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_rosacea | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_psoriasis | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_eczema | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_acne | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_harmful | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Trigger_allergens | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| VibrateRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| InstantAlertsRow | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Line | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Texts | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SkinProfileSection | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Panel | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| SkinProfileBlock | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Line | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| ... 72 more | | same rule |

### StartupScene.unity: 0 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|

### TermsAndConditionScene.unity: 4 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| PTCCard | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| ProgressBar | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| PrivacyPolicy_Scroll View | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

### UserDashboardScene.unity: 9 layout groups / scroll views / raw images

| Object | Kind | With the top inset applied |
|---|---|---|
| UserDashboardPanel | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| RecommendationsSkeleton | VerticalLayoutGroup | height shrinks by the top inset (stretched with the card) |
| RecommendationsScrollView | ScrollRect | height shrinks by the top inset (stretched with the card) |
| Content | VerticalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Buttons | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| Content | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |
| AllergenPillContainer | HorizontalLayoutGroup | keeps its size (a top-anchored one moves down by the inset, a bottom-anchored one stays) |

