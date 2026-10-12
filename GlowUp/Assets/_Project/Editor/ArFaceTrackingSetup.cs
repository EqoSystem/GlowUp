using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Set Up AR Face Tracking (ARCore). It also runs by itself each time the AR Try-On
    /// scene is built. It makes the project ready for live face tracking on Android and can be run any
    /// number of times; it only changes what is not right yet and says what it changed:
    ///
    ///  1. Graphics: Android uses OpenGL ES 3 (ARCore cannot start with Vulkan first in the list), and the
    ///     app is built for 32-bit AND 64-bit phones (Google's AR service closes a 32-bit-only app on a
    ///     64-bit phone the moment face tracking starts).
    ///  2. XR Plug-in Management: the "Google ARCore" plug-in is switched on for Android.
    ///  3. ARCore is set to OPTIONAL, so the app still installs and runs on phones without AR
    ///     (those phones get the guided preview).
    ///  4. Each URP renderer gets the "AR Background Renderer Feature" (it draws the camera picture).
    ///  5. The four face masks are imported the right way and the four layer materials are made.
    ///
    /// The same things can be done by hand in Project Settings (Player, XR Plug-in Management) and on
    /// the renderer assets in Assets/Settings.
    /// </summary>
    public static class ArFaceTrackingSetup
    {
        const string MaskFolder = "Assets/_Project/Textures/FaceMakeup/";
        const string MaterialFolder = "Assets/_Project/Materials/";
        const string ShaderFolder = "Assets/_Project/Shaders/";
        const string ArCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
        const string ArCoreSettingsType = "UnityEditor.XR.ARCore.ARCoreSettings";
        const string ArCoreSettingsPath = "Assets/XR/Settings/ARCoreSettings.asset";

        /// <summary>The four layer materials, in drawing order: face, cheeks, eyes, lips.</summary>
        public class Layers { public Material face, cheeks, eyes, lips, shine; public Texture2D[] shineMasks = new Texture2D[0]; }

        [MenuItem("GlowUp/Set Up AR Face Tracking (ARCore)")]
        static void RunFromMenu()
        {
            var notes = new List<string>();
            bool ready = Apply(notes);
            EditorUtility.DisplayDialog("GlowUp: AR face tracking",
                (ready ? "The project is ready for live face tracking.\n\n" : "Not everything could be set up yet.\n\n") + string.Join("\n", notes), "OK");
        }

        /// <summary>Does every step. Returns true when the project is ready; `notes` lists what was done or is missing.</summary>
        public static bool Apply(List<string> notes)
        {
            notes = notes ?? new List<string>();
            bool ready = true;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { notes.Add("Stop Play mode first."); return false; }

            Step(notes, "Graphics", SetGraphics);
            Step(notes, "Processors", SetArchitectures);
            bool packageInstalled = FindType(ArCoreLoaderType) != null;
            if (!packageInstalled)
            {
                ready = false;
                notes.Add("- The Google ARCore XR Plug-in is not installed yet. It is listed in Packages/manifest.json, so Unity installs it by itself; " +
                          "wait for it to finish (Window > Package Manager shows it), then run GlowUp > Set Up AR Face Tracking (ARCore) again.");
            }
            else
            {
                ready &= Step(notes, "ARCore plug-in", EnableLoader);
                ready &= Step(notes, "ARCore settings", MakeArCoreOptional);
            }
            ready &= Step(notes, "Camera picture", AddBackgroundFeatures);
            ready &= Step(notes, "Face layers", n => { EnsureLayers(n); return true; });

            AssetDatabase.SaveAssets();
            Debug.Log("[GlowUp] AR face tracking set-up:\n" + string.Join("\n", notes));
            return ready;
        }

        static bool Step(List<string> notes, string name, Func<List<string>, bool> step)
        {
            try { return step(notes); }
            catch (Exception e)
            {
                notes.Add($"- {name}: could not be set automatically ({e.Message}).");
                Debug.LogWarning($"[GlowUp] AR set-up, {name}: {e}");
                return false;
            }
        }

        // ---------------------------------------------------------------------------------------
        //  1. Graphics API and Android version
        // ---------------------------------------------------------------------------------------
        static bool SetGraphics(List<string> notes)
        {
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            bool automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
            if (automatic || apis.Length != 1 || apis[0] != GraphicsDeviceType.OpenGLES3)
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
                notes.Add("- Graphics: Android now uses OpenGL ES 3 only (it was " + (automatic ? "automatic" : string.Join(", ", apis)) + "). ARCore needs this.");
            }
            else notes.Add("- Graphics: already OpenGL ES 3.");

            if ((int)PlayerSettings.Android.minSdkVersion < 23)
            {
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)23;
                notes.Add("- Minimum API Level raised to 23 (Android 6.0).");
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------
        //  1b. 32-bit AND 64-bit, and the Gradle template that goes with it
        // ---------------------------------------------------------------------------------------
        const string GradleTemplate = "Assets/Plugins/Android/mainTemplate.gradle";
        const string TextReaderLibrary = "com.google.mlkit:text-recognition";
        const string TextReaderLine = "    implementation 'com.google.mlkit:text-recognition:16.0.1' // GlowUp: the phone's own text reader for the scan screen " +
                                      "(GlowUpTextReader.java). Kept outside the Android Resolver block on purpose.";

        const string BarcodeLibrary = "com.google.mlkit:barcode-scanning";
        const string BarcodeLine = "    implementation 'com.google.mlkit:barcode-scanning:17.2.0' // GlowUp: the phone's own barcode reader for the scan screen " +
                                   "(GlowUpBarcodeReader.java). Kept outside the Android Resolver block on purpose.";

        // The hand finder of the finger signs. This exact version is chosen on purpose: later versions (0.10.13 and up) bring a
        // newer "protobuf" library than Firebase's database works with. Do not raise it without checking that.
        const string HandLibrary = "com.google.mediapipe:tasks-vision";
        const string HandLine = "    implementation('com.google.mediapipe:tasks-vision:0.10.11') { exclude group: 'com.google.auto.value', module: 'auto-value' } " +
                                "// GlowUp: the phone's own hand finder for finger signs (GlowUpHandReader.java). Kept outside the Android Resolver block on purpose.";

        /// <summary>Builds for ARMv7 + ARM64 and keeps the Gradle template right. Adds a note only for what it changed.</summary>
        public static bool SetArchitectures(List<string> notes)
        {
            notes = notes ?? new List<string>();
            const AndroidArchitecture wanted = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                notes.Add("- Scripting backend: IL2CPP (64-bit builds need it).");
            }
            if (PlayerSettings.Android.targetArchitectures != wanted)
            {
                PlayerSettings.Android.targetArchitectures = wanted;
                notes.Add("- Processors: the app is now built for 32-bit AND 64-bit phones (ARMv7 + ARM64). New phones no longer say it is not compatible, " +
                          "and live face tracking can run on them. The build takes longer and the file is larger.");
            }
            FixGradleTemplate(notes);
            return true;
        }

        /// <summary>
        /// The Gradle template must list the scan screen's text reader, and must not throw the 64-bit
        /// libraries away (a line written while the project was 32-bit only did that).
        /// </summary>
        public static bool FixGradleTemplate(List<string> notes)
        {
            if (!File.Exists(GradleTemplate))
            {
                notes?.Add("- Gradle: " + GradleTemplate + " is missing. Tick Custom Main Gradle Template under Player > Publishing Settings, then add this line to its dependencies: " + TextReaderLine.Trim());
                return false;
            }
            string original = File.ReadAllText(GradleTemplate), text = original;
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            bool addedLibrary = false, keptWideLibraries = false;
            if (!text.Contains(TextReaderLibrary))
            {
                int at = text.IndexOf("**DEPS**", StringComparison.Ordinal);
                if (at >= 0) { text = text.Insert(at, TextReaderLine + newline); addedLibrary = true; }
            }
            bool addedBarcode = false;
            if (!text.Contains(BarcodeLibrary))
            {
                int at = text.IndexOf("**DEPS**", StringComparison.Ordinal);
                if (at >= 0) { text = text.Insert(at, BarcodeLine + newline); addedBarcode = true; }
            }
            bool addedHand = false;
            if (!text.Contains(HandLibrary))
            {
                int at = text.IndexOf("**DEPS**", StringComparison.Ordinal);
                if (at >= 0) { text = text.Insert(at, HandLine + newline); addedHand = true; }
            }
            var lines = new List<string>(text.Split('\n'));
            if (lines.RemoveAll(line => line.Contains("/lib/arm64-v8a/") && line.Contains("exclude")) > 0) { text = string.Join("\n", lines); keptWideLibraries = true; }
            if (text == original) return true;

            File.WriteAllText(GradleTemplate, text);
            AssetDatabase.ImportAsset(GradleTemplate);
            if (addedLibrary) notes?.Add("- Gradle template: the scan screen's text reader (Google ML Kit) was added to the libraries.");
            if (addedBarcode) notes?.Add("- Gradle template: the scan screen's barcode reader (Google ML Kit) was added to the libraries.");
            if (addedHand) notes?.Add("- Gradle template: the hand finder for finger signs (Google MediaPipe) was added to the libraries.");
            if (keptWideLibraries) notes?.Add("- Gradle template: the line that removed the 64-bit libraries was taken out.");
            return true;
        }

        // ---------------------------------------------------------------------------------------
        //  2. XR Plug-in Management: Google ARCore for Android
        // ---------------------------------------------------------------------------------------
        static bool EnableLoader(List<string> notes)
        {
            XRGeneralSettingsPerBuildTarget perTarget = null;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget);
            if (perTarget == null)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget"))
                {
                    perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid));
                    if (perTarget != null) break;
                }
                if (perTarget == null)
                {
                    // no settings asset at all: let XR Management make its own (the same thing its settings page does)
                    MethodInfo create = typeof(XRGeneralSettingsPerBuildTarget).GetMethod("GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    perTarget = create != null ? create.Invoke(null, null) as XRGeneralSettingsPerBuildTarget : null;
                }
                if (perTarget != null) EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (perTarget == null)
            {
                notes.Add("- ARCore plug-in: open Edit > Project Settings > XR Plug-in Management, choose the Android tab and tick Google ARCore.");
                return false;
            }

            const BuildTargetGroup android = BuildTargetGroup.Android;
            if (!perTarget.HasSettingsForBuildTarget(android)) perTarget.CreateDefaultSettingsForBuildTarget(android);
            if (!perTarget.HasManagerSettingsForBuildTarget(android)) perTarget.CreateDefaultManagerSettingsForBuildTarget(android);
            XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(android);
            if (manager == null)
            {
                notes.Add("- ARCore plug-in: open Edit > Project Settings > XR Plug-in Management, choose the Android tab and tick Google ARCore.");
                return false;
            }

            if (XRPackageMetadataStore.IsLoaderAssigned(ArCoreLoaderType, android)) { notes.Add("- ARCore plug-in: already switched on for Android."); return true; }
            bool assigned = XRPackageMetadataStore.AssignLoader(manager, ArCoreLoaderType, android);
            EditorUtility.SetDirty(perTarget);
            notes.Add(assigned ? "- ARCore plug-in: switched on for Android (XR Plug-in Management)."
                               : "- ARCore plug-in: could not be switched on automatically. Tick Google ARCore under Project Settings > XR Plug-in Management > Android.");
            return assigned;
        }

        // ---------------------------------------------------------------------------------------
        //  3. ARCore is optional: the app also runs on phones without AR
        // ---------------------------------------------------------------------------------------
        static bool MakeArCoreOptional(List<string> notes)
        {
            Type type = FindType(ArCoreSettingsType);
            PropertyInfo current = type != null ? type.GetProperty("currentSettings", BindingFlags.Static | BindingFlags.Public) : null;
            if (current == null)
            {
                notes.Add("- ARCore settings: set Requirement to Optional under Project Settings > XR Plug-in Management > ARCore.");
                return false;
            }

            var settings = current.GetValue(null) as ScriptableObject;
            if (settings == null)
            {
                settings = AssetDatabase.LoadAssetAtPath(ArCoreSettingsPath, type) as ScriptableObject;
                if (settings == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ArCoreSettingsPath));
                    settings = ScriptableObject.CreateInstance(type);
                    AssetDatabase.CreateAsset(settings, ArCoreSettingsPath);
                }
                current.SetValue(null, settings);
            }

            var serialized = new SerializedObject(settings);
            bool changed = false;
            foreach (string field in new[] { "m_Requirement", "m_Depth" })
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null) continue;
                int optional = Array.IndexOf(property.enumNames, "Optional");
                if (optional >= 0 && property.enumValueIndex != optional) { property.enumValueIndex = optional; changed = true; }
            }
            if (changed) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
            notes.Add(changed ? "- ARCore settings: Requirement set to Optional, so the app also installs on phones without AR."
                              : "- ARCore settings: already Optional.");
            return true;
        }

        // ---------------------------------------------------------------------------------------
        //  4. URP: the feature that draws the AR camera picture
        // ---------------------------------------------------------------------------------------
        static bool AddBackgroundFeatures(List<string> notes)
        {
            var pipelines = new List<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) pipelines.Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                RenderPipelineAsset asset = QualitySettings.GetRenderPipelineAssetAt(i);
                if (asset != null && !pipelines.Contains(asset)) pipelines.Add(asset);
            }

            var renderers = new List<ScriptableRendererData>();
            foreach (RenderPipelineAsset pipeline in pipelines)
            {
                if (!(pipeline is UniversalRenderPipelineAsset)) continue;
                SerializedProperty list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                if (list == null) continue;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                    if (data != null && !renderers.Contains(data)) renderers.Add(data);
                }
            }
            if (renderers.Count == 0)
            {
                notes.Add("- Camera picture: no URP renderer was found. Add \"AR Background Renderer Feature\" to your URP renderer asset by hand.");
                return false;
            }

            int added = 0;
            foreach (ScriptableRendererData data in renderers)
            {
                bool has = false;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures) if (feature is ARBackgroundRendererFeature) { has = true; break; }
                if (has) continue;

                var background = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                background.name = "AR Background Renderer Feature";
                AssetDatabase.AddObjectToAsset(background, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(background, out string _, out long localId);

                var serialized = new SerializedObject(data);
                SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
                SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
                if (features == null) { UnityEngine.Object.DestroyImmediate(background, true); continue; }
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = background;
                if (map != null) { map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId; }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                added++;
            }
            notes.Add(added > 0 ? $"- Camera picture: \"AR Background Renderer Feature\" added to {added} URP renderer(s) in Assets/Settings."
                                : "- Camera picture: the URP renderers already have the AR Background Renderer Feature.");
            return true;
        }

        // ---------------------------------------------------------------------------------------
        //  5. The four masks and the four layer materials
        // ---------------------------------------------------------------------------------------
        /// <summary>The layer materials (made on first use). Their pictures are the masks in Assets/_Project/Textures/FaceMakeup.</summary>
        public static Layers EnsureLayers(List<string> notes = null)
        {
            Directory.CreateDirectory(MaterialFolder);
            var layers = new Layers
            {
                face = Layer("ArMakeup_1_Face", "face_mask_face", 0, new Color(0.88f, 0.70f, 0.60f, 0f)),
                cheeks = Layer("ArMakeup_2_Cheeks", "face_mask_cheeks", 1, new Color(0.90f, 0.50f, 0.52f, 0f)),
                eyes = Layer("ArMakeup_3_Eyes", "face_mask_eyes", 2, new Color(0.62f, 0.42f, 0.50f, 0f)),
                lips = Layer("ArMakeup_4_Lips", "face_mask_lips", 3, new Color(0.78f, 0.26f, 0.36f, 0f))
            };
            // the highlight of the finishes (satin, glossy, shimmer, metallic): white, drawn on top; its mask is swapped to the part that is shown
            layers.shine = Layer("ArMakeup_5_Shine", "face_shine_lips", 4, new Color(1f, 1f, 1f, 0f), false);
            layers.shineMasks = new[] { Mask("face_shine_face"), Mask("face_shine_cheeks"), Mask("face_shine_eyes"), Mask("face_shine_lips") };
            bool complete = layers.face != null && layers.cheeks != null && layers.eyes != null && layers.lips != null;
            notes?.Add(complete ? "- Face layers: the four masks and materials are in place (Assets/_Project/Textures/FaceMakeup, Assets/_Project/Materials)."
                                : "- Face layers: a mask picture or the URP Unlit shader is missing, so a layer could not be made.");
            return layers;
        }

        /// <summary>The material of the guided preview's colour shapes: the same "worn, not painted" blend as on the tracked face.</summary>
        public static Material PreviewShapeMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "UiMakeupBlend.shader");
            if (shader == null) return null;
            Directory.CreateDirectory(MaterialFolder);
            string path = MaterialFolder + "ArMakeup_PreviewShape.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
            return material;
        }

        /// <summary>
        /// `worn` = the colour TINTS the skin and keeps its texture and light (shader GlowUp/FaceMakeupBlend); false = a plain
        /// see-through layer (the highlight of a finish, and every layer when that shader is missing).
        /// </summary>
        static Material Layer(string materialName, string maskName, int order, Color colour, bool worn = true)
        {
            Texture2D mask = Mask(maskName);
            Shader plain = Shader.Find("Universal Render Pipeline/Unlit");
            Shader tinting = worn ? AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "FaceMakeupBlend.shader") : null;
            Shader shader = tinting != null ? tinting : plain;
            if (mask == null || shader == null) return null;

            string path = MaterialFolder + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            if (material.shader != shader) material.shader = shader;

            // see-through, drawn over the camera picture, visible from both sides, no shadows
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.renderQueue = (int)RenderQueue.Transparent + order;       // face first, lips last
            material.SetTexture("_BaseMap", mask);
            material.SetColor("_BaseColor", colour);                           // invisible until the app sets a shade
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D Mask(string name)
        {
            string path = MaskFolder + name + ".png";
            if (!File.Exists(path)) { Debug.LogWarning("[GlowUp] Face mask not found: " + path); return null; }
            if (AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool right = importer.textureType == TextureImporterType.Default && importer.alphaIsTransparency && importer.wrapMode == TextureWrapMode.Clamp
                          && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.mipmapEnabled;
                if (!right)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.sRGBTexture = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 512;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;     // keeps the soft edges smooth
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
