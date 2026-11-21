using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Maps the book's displayed RenderTexture back into its physical page Canvas.
public class BookPageRaycaster : UnityEngine.UI.GraphicRaycaster
{
    [SerializeField] UnityEngine.UI.RawImage bookDisplay;
    // The display's overlay must not intercept controls rendered inside it.
    public override int sortOrderPriority => bookDisplay != null ? bookDisplay.canvas.rootCanvas.sortingOrder + 1 : base.sortOrderPriority;
    public override int renderOrderPriority => bookDisplay != null ? bookDisplay.canvas.rootCanvas.renderOrder + 1 : base.renderOrderPriority;

    public bool MapScreenPoint(Vector2 screen, out Vector2 mapped)
    {
        mapped = screen;
        if (bookDisplay == null || eventCamera == null) return false;
        var canvas = bookDisplay.canvas.rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(bookDisplay.rectTransform, screen, camera, out var local)) return false;
        var rect = bookDisplay.rectTransform.rect;
        mapped = new Vector2((local.x - rect.xMin) / rect.width * eventCamera.pixelWidth,
            (local.y - rect.yMin) / rect.height * eventCamera.pixelHeight);
        return true;
    }
    public override void Raycast(PointerEventData data, List<RaycastResult> results)
    {
        if (bookDisplay == null || !bookDisplay.gameObject.activeInHierarchy) return;
        if (!MapScreenPoint(data.position, out var mapped)) return;
        if (mapped.x < 0 || mapped.y < 0 || mapped.x > eventCamera.pixelWidth || mapped.y > eventCamera.pixelHeight) return;
        Vector2 original = data.position;
        int first = results.Count;
        data.position = mapped;
        try
        {
            base.Raycast(data, results);
            for (int i = first; i < results.Count; i++)
            {
                var hit = results[i];
                hit.screenPosition = original;
                results[i] = hit;
            }
        }
        finally { data.position = original; }
    }
}
