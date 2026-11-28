using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Adapts the scene's existing pause/options/confirmation UI at runtime.
[DefaultExecutionOrder(-9000)]
[DisallowMultipleComponent]
public sealed class VRPauseMenu : MonoBehaviour
{
    Camera head;
    GameObject panel;
    Canvas surface;
    PauseMenuController pause;
    VRMenuNavigation navigation;
    InputAction menu;
    bool wasPaused;
    bool menuHeld;
    readonly System.Collections.Generic.Dictionary<Material, Material> overlayMaterials = new();

    public void Initialize(Camera view)
    {
        if (menu != null) return;
        head = view;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            pause = root.GetComponentInChildren<PauseMenuController>(true);
            if (pause != null) break;
        }
        if (pause == null || pause.PausePanel == null || pause.ResumeButton == null)
        {
            Debug.LogError("[VR] Assign the existing PauseMenuController's panel and Resume button in this scene.", this);
            enabled = false;
            return;
        }
        panel = pause.PausePanel;
        surface = panel.GetComponentInParent<Canvas>(true);
        if (surface == null)
        {
            Debug.LogError("[VR] The existing pause panel needs a Canvas.", this);
            enabled = false;
            return;
        }
        var scaler = surface.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.enabled = false;
        surface.enabled = true;
        surface.renderMode = RenderMode.WorldSpace;
        surface.worldCamera = head;
        surface.scaleFactor = 1;
        var rect = (RectTransform)surface.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.sizeDelta = new Vector2(1920, 1080);
        rect.localScale = Vector3.one * 0.00065f;
        foreach (var focus in surface.GetComponentsInChildren<MenuPanelFocus>(true))
        {
            focus.StopAllCoroutines();
            focus.enabled = false;
        }
        foreach (var save in surface.GetComponentsInChildren<PauseSaveMenu>(true))
            save.ConfigureUnavailable("VR test progress is not saved. Saving will be added later.");
        // Desktop explicit links include Save Game. Automatic navigation skips
        // that disabled button instead of trapping the stick on it.
        foreach (var button in panel.GetComponentsInChildren<Button>(true))
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic, wrapAround = true };
        navigation = gameObject.AddComponent<VRMenuNavigation>();
        navigation.Scope = surface.transform;
        navigation.enabled = false;
        pause.ConfigurePanel(panel, pause.ResumeButton, false, true);
        pause.enabled = true;
        HidePanels();
        VRMenuUI.ConfigureEvents();
        PrepareControls();
        menu = new InputAction("VR Pause", InputActionType.Button, "<XRController>{LeftHand}/menuButton");
        menu.Enable();
    }

    void Update()
    {
        if (pause == null || menu == null) return;
        if (wasPaused && !PauseMenuController.IsPaused) HidePanels();
        wasPaused = PauseMenuController.IsPaused;
        navigation.enabled = pause.IsShowingOverlay(panel);
        bool held = menu.IsPressed();
        bool pressed = held && !menuHeld;
        menuHeld = held;
        if (!pressed) return;
        if (pause.IsShowingOverlay(panel))
        {
            pause.Resume();
            HidePanels();
            navigation.enabled = false;
        }
        else if (!PauseMenuController.BlocksGameplayInput)
        {
            HidePanels();
            PlaceSurface();
            pause.Pause();
            wasPaused = PauseMenuController.IsPaused;
            navigation.enabled = pause.IsShowingOverlay(panel);
        }
    }

    void PlaceSurface()
    {
        Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        surface.transform.SetPositionAndRotation(head.transform.position + forward * 1.1f,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    void HidePanels()
    {
        foreach (var focus in surface.GetComponentsInChildren<MenuPanelFocus>(true))
            focus.gameObject.SetActive(false);
        panel.SetActive(false);
    }

    void LateUpdate()
    {
        if (navigation != null && navigation.enabled) PrepareControls();
    }

    void PrepareControls()
    {
        foreach (var canvas in surface.GetComponentsInChildren<Canvas>(true))
        {
            canvas.enabled = true;
            canvas.worldCamera = head;
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
        foreach (var control in surface.GetComponentsInChildren<Selectable>(true))
        {
            var pointer = control.GetComponent<VRMenuPointerFocus>();
            if (pointer == null) pointer = control.gameObject.AddComponent<VRMenuPointerFocus>();
            pointer.Navigation = navigation;
        }
        foreach (var graphic in surface.GetComponentsInChildren<Graphic>(true))
        {
            Material original = graphic.material;
            if (original == null || overlayMaterials.ContainsValue(original)) continue;
            if (!overlayMaterials.TryGetValue(original, out var overlay))
            {
                overlay = new Material(original) { name = original.name + " (VR pause overlay)" };
                overlay.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                if (overlay.HasProperty("_ZTestMode"))
                    overlay.SetInt("_ZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                overlayMaterials.Add(original, overlay);
            }
            graphic.material = overlay;
        }
    }

    void OnDestroy()
    {
        menu?.Dispose();
        foreach (var material in overlayMaterials.Values) Destroy(material);
    }
}
