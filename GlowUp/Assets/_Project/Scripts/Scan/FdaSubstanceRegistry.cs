using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Scan
{
    /// <summary>
    /// Checks an ingredient name against the US FDA's public substance registry (openFDA, no key needed):
    ///     https://api.fda.gov/other/substance.json?search=names.name:"NIACINAMIDE"
    /// A hit returns the FDA's Unique Ingredient Identifier (UNII), which shows the name is a real,
    /// registered substance and not a misread label. It says nothing about safety: the FDA has no public
    /// list of cosmetic products or of ingredient safety ratings.
    /// Needs the internet. Any failure simply means "not confirmed".
    /// </summary>
    public static class FdaSubstanceRegistry
    {
        public static string Address = "https://api.fda.gov/other/substance.json";

        /// <summary>The FDA's UNII code for this ingredient name, or "" when the registry does not list it (or cannot be reached).</summary>
        public static async Task<string> FindUnii(string ingredientName, float timeoutSeconds = 6f)
        {
            string name = (ingredientName ?? "").Trim().ToUpperInvariant().Replace("\"", "");
            if (name.Length < 3) return "";
            try
            {
                string url = Address + "?search=names.name:%22" + UnityWebRequest.EscapeURL(name).Replace("+", "%20") + "%22&limit=1";
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = Mathf.Max(2, Mathf.RoundToInt(timeoutSeconds));
                    await request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200) return "";   // 404 = not listed

                    var root = MiniJson.Deserialize(request.downloadHandler.text) as Dictionary<string, object>;
                    var results = root != null && root.TryGetValue("results", out object r) ? r as List<object> : null;
                    var first = results != null && results.Count > 0 ? results[0] as Dictionary<string, object> : null;
                    return first != null && first.TryGetValue("unii", out object unii) && unii is string code ? code : "";
                }
            }
            catch (Exception e)
            {
                { }
                return "";
            }
        }
    }
}
