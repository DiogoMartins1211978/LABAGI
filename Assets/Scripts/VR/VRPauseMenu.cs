using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-9000)]
public sealed class VRPauseMenu : MonoBehaviour
{
    Camera head;
    GameObject panel;
    PauseMenuController pause;
    InputAction menu;

    public void Initialize(Camera view)
    {
        head = view;
        panel = VRMenuUI.Create(transform, head, "Paused");
        pause = gameObject.AddComponent<PauseMenuController>();
        var resume = VRMenuUI.Button(panel.transform, "Resume", 65, () => pause.Resume());
        VRMenuUI.Button(panel.transform, "Return to Main Menu", -65, () => pause.ReturnToMainMenu());
        VRMenuUI.Label(panel.transform, "VR test progress is not saved.\nAim at a button and press the trigger.",
            new Vector2(0, -220), new Vector2(820, 100), 27);
        pause.ConfigurePanel(panel, resume, false);
        panel.SetActive(false);
        menu = new InputAction("VR Pause", InputActionType.Button, "<XRController>{LeftHand}/menuButton");
        menu.Enable();
    }

    void Update()
    {
        if (pause == null || menu == null) return;
        if (!panel.activeSelf && !PauseMenuController.IsPaused) VRMenuUI.Place(panel, head);
        if (!menu.WasPressedThisFrame()) return;
        if (pause.IsShowingOverlay(panel)) pause.Resume();
        else if (!PauseMenuController.BlocksGameplayInput) pause.Pause();
    }

    void OnDestroy() => menu?.Dispose();
}
