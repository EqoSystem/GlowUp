using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using GlowUp.Networking;

namespace GlowUp.Scanning
{
    /// <summary>
    /// Hardened Gemini scan client used by AiSmartAnalysisScene.
    ///
    /// Behavior:
    /// - Walks GeminiApiConfig.ModelChain in order. A 404 (model retired) advances
    ///   to the next model immediately, no wasted retries on a dead endpoint.
    /// - Transient errors (timeout, 429, 5xx, connection error) retry on the SAME
    ///   model with exponential backoff + jitter, up to MaxRetriesPerModel times,
    ///   before advancing to the next model.
    /// - Terminal errors (400 bad request, 401/403 auth) stop immediately —
    ///   retrying a malformed request or bad key just burns quota.
    /// - Every attempt respects the CancellationToken so leaving the scan screen
    ///   actually aborts the in-flight request instead of updating UI after the
    ///   user has navigated away.
    /// </summary>
    public class GeminiApiClient
    {
        private readonly GeminiApiConfig _config;
        private readonly string _apiKey;

        /// <param name="apiKey">
    
        public GeminiApiClient(GeminiApiConfig config, string apiKey)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("API key is required.", nameof(apiKey));
            _apiKey = apiKey;
        }

        /// <param name="requestJsonBody">
      
        public async Task<GeminiScanResult> AnalyzeAsync(string requestJsonBody, CancellationToken cancellationToken = default)
        {
            int totalAttempts = 0;

            foreach (string model in _config.ModelChain)
            {
                for (int retry = 0; retry <= _config.MaxRetriesPerModel; retry++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return GeminiScanResult.Failed(GeminiErrorCode.Cancelled, "Cancelled by caller.", null, totalAttempts);

                    totalAttempts++;
                    var attemptResult = await TryOnceAsync(model, requestJsonBody, cancellationToken);

                    if (attemptResult.Success)
                    {
                        attemptResult.AttemptsMade = totalAttempts;
                        attemptResult.ModelUsed = model;
                        return attemptResult;
                    }

                    switch (attemptResult.ErrorCode)
                    {
                        
                        case GeminiErrorCode.AuthError:
                        case GeminiErrorCode.BadRequest:
                        case GeminiErrorCode.Cancelled:
                            attemptResult.AttemptsMade = totalAttempts;
                            return attemptResult;

                      
                        case GeminiErrorCode.ModelNotFound:
                            goto nextModel;

                        case GeminiErrorCode.Timeout:
                        case GeminiErrorCode.RateLimited:
                        case GeminiErrorCode.ServerError:
                        case GeminiErrorCode.NoInternet:
                        case GeminiErrorCode.MalformedResponse:
                        case GeminiErrorCode.Unknown:
                        default:
                            if (retry == _config.MaxRetriesPerModel)
                                goto nextModel; // exhausted retries on this model, try the next one

                            float delay = ComputeBackoffSeconds(retry);
                            { }
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                            }
                            catch (TaskCanceledException)
                            {
                                return GeminiScanResult.Failed(GeminiErrorCode.Cancelled, "Cancelled during backoff.", null, totalAttempts);
                            }
                            break;
                    }
                }
            nextModel:;
            }

            return GeminiScanResult.Failed(
                GeminiErrorCode.ModelNotFound,
                "All models in the configured chain were unavailable (check GeminiApiConfig.ModelChain for retired model IDs).",
                "We couldn't reach the scanning service right now. Please try again shortly.",
                totalAttempts);
        }

        private float ComputeBackoffSeconds(int retryIndex)
        {
            float exponential = _config.BaseBackoffSeconds * Mathf.Pow(2f, retryIndex);
            float jitter = UnityEngine.Random.Range(0f, _config.BaseBackoffSeconds * 0.5f);
            return Mathf.Min(exponential + jitter, _config.MaxBackoffSeconds);
        }

        private async Task<GeminiScanResult> TryOnceAsync(string model, string requestJsonBody, CancellationToken cancellationToken)
        {
            string url = _config.BuildUrl(model);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJsonBody);

            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-goog-api-key", _apiKey);
            req.timeout = _config.TimeoutSeconds;

            using var registration = cancellationToken.Register(() =>
            {
                if (!req.isDone) req.Abort();
            });

            try
            {
                await req.SendWebRequest();
            }
            catch (Exception ex)
            {
                return GeminiScanResult.Failed(GeminiErrorCode.Unknown, $"Unexpected exception sending request: {ex}", null, 0);
            }

            if (cancellationToken.IsCancellationRequested)
                return GeminiScanResult.Failed(GeminiErrorCode.Cancelled, "Request aborted by cancellation token.", null, 0);

            return ClassifyAndParse(req, model);
        }

        private GeminiScanResult ClassifyAndParse(UnityWebRequest req, string model)
        {
            long status = req.responseCode;

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                return GeminiScanResult.Failed(
                    GeminiErrorCode.NoInternet,
                    $"Connection error: {req.error}",
                    "No internet connection. Please check your network and try again.",
                    0);
            }

            if (req.result == UnityWebRequest.Result.DataProcessingError)
            {
                return GeminiScanResult.Failed(
                    GeminiErrorCode.MalformedResponse,
                    $"Data processing error: {req.error}",
                    "We had trouble reading the scan result. Please try again.",
                    0);
            }

           
            if (req.error != null && req.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GeminiScanResult.Failed(
                    GeminiErrorCode.Timeout,
                    $"Timed out after {_config.TimeoutSeconds}s.",
                    "The scan is taking longer than expected. Please try again.",
                    0);
            }

            if (req.result == UnityWebRequest.Result.ProtocolError)
            {
                switch (status)
                {
                    case 400:
                        return GeminiScanResult.Failed(GeminiErrorCode.BadRequest,
                            $"400 Bad Request: {req.downloadHandler?.text}",
                            "Something went wrong preparing your scan. Please retake the photo.", 0);
                    case 401:
                    case 403:
                        return GeminiScanResult.Failed(GeminiErrorCode.AuthError,
                            $"{status} Auth error: {req.downloadHandler?.text}",
                            "We couldn't authenticate the scanning service. Please try again later.", 0);
                    case 404:
                        return GeminiScanResult.Failed(GeminiErrorCode.ModelNotFound,
                            $"404 on model '{model}' — likely retired/renamed.",
                            null, 0);
                    case 429:
                        return GeminiScanResult.Failed(GeminiErrorCode.RateLimited,
                            $"429 Rate limited on model '{model}'.",
                            "The scanning service is busy. Retrying shortly.", 0);
                    default:
                        if (status >= 500)
                        {
                            return GeminiScanResult.Failed(GeminiErrorCode.ServerError,
                                $"{status} Server error: {req.downloadHandler?.text}",
                                "The scanning service is temporarily unavailable.", 0);
                        }
                        return GeminiScanResult.Failed(GeminiErrorCode.Unknown,
                            $"Unexpected status {status}: {req.downloadHandler?.text}",
                            "Something unexpected happened. Please try again.", 0);
                }
            }

            // Success at the transport level — now validate the payload itself.
            return ParseSuccessBody(req.downloadHandler?.text);
        }

        private GeminiScanResult ParseSuccessBody(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return GeminiScanResult.Failed(GeminiErrorCode.MalformedResponse,
                    "200 OK but response body was empty.",
                    "We didn't get a result for that scan. Please try again.", 0);
            }

            try
            {
                // 1) Unwrap Gemini's envelope to get to the model's text output.
                string extractedText = ExtractGeminiText(rawJson);
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return GeminiScanResult.Failed(GeminiErrorCode.MalformedResponse,
                        $"Could not locate text content in Gemini response: {Truncate(rawJson)}",
                        "We couldn't read the scan result. Please try again.", 0);
                }

                // 2) Strip markdown code fences if the model wrapped its JSON in ```json ... ```.
                string cleanJson = StripCodeFences(extractedText);

                // 3) Deserialize into our expected schema. JsonUtility silently leaves
                //    fields at their default if they're missing, so do a minimal sanity
                //    check rather than trusting it blindly.
                var analysis = JsonUtility.FromJson<ProductAnalysis>(cleanJson);
                if (analysis == null || string.IsNullOrEmpty(analysis.ProductSummary))
                {
                    return GeminiScanResult.Failed(GeminiErrorCode.MalformedResponse,
                        $"Parsed but schema looked empty/invalid: {Truncate(cleanJson)}",
                        "The scan result didn't come through correctly. Please try again.", 0);
                }

                return GeminiScanResult.Ok(analysis, modelUsed: null, attempts: 0);
            }
            catch (Exception ex)
            {
                return GeminiScanResult.Failed(GeminiErrorCode.MalformedResponse,
                    $"Exception parsing response: {ex.Message}. Raw: {Truncate(rawJson)}",
                    "We had trouble reading the scan result. Please try again.", 0);
            }
        }

        /// <summary>
        /// Pulls candidates[0].content.parts[0].text out of the raw Gemini envelope
        /// without a full JSON object graph, since JsonUtility can't deserialize the
        /// nested array-of-objects envelope directly. Replace with a full JSON library
        /// (e.g. Newtonsoft) if your payload structure grows more complex.
        /// </summary>
        private string ExtractGeminiText(string rawJson)
        {
            var wrapper = JsonUtility.FromJson<GeminiEnvelope>(rawJson);
            if (wrapper?.candidates == null || wrapper.candidates.Length == 0) return null;
            var content = wrapper.candidates[0].content;
            if (content?.parts == null || content.parts.Length == 0) return null;
            return content.parts[0].text;
        }

        private string StripCodeFences(string text)
        {
            string t = text.Trim();
            if (t.StartsWith("```"))
            {
                int firstNewline = t.IndexOf('\n');
                if (firstNewline >= 0) t = t.Substring(firstNewline + 1);
                int lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0) t = t.Substring(0, lastFence);
            }
            return t.Trim();
        }

        private string Truncate(string s, int max = 300) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        [Serializable] private class GeminiEnvelope { public Candidate[] candidates; }
        [Serializable] private class Candidate { public Content content; }
        [Serializable] private class Content { public Part[] parts; }
        [Serializable] private class Part { public string text; }
    }
}