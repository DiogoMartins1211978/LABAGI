using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Runtime prototype UI; no changes to the desktop menu assets.
public static class VRMenuUI
{
    public static GameObject Create(Transform parent, Camera head, string title)
    {
        ConfigureEvents();
        var root = new GameObject(title + " Canvas", typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
        root.transform.SetParent(parent, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head;
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 650);
        root.transform.localScale = Vector3.one * 0.0015f;
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.045f, 0.97f);
        Label(panel.transform, title, new Vector2(0, 225), new Vector2(850, 85), 48);
        return panel;
    }

    public static void ConfigureEvents()
    {
        var events = EventSystem.current;
        if (events == null) events = new GameObject("VR Event System", typeof(EventSystem)).GetComponent<EventSystem>();
        foreach (var module in events.GetComponents<BaseInputModule>()) module.enabled = false;
        var xr = events.GetComponent<XRUIInputModule>();
        if (xr == null) xr = events.gameObject.AddComponent<XRUIInputModule>();
        xr.enabled = true;
        events.SetSelectedGameObject(null);
    }

    public static TMP_Text Label(Transform parent, string text, Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size; rect.anchoredPosition = position;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = fontSize; tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
        return tmp;
    }

    public static Button Button(Transform panel, string text, float y, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(680, 95); rect.anchoredPosition = new Vector2(0, y);
        go.GetComponent<Image>().color = Color.white;
        var button = go.GetComponent<Button>();
        // Ray hover supplies feedback; pointer clicks must not leave a second
        // button selected as if keyboard navigation were still focused there.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = go.GetComponent<Image>();
        var colors = button.colors;
        colors.normalColor = new Color(0.12f, 0.18f, 0.22f);
        colors.highlightedColor = new Color(0.12f, 0.55f, 0.52f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.08f, 0.35f, 0.32f);
        button.colors = colors;
        button.onClick.AddListener(action);
        Label(go.transform, text, Vector2.zero, rect.sizeDelta, 34);
        return button;
    }

    public static void Place(GameObject panel, Camera head)
    {
        var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        panel.transform.parent.SetPositionAndRotation(head.transform.position + forward * 1.6f,
            Quaternion.LookRotation(forward, Vector3.up));
    }
}
