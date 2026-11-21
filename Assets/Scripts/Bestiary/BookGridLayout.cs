using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways, RequireComponent(typeof(GridLayoutGroup))]
public class BookGridLayout : MonoBehaviour
{
    [SerializeField, Range(1, 4)] int columns = 3;
    [SerializeField, Range(1, 2)] int rows = 2;
    [SerializeField] Vector2 spacing = new Vector2(12, 12);
    [SerializeField] float aspect = 5f / 7;
    public int Capacity => Mathf.Clamp(columns, 1, 4) * Mathf.Clamp(rows, 1, 2);
    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void OnRectTransformDimensionsChange() => Apply();
    public void Apply()
    {
        columns = Mathf.Clamp(columns, 1, 4);
        rows = Mathf.Clamp(rows, 1, 2);
        var grid = GetComponent<GridLayoutGroup>();
        if (grid == null) return;
        var size = ((RectTransform)transform).rect.size;
        float height = Mathf.Max(1, Mathf.Min((size.y - spacing.y * (rows - 1)) / rows,
            (size.x - spacing.x * (columns - 1)) / columns / aspect));
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.spacing = spacing;
        grid.cellSize = new Vector2(height * aspect, height);
    }
}
