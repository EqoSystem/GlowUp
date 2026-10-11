using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Makes a GridLayoutGroup's cells fill the space it has: the cell size is worked out from the
    /// grid's own size, the number of columns and rows and the spacing. Tiles therefore fit on short
    /// and tall phones alike. Limits are Inspector fields. Works in the Editor too.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(GridLayoutGroup))]
    public class GridFitCells : MonoBehaviour
    {
        [SerializeField] private int columns = 2;
        [SerializeField] private int rows = 3;
        [SerializeField] private float minCellHeight = 200f;
        [SerializeField] private float maxCellHeight = 480f;

        GridLayoutGroup grid;

        void OnEnable() { Fit(); }
        void OnRectTransformDimensionsChange() { Fit(); }
#if UNITY_EDITOR
        void Update() { if (!Application.isPlaying) Fit(); }
#endif

        void Fit()
        {
            if (grid == null) grid = GetComponent<GridLayoutGroup>();
            if (grid == null || columns < 1 || rows < 1) return;
            var r = ((RectTransform)transform).rect;
            if (r.width <= 1f || r.height <= 1f) return;
            float w = (r.width - grid.padding.horizontal - grid.spacing.x * (columns - 1)) / columns;
            float h = (r.height - grid.padding.vertical - grid.spacing.y * (rows - 1)) / rows;
            var size = new Vector2(Mathf.Max(10f, w), Mathf.Clamp(h, minCellHeight, maxCellHeight));
            if ((grid.cellSize - size).sqrMagnitude > 0.01f) grid.cellSize = size;
        }
    }
}
