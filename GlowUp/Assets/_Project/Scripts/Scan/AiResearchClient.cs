using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Networking;
using GlowUp.Scanning;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Scan
{
    /// <summary>
    /// Asks the AI (Gemini) a research question and returns its answer as text, together with the web
    /// pages it used when "search the web" is on. Used to look up ingredients the app has never seen.
    /// It walks the model list of the AI settings (GeminiApiConfig > Model Chain).
    /// If a model or key does not offer web search, the same question is asked again without it.
    /// Needs the internet; the caller checks that first.
    /// </summary>
    public static class AiResearchClient
    {
        public class Answer
        {
            public bool ok;
            public string text = "";
            public string error = "";
            public string model = "";
            /// <summary>True when the answer is backed by a live web search (then `sources` lists the pages).</summary>
            public bool usedWebSearch;
            /// <summary>Pages the web search used, each as "site name | address".</summary>
            public List<string> sources = new List<string>();
        }

        public static Task<Answer> Ask(GeminiApiConfig config, string apiKey, string question, bool searchTheWeb, float timeoutSeconds)
        {
            return Ask(config, apiKey, question, searchTheWeb, timeoutSeconds, (IList<byte[]>)null);
        }

        /// <param name="jpegPicture">A JPG picture the question is about (a photo of a product), or null.</param>
        public static Task<Answer> Ask(GeminiApiConfig config, string apiKey, string question, bool searchTheWeb, float timeoutSeconds, byte[] jpegPicture)
        {
            return Ask(config, apiKey, question, searchTheWeb, timeoutSeconds, jpegPicture != null && jpegPicture.Length > 0 ? new List<byte[]> { jpegPicture } : null);
        }

        /// <param name="jpegPictures">JPG pictures the question is about (the sides of one product), sent together in ONE request. Null or empty = none.</param>
        public static async Task<Answer> Ask(GeminiApiConfig config, string apiKey, string question, bool searchTheWeb, float timeoutSeconds, IList<byte[]> jpegPictures)
        {
            var last = new Answer { error = "No AI model is configured." };
            if (config == null || string.IsNullOrEmpty(apiKey) || config.ModelChain == null) return last;

            // switched off for the rest of this question once the service refuses it, and not even tried for a while after a
            // refusal (the key does not offer web search: asking with it first would waste a request on every question)
            bool searchOffered = searchTheWeb && AiBudget.WebSearchWorthTrying;
            foreach (string model in config.ModelChain)
            {
                bool[] modes = searchOffered ? new[] { true, false } : new[] { false };
                foreach (bool withSearch in modes)
                {
                    using (var request = new UnityWebRequest(config.BuildUrl(model), "POST"))
                    {
                        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Body(question, withSearch, jpegPictures)));
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.SetRequestHeader("x-goog-api-key", apiKey);
                        request.timeout = Mathf.Max(5, Mathf.RoundToInt(timeoutSeconds));
                        await request.SendWebRequest();

                        long status = request.responseCode;
                        if (request.result == UnityWebRequest.Result.Success && status == 200)
                        {
                            Answer answer = Read(request.downloadHandler.text);
                            answer.model = model;
                            if (answer.ok) return answer;
                            last = answer;
                            continue;                                   // empty answer: try the plain mode / next model
                        }

                        last = new Answer { error = $"{model}: HTTP {status} {request.error}" };
                        if (status == 401 || status == 403) return last; // the key is refused: no point trying on
                        if (status == 404) break;                        // this model is gone: next model
                        if (status == 0) return last;                    // no connection / timed out
                        // 400 or 429 with web search usually means "search is not offered on this key or plan": ask again without it
                        if (withSearch && (status == 400 || status == 429)) { searchOffered = false; AiBudget.WebSearchRefused(); }
                    }
                }
            }
            return last;
        }

        static string Body(string question, bool withSearch, IList<byte[]> jpegPictures)
        {
            int size = 0;
            if (jpegPictures != null) foreach (byte[] one in jpegPictures) if (one != null) size += one.Length * 4 / 3 + 80;
            var sb = new StringBuilder(question.Length + 200 + size);
            sb.Append("{\"contents\":[{\"role\":\"user\",\"parts\":[");
            if (jpegPictures != null)
                foreach (byte[] one in jpegPictures)
                    if (one != null && one.Length > 0) sb.Append("{\"inline_data\":{\"mime_type\":\"image/jpeg\",\"data\":\"").Append(Convert.ToBase64String(one)).Append("\"}},");
            sb.Append("{\"text\":\"").Append(Escape(question)).Append("\"}]}],");
            if (withSearch) sb.Append("\"tools\":[{\"google_search\":{}}],");
            sb.Append("\"generationConfig\":{\"temperature\":0.1}}");
            return sb.ToString();
        }

        /// <summary>Pulls the text and the search sources out of Gemini's reply.</summary>
        static Answer Read(string raw)
        {
            var answer = new Answer();
            try
            {
                var root = MiniJson.Deserialize(raw) as Dictionary<string, object>;
                var candidates = root != null && root.TryGetValue("candidates", out object c) ? c as List<object> : null;
                var first = candidates != null && candidates.Count > 0 ? candidates[0] as Dictionary<string, object> : null;
                if (first == null) { answer.error = "The AI sent no answer."; return answer; }

                var text = new StringBuilder();
                if (first.TryGetValue("content", out object contentObject) && contentObject is Dictionary<string, object> content
                    && content.TryGetValue("parts", out object partsObject) && partsObject is List<object> parts)
                    foreach (object part in parts)
                        if (part is Dictionary<string, object> p && p.TryGetValue("text", out object t) && t is string s) text.Append(s);
                answer.text = text.ToString().Trim();

                if (first.TryGetValue("groundingMetadata", out object groundingObject) && groundingObject is Dictionary<string, object> grounding
                    && grounding.TryGetValue("groundingChunks", out object chunksObject) && chunksObject is List<object> chunks)
                    foreach (object chunk in chunks)
                    {
                        if (!(chunk is Dictionary<string, object> ch) || !ch.TryGetValue("web", out object webObject) || !(webObject is Dictionary<string, object> web)) continue;
                        string uri = web.TryGetValue("uri", out object u) ? u as string : null;
                        string title = web.TryGetValue("title", out object ti) ? ti as string : null;
                        if (string.IsNullOrEmpty(uri) || !(uri.StartsWith("https://") || uri.StartsWith("http://"))) continue;
                        string line = (string.IsNullOrWhiteSpace(title) ? "web page" : title.Trim()) + " | " + uri;
                        if (!answer.sources.Contains(line) && answer.sources.Count < 8) answer.sources.Add(line);
                    }
                answer.usedWebSearch = answer.sources.Count > 0;
                answer.ok = answer.text.Length > 0;
                if (!answer.ok) answer.error = "The AI's answer was empty.";
            }
            catch (Exception e) { answer.ok = false; answer.error = "Could not read the AI's answer: " + e.Message; }
            return answer;
        }

        /// <summary>The first complete JSON object inside a text (the AI sometimes wraps it in ```json fences or adds a sentence).</summary>
        public static Dictionary<string, object> JsonObjectIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{'), end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            try { return MiniJson.Deserialize(text.Substring(start, end - start + 1)) as Dictionary<string, object>; }
            catch { return null; }
        }

        static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append(' '); else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
