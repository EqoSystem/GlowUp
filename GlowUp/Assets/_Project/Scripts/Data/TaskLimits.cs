using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Net;

namespace GlowUp.Data
{
    /// <summary>
    /// Time limits for anything that waits for the internet. A phone can be "connected" (Wi-Fi with no
    /// internet behind it, one bar of signal) and never get an answer; without a limit the screen that
    /// is waiting would wait forever. With these helpers every wait ends, and the caller carries on with
    /// what is saved on the phone.
    /// </summary>
    public static class TaskLimits
    {
        /// <summary>
        /// Waits for the task, but no longer than `seconds`. True = it finished in time (a failure is thrown
        /// to the caller as usual). False = it is still running; it carries on in the background.
        /// </summary>
        public static async Task<bool> Within(this Task task, float seconds)
        {
            if (!task.IsCompleted)
            {
                Task first = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(Math.Max(0.1f, seconds))));
                if (first != task) { Quiet(task); return false; }
            }
            await task;
            return true;
        }

        /// <summary>Waits for the task; if it takes longer than `seconds` a TimeoutException is thrown.</summary>
        public static async Task OrTimeout(this Task task, float seconds, string what)
        {
            if (!await task.Within(seconds)) throw new TimeoutException(what + " took too long (slow or no internet).");
        }

        /// <summary>Waits for the task's result; if it takes longer than `seconds` a TimeoutException is thrown.</summary>
        public static async Task<T> OrTimeout<T>(this Task<T> task, float seconds, string what)
        {
            if (!await ((Task)task).Within(seconds)) throw new TimeoutException(what + " took too long (slow or no internet).");
            return task.Result;
        }

        /// <summary>True when the failure was about the connection (no answer, or too slow), not about what was sent.</summary>
        public static bool IsNetworkProblem(Exception e)
        {
            if (e == null) return false;
            if (e is AggregateException many)
            {
                foreach (Exception inner in many.Flatten().InnerExceptions) if (IsNetworkProblem(inner)) return true;
                return false;
            }
            for (Exception x = e; x != null; x = x.InnerException)
            {
                if (x is TimeoutException) return true;
                if (x is FirebaseException firebase && (AuthError)firebase.ErrorCode == AuthError.NetworkRequestFailed) return true;
            }
            return false;
        }

        /// <summary>The Firebase failure inside an exception, if there is one.</summary>
        public static FirebaseException FirebaseError(Exception e)
        {
            if (e is AggregateException many)
            {
                foreach (Exception inner in many.Flatten().InnerExceptions) { FirebaseException found = FirebaseError(inner); if (found != null) return found; }
                return null;
            }
            for (Exception x = e; x != null; x = x.InnerException) if (x is FirebaseException firebase) return firebase;
            return null;
        }

        /// <summary>
        /// A list that the database library answered from its own store on the phone (because the cloud gave
        /// no answer) is not a fresh download. It is refused when the app already has its own copy, or when it
        /// is empty, so a dead connection can never replace a good list with an old or empty one.
        /// </summary>
        public static void RefuseStale(QuerySnapshot list, bool haveOwnCopy)
        {
            if (list.Metadata.IsFromCache && (haveOwnCopy || list.Count == 0))
                throw new TimeoutException("The cloud gave no answer (slow or no internet).");
            ConnectivityMonitor.ReportAnswer();
        }

        /// <summary>Call from the catch of a download: when the failure was the connection, other downloads pause for a moment.</summary>
        public static void NoteFailure(Exception e)
        {
            if (IsNetworkProblem(e)) ConnectivityMonitor.ReportNoAnswer();
        }

        // a task nobody waits for any more must not report its failure as an unhandled error
        static void Quiet(Task task)
        {
            task.ContinueWith(t => { Exception ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
