using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>Fills one submission row (a copy of the scene's row template) by the names of its children.</summary>
    public static class SubmissionRows
    {
        public static readonly Color Approved = new Color(0.20f, 0.56f, 0.38f), Rejected = new Color(0.78f, 0.30f, 0.38f),
                                     Pending = new Color(0.80f, 0.50f, 0.58f), Attention = new Color(0.80f, 0.52f, 0.10f), Unsynced = new Color(0.55f, 0.50f, 0.52f);

        public static void Fill(GameObject row, SubmissionStore.Info i)
        {
            Set(row, "NameText", string.IsNullOrEmpty(i.name) ? "(no name)" : i.name);
            Set(row, "BrandText", string.IsNullOrEmpty(i.brand) ? "Brand not known" : i.brand);
            Set(row, "TimeText", SubmissionStore.Ago(i.addedUnix));
            string label = i.Label;
            TextMeshProUGUI status = Set(row, "StatusText", label);
            Color c = ColorOf(label);
            if (status != null) status.color = c;
            Transform pill = row.transform.Find("StatusPill");
            if (pill != null && pill.TryGetComponent(out Image img)) img.color = new Color(c.r, c.g, c.b, 0.16f);
        }

        public static Color ColorOf(string label) =>
            label == "Approved" ? Approved : label == "Rejected" || label == "Removed" ? Rejected :
            label == "Changes requested" ? Attention : label == "Not yet synced" ? Unsynced : Pending;

        static TextMeshProUGUI Set(GameObject row, string child, string text)
        {
            foreach (TextMeshProUGUI t in row.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t.name == child) { t.text = text; return t; }
            return null;
        }
    }
}
