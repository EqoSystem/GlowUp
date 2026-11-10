using System;
using UnityEngine;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Single-choice group of tiles (the skin type tiles of Step 2, the skin tone tiles of Step 3).
    /// It sits on the object that holds the tiles. Tapping a tile selects it and unselects the others;
    /// Changed is raised with the chosen value.
    ///
    /// This class has its own file on purpose: Unity can only keep a script attached to an object in a
    /// saved scene when the script's class and its file have the same name.
    /// </summary>
    public class TileSelectionGroup : MonoBehaviour
    {
        [SerializeField] private SelectableTile[] tiles;

        public string Value { get; private set; } = "";
        public event Action<string> Changed;

        void Awake()
        {
            if (tiles == null) return;
            foreach (var t in tiles) if (t != null) t.Clicked += OnTile;
        }

        void OnTile(SelectableTile t) => Select(t.Value);

        public void Select(string v, bool notify = true)
        {
            Value = v ?? "";
            if (tiles != null)
                foreach (var t in tiles) if (t != null) t.SetSelected(t.Value == Value, notify);      // a restored answer (notify false) does not animate
            if (notify) Changed?.Invoke(Value);
        }
    }
}
