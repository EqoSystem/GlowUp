using System.IO;
using GlowUp.Common;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// New20 (17 November 2026): the freezing sound of Recent Scans was never heard on a phone. The app cut its short piece out
    /// of the MP3 at run time with AudioClip.GetData, which on Android can give no samples for a compressed clip that is not
    /// preloaded; every failure was silent. Here the same cut (DeleteSoundPlayer.Cut, with the player's own settings) is made in
    /// the Editor and saved as Assets/_Project/Audio/Delete_FreezingCold_Piece.wav: plain 16-bit PCM, decompressed on load and
    /// preloaded, so the phone only has to play it. The file is written only when its content changes.
    /// </summary>
    public static class DeleteSoundPiece
    {
        public const string PiecePath = "Assets/_Project/Audio/Delete_FreezingCold_Piece.wav";

        /// <summary>Cuts the piece of `clip` with the settings on `player` (a DeleteSoundPlayer) and returns the saved WAV, or null.</summary>
        public static AudioClip Make(SerializedObject player, AudioClip clip)
        {
            if (clip == null) { Debug.LogWarning("[GlowUp] Delete sound: no clip to cut a piece from."); return AssetDatabase.LoadAssetAtPath<AudioClip>(PiecePath); }
            bool wasLoaded = clip.loadState == AudioDataLoadState.Loaded;
            if (!wasLoaded) clip.LoadAudioData();
            var all = new float[clip.samples * clip.channels];
            bool read = clip.GetData(all, 0);
            if (!read)
            {
                Debug.LogWarning("[GlowUp] Delete sound: the Editor could not read " + AssetDatabase.GetAssetPath(clip) + "; the existing piece (if any) is kept.");
                return AssetDatabase.LoadAssetAtPath<AudioClip>(PiecePath);
            }
            float[] cut = DeleteSoundPlayer.Cut(all, clip.channels, clip.frequency,
                Bool(player, "skipSilenceAtStart", true), Float(player, "startAtSeconds", 0f), Float(player, "playSeconds", 1.1f),
                Float(player, "fadeOutSeconds", 0.5f), Float(player, "loudestPoint", 0.85f), Float(player, "mostGain", 6f));
            if (cut.Length == 0) { Debug.LogWarning("[GlowUp] Delete sound: the clip is silent; no piece made."); return AssetDatabase.LoadAssetAtPath<AudioClip>(PiecePath); }

            byte[] wav = Wav(cut, clip.channels, clip.frequency);
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), PiecePath);
            bool same = File.Exists(full) && Same(File.ReadAllBytes(full), wav);
            if (!same)
            {
                File.WriteAllBytes(full, wav);
                AssetDatabase.ImportAsset(PiecePath, ImportAssetOptions.ForceUpdate);
            }
            KeepLoaded(PiecePath, pcm: true);
            var piece = AssetDatabase.LoadAssetAtPath<AudioClip>(PiecePath);
            Debug.Log($"[GlowUp] Delete sound piece {(same ? "unchanged" : "written")}: {cut.Length / clip.channels / (float)clip.frequency:0.00} s, {clip.channels} channel(s), {clip.frequency} Hz.");
            return piece;
        }

        /// <summary>The clip at `path` is decompressed when the scene loads and kept ready (preloaded), so its first play is never late or silent.</summary>
        public static void KeepLoaded(string path, bool pcm = false)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            bool change = s.loadType != AudioClipLoadType.DecompressOnLoad || !s.preloadAudioData || (pcm && s.compressionFormat != AudioCompressionFormat.PCM);
            if (!change) return;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = true;
            if (pcm) s.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static bool Bool(SerializedObject o, string name, bool fallback) { var p = o.FindProperty(name); return p != null ? p.boolValue : fallback; }
        static float Float(SerializedObject o, string name, float fallback) { var p = o.FindProperty(name); return p != null ? p.floatValue : fallback; }

        /// <summary>16-bit PCM WAV bytes of interleaved samples in -1..1.</summary>
        static byte[] Wav(float[] samples, int channels, int frequency)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                int data = samples.Length * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + data);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(frequency); w.Write(frequency * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(data);
                foreach (float v in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f));
                w.Flush();
                return stream.ToArray();
            }
        }
    }
}
