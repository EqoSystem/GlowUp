using System.Collections;
using System.Collections.Generic;
using Firebase.Firestore;

namespace GlowUp.Data
{
    /// <summary>
    /// Reads fields from a Firestore document without ever failing on a missing field or an
    /// unexpected type (for example an age saved as text, or a list saved as one comma-separated text).
    /// </summary>
    public class FirestoreFields
    {
        readonly Dictionary<string, object> d;
        public string Id { get; }

        public FirestoreFields(DocumentSnapshot snapshot)
        {
            Id = snapshot.Id;
            d = snapshot.Exists ? snapshot.ToDictionary() : new Dictionary<string, object>();
        }

        public string Str(string key, string fallback = "")
        {
            return d.TryGetValue(key, out object v) && v != null && v.ToString().Length > 0 ? v.ToString() : fallback;
        }

        public string NestedStr(string mapKey, string key, string fallback = "")
        {
            Dictionary<string, object> map = Map(mapKey);
            return map.TryGetValue(key, out object v) && v != null && v.ToString().Length > 0 ? v.ToString() : fallback;
        }

        public int Int(string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return 0;
            if (v is long l) return (int)l;
            if (v is double db) return (int)db;
            if (v is int i) return i;
            return int.TryParse(v.ToString(), out int parsed) ? parsed : 0;
        }

        public long Long(string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return 0;
            if (v is long l) return l;
            if (v is double db) return (long)db;
            return long.TryParse(v.ToString(), out long parsed) ? parsed : 0;
        }

        public bool Bool(string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return false;
            if (v is bool b) return b;
            return v.ToString().ToLowerInvariant() == "true";
        }

        public List<string> List(string key)
        {
            var list = new List<string>();
            if (!d.TryGetValue(key, out object v) || v == null) return list;
            AddListValues(list, v);
            return list;
        }

        public List<string> NestedList(string mapKey, string key)
        {
            var list = new List<string>();
            Dictionary<string, object> map = Map(mapKey);
            if (map.TryGetValue(key, out object value) && value != null) AddListValues(list, value);
            return list;
        }

        Dictionary<string, object> Map(string key)
        {
            return d.TryGetValue(key, out object value) && value is Dictionary<string, object> map
                ? map
                : new Dictionary<string, object>();
        }

        static void AddListValues(List<string> list, object value)
        {
            if (value is string text)
            {
                foreach (string part in text.Split(new[] { ',', ';', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
                    if (part.Trim().Length > 0) list.Add(part.Trim());
            }
            else if (value is IEnumerable items)
            {
                foreach (object o in items) if (o != null && o.ToString().Trim().Length > 0) list.Add(o.ToString().Trim());
            }
        }
    }
}
