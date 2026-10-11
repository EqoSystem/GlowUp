using System;
using System.IO;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Tiny JSON file cache in persistentDataPath/GlowUpCache. Used for the profile,
    /// ingredient database, brands, scan history and the pending-write queue so the app
    /// works offline. Wrap any Serializable class (not a bare list) in a holder type.
    /// </summary>
    public static class OfflineCache
    {
        static string Dir => Path.Combine(Application.persistentDataPath, "GlowUpCache");
        static string PathFor(string key) => Path.Combine(Dir, key + ".json");

        public static void Save<T>(string key, T data)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(PathFor(key), JsonUtility.ToJson(data));
            }
            catch (Exception e) { Debug.LogWarning($"OfflineCache.Save({key}) failed: {e.Message}"); }
        }

        public static bool TryLoad<T>(string key, out T data)
        {
            data = default;
            try
            {
                var p = PathFor(key);
                if (!File.Exists(p)) return false;
                data = JsonUtility.FromJson<T>(File.ReadAllText(p));
                return data != null;
            }
            catch (Exception e) { Debug.LogWarning($"OfflineCache.Load({key}) failed: {e.Message}"); return false; }
        }

        public static void Delete(string key)
        {
            try { var p = PathFor(key); if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }
}
