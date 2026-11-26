using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Added only when the persistent VR session enters the shared Main Menu scene.
// All layout and button callbacks still come from the original menu.
public sealed class VRMainMenuAdapter : MonoBehaviour
{
    Camera head;
    bool launching;
    VRMenuNavigation navigation;

    public void Initialize(Scene scene, Camera view)
    {
        head = view;
        navigation = gameObject.AddComponent<VRMenuNavigation>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var focus in root.GetComponentsInChildren<MenuPanelFocus>(true))
            {
                focus.StopAllCoroutines();
                focus.enabled = false;
            }
            foreach (var menu in root.GetComponentsInChildren<SinglePlayerMenu>(true))
                menu.ConfigureUnsavedPreview(LaunchTest);
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas) continue;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = head;
                canvas.scaleFactor = 1;
                var rect = (RectTransform)canvas.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(1920, 1080);
                rect.localScale = Vector3.one * 0.001f;
                var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                rect.SetPositionAndRotation(head.transform.position + forward * 2,
                    Quaternion.LookRotation(forward, Vector3.up));
            }
        }
        VRMenuUI.ConfigureEvents();
        PrepareControls();
    }

    void LateUpdate() => PrepareControls();

    void PrepareControls()
    {
        if (head == null) return;
        // Also includes runtime dropdown canvases and controls whose navigation
        // is regenerated when the slot panel changes selection.
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.worldCamera = head;
                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                var pointer = selectable.GetComponent<VRMenuPointerFocus>();
                if (pointer == null) pointer = selectable.gameObject.AddComponent<VRMenuPointerFocus>();
                pointer.Navigation = navigation;
            }
        }
    }

    void LaunchTest()
    {
        if (launching) return;
        if (!Application.CanStreamedLevelBeLoaded("VR_Test"))
        {
            Debug.LogError("[VR] Add VR_Test to the Quest build scene list.", this);
            return;
        }
        launching = true;
        Time.timeScale = 1;
        SceneManager.LoadSceneAsync("VR_Test");
    }
}
