using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// First integration stage: head-relative room movement only. Add to a separate
// root in the copied VR scene, not the PC/mobile scene or the XR rig prefab asset.
[DefaultExecutionOrder(-20000)]
public sealed class VRRoomRig : MonoBehaviour
{
    [SerializeField] PlayerNodeMover player;
    [SerializeField] XROrigin origin;
    [Tooltip("Enable headset-directed scanning while keeping combat and enemy discovery off for this test.")]
    [SerializeField] bool enableLidar = true;

    InputActionAsset movementAsset;
    InputActionReference movementReference;

    void Awake()
    {
        if (player == null || origin == null || origin.Camera == null)
        {
            Debug.LogError("VR Room Rig: assign PlayerNodeMover and the XR Origin with its camera.", this);
            enabled = false;
            return;
        }
        if (origin.transform.IsChildOf(player.transform))
        {
            Debug.LogError("VR Room Rig: keep XR Origin separate from the Player; the bridge follows its room anchor.", this);
            enabled = false;
            return;
        }
        // Set before PlayerNodeMover.Awake resolves its action reference. Keep
        // these bindings separate from the desktop/gameplay action asset.
        movementAsset = ScriptableObject.CreateInstance<InputActionAsset>();
        movementAsset.name = "VR Room Movement (runtime)";
        var map = new InputActionMap("VRRooms");
        movementAsset.AddActionMap(map);
        var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
        move.AddBinding("<XRController>{LeftHand}/primary2DAxis");
        movementReference = InputActionReference.Create(move);
        player.moveActionReference = movementReference;
        player.cameraTransform = origin.Camera.transform;
        player.instantTransitions = true;
        move.Enable();

        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        // The starter rig's locomotion includes free movement, jumping,
        // teleportation and turning. None should move this test's rig.
        var locomotion = origin.transform.Find("Locomotion");
        if (locomotion != null) locomotion.gameObject.SetActive(false);
        foreach (var provider in origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true))
            provider.enabled = false;
        DisableOtherSystemsForMovementTest();
        if (enableLidar)
        {
            int scanners = 0;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var scanner in root.GetComponentsInChildren<LiDARScanner>(true))
                {
                    scanner.ConfigureView(origin.Camera, player, false);
                    scanner.enabled = true;
                    scanners++;
                }
            if (scanners == 0) Debug.LogWarning("[VR] No LiDARScanner found in the VR scene.", this);
        }
        origin.Camera.clearFlags = CameraClearFlags.SolidColor;
        origin.Camera.backgroundColor = Color.black;
        Debug.Log("[VR] Left stick changes rooms; headset controls looking. LiDAR " + (enableLidar ? "enabled" : "disabled") + "; combat and menus remain off.", this);
    }

    void DisableOtherSystemsForMovementTest()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script is CombatController || script is RewardController || script is CardSystem
                    || script is SaveGameSession || script is PauseMenuController || script is BestiaryController
                    || script is LiDARScanner || script is CameraController)
                    script.enabled = false;
            foreach (var view in root.GetComponentsInChildren<Camera>(true))
                if (view != origin.Camera) view.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = listener.gameObject == origin.Camera.gameObject;
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
        }
        // Presence sounds belong to the later encounter integration stage.
        SceneManager.sceneLoaded += SilenceEnemies;
    }

    void SilenceEnemies(Scene scene, LoadSceneMode mode)
    {
        if (scene != gameObject.scene) return;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var enemy in root.GetComponentsInChildren<Enemy>(true))
            {
                enemy.enabled = false;
                foreach (var source in enemy.GetComponentsInChildren<AudioSource>(true))
                {
                    source.Stop();
                    source.enabled = false;
                }
            }
    }

    void LateUpdate()
    {
        if (player == null || origin == null || player.CurrentRoom == null) return;
        // PlayerNodeMover's height offset is an eye-height convention for the
        // desktop camera. XR Origin sits at floor level and tracks real height.
        origin.transform.position = player.transform.position - Vector3.up * player.heightOffset;
        // Never overwrite the tracked camera's position or rotation.
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SilenceEnemies;
        if (movementAsset != null) movementAsset.Disable();
        if (movementReference != null) Destroy(movementReference);
        if (movementAsset != null) Destroy(movementAsset);
    }
}
