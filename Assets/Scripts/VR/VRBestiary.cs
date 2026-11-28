using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Reuses the physical book and its existing page controls. The desktop
// RenderTexture display and its screen-coordinate raycasters stay off in VR.
[DisallowMultipleComponent, DefaultExecutionOrder(-8500)]
public sealed class VRBestiary : MonoBehaviour
{
    BestiaryController book;
    Camera head;
    Transform presentation;
    VRMenuNavigation navigation;
    InputAction toggle;
    Quaternion facingOffset;
    Vector3 originalScale;
    Vector3 localCentre;
    float sizeMultiplier = 1;
    bool held;

    public void Initialize(Camera view)
    {
        head = view;
        PauseMenuController pause = null;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (book == null) book = root.GetComponentInChildren<BestiaryController>(true);
            if (pause == null) pause = root.GetComponentInChildren<PauseMenuController>(true);
        }
        if (book == null || pause == null || book.Panel == null || book.BookView == null
            || book.BookView.Root == null || book.PreviewCamera == null)
        {
            Debug.LogError("[VR] The scene needs its existing BestiaryController, book view and preview camera references.", this);
            enabled = false;
            return;
        }
        presentation = book.BookView.Root;
        book.BookView.ConfigureTrackedView(head);
        originalScale = presentation.localScale;
        facingOffset = Quaternion.Inverse(book.PreviewCamera.transform.rotation) * presentation.rotation;
        // Fit from the working desktop camera's framing, not the animated
        // bounds of individual leaves. Preserve the model's authored pose.
        float visibleWidth = book.PreviewCamera.orthographicSize * 2 * book.PreviewCamera.aspect;
        if (visibleWidth > 0.01f) sizeMultiplier = Mathf.Clamp(1.1f / visibleWidth, 0.1f, 10f);
        Vector3 centre = book.PreviewCamera.transform.position
            + book.PreviewCamera.transform.forward * Vector3.Dot(presentation.position - book.PreviewCamera.transform.position,
                book.PreviewCamera.transform.forward);
        localCentre = presentation.InverseTransformPoint(centre);
        var overlay = book.Panel.GetComponentInParent<Canvas>(true);
        if (overlay == null) { enabled = false; Debug.LogError("[VR] Bestiary panel requires its overlay Canvas.", this); return; }
        overlay.enabled = true;
        // This Canvas owns pause state only; the visible pages are on the book.
        foreach (var display in book.Panel.GetComponentsInChildren<Graphic>(true)) display.enabled = false;
        foreach (var focus in overlay.GetComponentsInChildren<MenuPanelFocus>(true))
        {
            focus.StopAllCoroutines(); focus.enabled = false;
        }
        book.ConfigureVR(pause);
        book.enabled = true;
        navigation = gameObject.AddComponent<VRMenuNavigation>();
        navigation.Scope = presentation;
        navigation.BackHandler = () => book.Close();
        navigation.enabled = false;
        PreparePages();
        toggle = new InputAction("VR Bestiary", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
        toggle.Enable();
        Debug.Log("[VR] Y opens the book; rays/joysticks browse, A/X confirms, B/Y closes. Deck editing is available outside combat.", this);
    }

    void Update()
    {
        if (toggle == null || book == null) return;
        bool down = toggle.IsPressed();
        bool pressed = down && !held;
        held = down;
        if (!pressed) return;
        if (book.IsOpen)
        {
            if (!book.BookView.IsTransitioning) book.Close();
        }
        else if (!PauseMenuController.BlocksGameplayInput)
        {
            PlaceBook();
            book.OpenBestiary();
        }
    }

    void LateUpdate()
    {
        if (navigation == null || book == null) return;
        // Avoid treating the opening Y press as the navigation Back command.
        navigation.enabled = book.IsOpen && !book.BookView.IsTransitioning;
        if (book.IsOpen) PreparePages();
    }

    void PlaceBook()
    {
        Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        presentation.localScale = originalScale * sizeMultiplier;
        presentation.rotation = Quaternion.LookRotation(forward, Vector3.up) * facingOffset;
        Vector3 desiredCentre = head.transform.position + forward * 1.05f - Vector3.up * 0.12f;
        presentation.position += desiredCentre - presentation.TransformPoint(localCentre);
        PreparePages();
    }

    void PreparePages()
    {
        foreach (var canvas in presentation.GetComponentsInChildren<Canvas>(true))
        {
            canvas.enabled = true;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            foreach (var desktop in canvas.GetComponents<BookPageRaycaster>()) desktop.enabled = false;
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            var layer = canvas.gameObject.layer;
            head.cullingMask |= 1 << layer;
        }
        foreach (var renderer in presentation.GetComponentsInChildren<Renderer>(true))
            head.cullingMask |= 1 << renderer.gameObject.layer;
        foreach (var control in presentation.GetComponentsInChildren<Selectable>(true))
        {
            var pointer = control.GetComponent<VRMenuPointerFocus>();
            if (pointer == null) pointer = control.gameObject.AddComponent<VRMenuPointerFocus>();
            pointer.Navigation = navigation;
        }
    }

    void OnDestroy() => toggle?.Dispose();
}
