using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Put this on an input field whose badges, check line or requirement list sit next to it or under it (the email's check
    /// line, the password's checklist). While the field is being typed in, KeyboardAvoider keeps the field AND everything listed
    /// here above the keyboard. A field without this gets "Space Below Field" (KeyboardAvoider) for a small badge.
    /// The scene builders fill the list; add or remove objects here in the Inspector.
    /// </summary>
    public class KeyboardKeepVisible : MonoBehaviour
    {
        [Tooltip("Badges, check lines and requirement rows that belong to this field (switched-off ones are skipped)")]
        [SerializeField] private RectTransform[] alsoShow = new RectTransform[0];
        [Tooltip("Extra room under the lowest of them, in canvas units")]
        [Min(0f)] [SerializeField] private float extraBelow = 8f;

        public RectTransform[] AlsoShow => alsoShow;
        public float ExtraBelow => extraBelow;

        /// <summary>
        /// Used by the scene builders. Only objects inside this field's own container (its parent) are kept, so a badge of
        /// another field or a far-away panel can never be listed by mistake; nulls and the field itself are dropped.
        /// </summary>
        public void Set(RectTransform[] items)
        {
            var kept = new System.Collections.Generic.List<RectTransform>();
            Transform container = transform.parent;
            if (items != null && container != null)
                foreach (RectTransform item in items)
                    if (item != null && item != transform && item.IsChildOf(container) && !kept.Contains(item)) kept.Add(item);
            alsoShow = kept.ToArray();
        }
    }
}
