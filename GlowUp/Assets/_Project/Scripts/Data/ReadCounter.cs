using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GlowUp.Data
{
    /// <summary>
    /// Counts the documents the app reads from Firestore, per call, per scan and per session, and writes each to the Console.
    /// Editor and Development Builds only: in a release build every call to this class is removed by the compiler
    /// ([Conditional]), so it costs nothing and prints nothing.
    /// </summary>
    public static class ReadCounter
    {
        static readonly object gate = new object();
        static int scanTotal, sessionTotal, scanNumber;

        /// <summary>One call read `documents` documents (a count query is 1). `label` says which call.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Add(string label, int documents)
        {
            if (documents <= 0) return;
            int scan, session;
            lock (gate) { scanTotal += documents; sessionTotal += documents; scan = scanTotal; session = sessionTotal; }
            Debug.Log("[reads] " + label + ": " + documents + " (this scan " + scan + ", this session " + session + ")");
        }

        /// <summary>A query read `documents` documents. An empty result still bills 1 read, so it is counted as 1 and says so in the label.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void AddQuery(string label, int documents)
        {
            if (documents <= 0) Add(label + " (empty result, billed as 1 read)", 1);
            else Add(label, documents);
        }

        /// <summary>A new scan begins: the total of the one before is written, and the scan total starts again at 0.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void NewScan()
        {
            int finished, number;
            lock (gate) { finished = scanTotal; number = ++scanNumber; scanTotal = 0; }
            Debug.Log("[reads] scan " + (number - 1) + " read " + finished + " documents in all. Scan " + number + " begins.");
        }
    }
}
