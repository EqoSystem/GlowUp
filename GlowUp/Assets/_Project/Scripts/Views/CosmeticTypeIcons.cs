using System;
using UnityEngine;

namespace GlowUp.Views
{
    /// <summary>
    /// Which icon goes with which kind of product. One shared asset
    /// (Assets/_Project/CosmeticTypeIcons.asset) used by the dashboard, recent scans, result and AR scenes.
    /// Add a line, or swap an icon, in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "GlowUp/Cosmetic Type Icons", fileName = "CosmeticTypeIcons")]
    public class CosmeticTypeIcons : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Words that identify the product type, separated by commas (e.g. lipstick, lip)")]
            public string keywords;
            public Sprite icon;
        }

        [Tooltip("Checked from top to bottom; the first line with a matching word wins")]
        public Entry[] entries = new Entry[0];
        [Tooltip("Used when no line matches")]
        public Sprite fallback;

        public Sprite For(string cosmeticType)
        {
            string t = (cosmeticType ?? "").ToLowerInvariant();
            if (t.Length > 0 && entries != null)
                foreach (var e in entries)
                {
                    if (e == null || e.icon == null || string.IsNullOrEmpty(e.keywords)) continue;
                    foreach (string k in e.keywords.Split(','))
                        if (k.Trim().Length > 0 && t.Contains(k.Trim().ToLowerInvariant())) return e.icon;
                }
            return fallback;
        }
    }
}
