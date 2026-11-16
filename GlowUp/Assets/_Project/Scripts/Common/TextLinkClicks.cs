using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Common
{
    /// <summary>
    /// Makes the &lt;link="id"&gt; parts of a TextMeshPro text tappable: a tap on one calls Clicked(id). A tap beside the links,
    /// but on the text, goes to the nearest link on the same line, so a short word is still easy to hit with a finger.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class TextLinkClicks : MonoBehaviour, IPointerClickHandler
    {
        public Action<string> Clicked;
        TMP_Text text;

        void Awake() { text = GetComponent<TMP_Text>(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (text == null || Clicked == null) return;
            Canvas canvas = text.canvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            int index = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, cam);
            if (index < 0) index = NearestLink(eventData.position, cam);
            if (index < 0 || index >= text.textInfo.linkCount) return;
            Clicked(text.textInfo.linkInfo[index].GetLinkID());
        }

        /// <summary>The link whose letters are closest to the tap, or -1 when the text has none.</summary>
        int NearestLink(Vector2 screen, Camera cam)
        {
            TMP_TextInfo info = text.textInfo;
            if (info == null || info.linkCount == 0) return -1;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(text.rectTransform, screen, cam, out Vector2 local);
            int best = -1; float bestDistance = float.MaxValue;
            for (int i = 0; i < info.linkCount; i++)
            {
                TMP_LinkInfo link = info.linkInfo[i];
                for (int c = link.linkTextfirstCharacterIndex; c < link.linkTextfirstCharacterIndex + link.linkTextLength && c < info.characterCount; c++)
                {
                    TMP_CharacterInfo ch = info.characterInfo[c];
                    Vector2 middle = (ch.bottomLeft + ch.topRight) * 0.5f;
                    float d = (middle - local).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
            }
            return best;
        }
    }
}
