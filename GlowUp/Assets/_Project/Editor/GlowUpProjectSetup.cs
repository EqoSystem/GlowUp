using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Keeps the settings every Android build of Glow Up needs in place, without anyone having to
    /// remember them. It checks them each time the Editor loads the scripts and says in the Console
    /// what it changed (nothing is printed when everything is already right):
    ///
    ///  - the app is built for 32-bit AND 64-bit phones (ARMv7 + ARM64). A 32-bit-only app shows
    ///    "This app isn't compatible with the latest version of Android" on new phones, and Google's
    ///    AR service closes it when live face tracking starts;
    ///  - the Gradle template lists the text reader the scan screen uses and keeps 64-bit libraries;
    ///  - the start-up logo and the app icon (see BrandingSetup);
    ///  - what finger signs need: the hand model, its library line and the launcher manifest (see FingerSignSetup).
    ///
    /// Just before an Android build the Gradle template is checked once more.
    /// </summary>
    [InitializeOnLoad]
    public class GlowUpProjectSetup : IPreprocessBuildWithReport
    {
        static GlowUpProjectSetup()
        {
            EditorApplication.delayCall += Check;       // wait until the Editor has finished loading
        }

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var notes = new List<string>();
            try
            {
                ArFaceTrackingSetup.SetArchitectures(notes);
                BrandingSetup.Apply(notes);
                FingerSignSetup.Apply(notes);
            }
            catch (Exception e) { Debug.LogWarning("[GlowUp] The project settings could not be checked: " + e.Message); }
            if (notes.Count > 0) Debug.Log("[GlowUp] Project settings updated:\n" + string.Join("\n", notes));
        }

        public int callbackOrder => -1000;      // before the AR plug-in's own checks

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var notes = new List<string>();
            try { ArFaceTrackingSetup.FixGradleTemplate(notes); FingerSignSetup.Apply(notes); }
            catch (Exception e) { Debug.LogWarning("[GlowUp] The Gradle template could not be checked: " + e.Message); }
            if (notes.Count > 0) Debug.Log("[GlowUp] Before the build:\n" + string.Join("\n", notes));

            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                Debug.LogWarning("[GlowUp] This build is 32-bit only (ARM64 is not ticked under Player > Other Settings > Target Architectures). " +
                                 "New phones will say the app is not compatible, and live face tracking will be switched off on them.");
        }
    }
}
