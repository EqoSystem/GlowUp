using System;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;

namespace GlowUp.Networking
{
    /// <summary>
    /// Lets you `await` a UnityWebRequestAsyncOperation directly:
    ///     using var req = UnityWebRequest.Post(url, body);
    ///     await req.SendWebRequest();
    /// This keeps retry/backoff logic linear instead of nested coroutines.
    /// </summary>
    public static class UnityWebRequestAwaiterExtensions
    {
        public static UnityWebRequestAwaiter GetAwaiter(this UnityWebRequestAsyncOperation op)
        {
            return new UnityWebRequestAwaiter(op);
        }
    }

    public readonly struct UnityWebRequestAwaiter : INotifyCompletion
    {
        private readonly UnityWebRequestAsyncOperation _op;

        public UnityWebRequestAwaiter(UnityWebRequestAsyncOperation op) => _op = op;

        public bool IsCompleted => _op.isDone;

        public void GetResult()
        {
            // Intentionally does NOT throw on protocol/connection errors.
            // GeminiApiClient inspects req.result itself so it can distinguish
            // retryable errors (timeout, 5xx, 429) from terminal ones (400, 401).
        }

        public void OnCompleted(Action continuation)
        {
            _op.completed += _ => continuation();
        }
    }
}