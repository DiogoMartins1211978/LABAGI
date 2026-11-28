using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// First integration stage: head-relative room movement only. Add to a separate
// root in the copied VR scene, not the PC/mobile scene or the XR rig prefab asset.
[DefaultExecutionOrder(-20000)]
[DisallowMultipleComponent]
public sealed class VRRoomRig : MonoBehaviour
{
    static VRRoomRig activeBridge;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBridge() => activeBridge = null;
    [SerializeField] PlayerNodeMover player;
    [SerializeField] XROrigin origin;
    [Tooltip("Enable headset-directed scanning while keeping combat and enemy discovery off for this test.")]
    [SerializeField] bool enableLidar = true;
    [Tooltip("Enable tracked-head combat and right-controller card controls. Other menus and rewards stay off.")]
    [SerializeField] bool enableCombat = true;
    [Tooltip("Hide the legacy player body mesh so it cannot mask LiDAR points around the tracked headset. Colliders stay enabled.")]
    [SerializeField] bool hideDesktopBody = true;

    InputActionAsset movementAsset;
    InputActionReference movementReference;

    void Awake()
    {
        if (activeBridge != null && activeBridge != this && activeBridge.gameObject.scene == gameObject.scene)
        {
            Debug.LogWarning("[VR] Ignored a duplicate VRRoomRig in this scene.", this);
            enabled = false;
            return;
        }
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
        activeBridge = this;
        var sceneOrigin = origin;
        origin = VRRuntimeSession.GetOrCreate().ReuseRig(origin);
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

        // Requesting Floor again invokes subsystem initialization/recentering.
        // A reused, already-tracking rig must keep its current reference space.
        if (origin == sceneOrigin)
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        // This game moves between room anchors, not via XR capsule locomotion.
        // Its unused capsule must not intercept the scanner's physics rays.
        foreach (var capsule in origin.GetComponentsInChildren<CharacterController>(true))
            capsule.enabled = false;
        if (hideDesktopBody)
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    renderer.enabled = false;
        // The starter rig's locomotion includes free movement, jumping,
        // teleportation and turning. None should move this test's rig.
        var locomotion = origin.transform.Find("Locomotion");
        if (locomotion != null) locomotion.gameObject.SetActive(false);
        foreach (var provider in origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true))
            provider.enabled = false;
        DisableOtherSystemsForMovementTest();
        if (enableCombat) ConfigureCombat();
        if (enableLidar)
        {
            int scanners = 0;
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var scanner in root.GetComponentsInChildren<LiDARScanner>(true))
                {
                    scanner.ConfigureView(origin.Camera, player, enableCombat);
                    scanner.enabled = true;
                    scanners++;
                }
            if (scanners == 0) Debug.LogWarning("[VR] No LiDARScanner found in the VR scene.", this);
        }
        origin.Camera.clearFlags = CameraClearFlags.SolidColor;
        origin.Camera.backgroundColor = Color.black;
        VRRuntimeSession.GetOrCreate().Adopt(origin);
        gameObject.AddComponent<VRPauseMenu>().Initialize(origin.Camera);
        gameObject.AddComponent<VRBestiary>().Initialize(origin.Camera);
        Debug.Log("[VR] Left stick changes rooms; headset controls looking. LiDAR " + (enableLidar ? "enabled" : "disabled")
            + "; combat " + (enableCombat ? "enabled" : "disabled") + "; left Menu opens pause. Saving and rewards remain off.", this);
    }

    void ConfigureCombat()
    {
        CombatController combat = null;
        CardSystem cards = null;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (combat == null) combat = root.GetComponentInChildren<CombatController>(true);
            if (cards == null) cards = root.GetComponentInChildren<CardSystem>(true);
            foreach (var hand in root.GetComponentsInChildren<HandView>(true)) hand.enabled = false;
        }
        if (combat == null || cards == null)
        {
            Debug.LogError("[VR] The VR game scene needs CombatController and CardSystem for combat.", this);
            enableCombat = false;
            return;
        }
        var health = player.GetComponent<PlayerHealth>();
        if (health == null) health = player.gameObject.AddComponent<PlayerHealth>();
        cards.ConfigureExternalControls(health);
        combat.ConfigureTrackedHeadset(player, origin.Camera.transform, cards, health);
        cards.enabled = combat.enabled = true;
        var controls = GetComponent<VRCombatCards>();
        if (controls == null) controls = gameObject.AddComponent<VRCombatCards>();
        controls.Initialize(cards, combat, health, origin.Camera);
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
        if (enableCombat) return;
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
        if (activeBridge == this) activeBridge = null;
        SceneManager.sceneLoaded -= SilenceEnemies;
        if (movementAsset != null) movementAsset.Disable();
        if (movementReference != null) Destroy(movementReference);
        if (movementAsset != null) Destroy(movementAsset);
    }
}
