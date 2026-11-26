using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.InputSystem.XR;

// Only an explicit VR scene creates this service. Regular launches are untouched.
public sealed class VRRuntimeSession : MonoBehaviour
{
    static VRRuntimeSession instance;
    XROrigin origin;
    XRManagerSettings manager;
    bool initializing, ownsLoader;

    public static VRRuntimeSession GetOrCreate()
    {
        if (instance == null)
        {
            instance = new GameObject("VR Runtime Session").AddComponent<VRRuntimeSession>();
            DontDestroyOnLoad(instance.gameObject);
            SceneManager.sceneLoaded += instance.SceneLoaded;
        }
        return instance;
    }

    public void Adopt(XROrigin rig)
    {
        if (origin != null && origin != rig)
        {
            origin.gameObject.SetActive(false);
            Destroy(origin.gameObject);
        }
        origin = rig;
        origin.transform.SetParent(transform, true);
        // Interactors must not keep a manager belonging to the departing scene.
        var interactions = GetComponent<XRInteractionManager>();
        if (interactions == null) interactions = gameObject.AddComponent<XRInteractionManager>();
        foreach (var interactor in origin.GetComponentsInChildren<XRBaseInteractor>(true))
            interactor.interactionManager = interactions;
    }

    public XROrigin ReuseRig(XROrigin sceneRig)
    {
        if (origin == null || origin == sceneRig) return sceneRig;
        // Keep the tracked objects, ray origins and input callbacks together.
        // Only discard the extra prefab supplied by the new gameplay scene.
        sceneRig.gameObject.SetActive(false);
        Destroy(sceneRig.gameObject);
        return origin;
    }

    public void StartXR()
    {
        if (!initializing && !ownsLoader) StartCoroutine(InitializeXR());
    }

    IEnumerator InitializeXR()
    {
        initializing = true;
        yield return null;
#if UNITY_ANDROID && !UNITY_EDITOR
        manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager == null || manager.activeLoaders.Count == 0)
            Debug.LogError("[VR] Enable Android OpenXR in XR Plug-in Management.", this);
        else if (manager.activeLoader == null)
        {
            yield return manager.InitializeLoader();
            if (manager.activeLoader != null)
            {
                manager.StartSubsystems();
                ownsLoader = true;
                Debug.Log("[VR] XR started: " + manager.activeLoader.name, this);
            }
            else Debug.LogError("[VR] XR initialization failed. Check OpenXR Project Validation.", this);
        }
#else
        Debug.Log("[VR] Test tracking in the standalone Quest build.", this);
#endif
        initializing = false;
    }

    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(RestoreRigInput(scene));
        if (scene.name == "Main Menu" && origin != null) StartCoroutine(ShowMenu(scene));
    }

    IEnumerator RestoreRigInput(Scene scene)
    {
        // Both rigs reference the starter input asset. Departing-rig OnDisable
        // can run after the new rig's OnEnable and disable its tracking actions.
        yield return null;
        if (SceneManager.GetActiveScene() != scene || origin == null) yield break;
        foreach (var input in origin.GetComponentsInChildren<InputActionManager>(true))
            if (input.isActiveAndEnabled) input.EnableInput();
        // Rebind after scene teardown has finished and read the current pose
        // again, even if a controller stayed stationary during the transition.
        foreach (var pose in origin.GetComponentsInChildren<TrackedPoseDriver>(true))
            if (pose.isActiveAndEnabled)
            {
                pose.enabled = false;
                pose.enabled = true;
            }
        foreach (var interactor in origin.GetComponentsInChildren<XRBaseInteractor>(true))
            if (interactor.isActiveAndEnabled)
            {
                interactor.enabled = false;
                interactor.enabled = true;
            }
        Debug.Log("[VR] Rig ready in " + scene.name + "; tracking=" + origin.CurrentTrackingOriginMode
            + "; local head height=" + origin.CameraInOriginSpaceHeight.ToString("F2"), this);
    }

    IEnumerator ShowMenu(Scene scene)
    {
        Time.timeScale = 1;
        var adapter = new GameObject("VR Main Menu Adapter").AddComponent<VRMainMenuAdapter>();
        adapter.Initialize(scene, origin.Camera);
        yield return null;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
        if (!ownsLoader || manager == null || manager.activeLoader == null) return;
        manager.StopSubsystems();
        manager.DeinitializeLoader();
    }
}
