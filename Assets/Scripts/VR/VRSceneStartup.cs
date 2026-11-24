using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

// Add only to VR scenes. Keep XR Plug-in Management's automatic startup disabled.
[DisallowMultipleComponent]
public sealed class VRSceneStartup : MonoBehaviour
{
    XRManagerSettings manager = null;
    bool ownsLoader;

    IEnumerator Start()
    {
        // XR manual initialization must wait until scene Start callbacks finish.
        yield return null;
#if UNITY_ANDROID && !UNITY_EDITOR
        manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager == null || manager.activeLoaders.Count == 0)
        {
            Debug.LogError("[VR] Enable OpenXR under XR Plug-in Management's Android tab. Keep Initialize XR on Startup unchecked.", this);
            yield break;
        }
        if (manager.activeLoader != null)
        {
            Debug.LogWarning("[VR] XR is already initialized. This scene will not take ownership of it.", this);
            yield break;
        }
        ownsLoader = true;
        yield return manager.InitializeLoader();
        if (manager.activeLoader == null)
        {
            ownsLoader = false;
            Debug.LogError("[VR] XR initialization failed. Check the Quest Android OpenXR settings and Project Validation.", this);
            yield break;
        }
        manager.StartSubsystems();
        Debug.Log("[VR] XR started: " + manager.activeLoader.name, this);
#else
        Debug.Log("[VR] This startup component runs in the Android headset build. Editor Play mode does not test standalone Quest tracking.", this);
#endif
    }

    void OnDestroy()
    {
        if (!ownsLoader || manager == null || manager.activeLoader == null) return;
        manager.StopSubsystems();
        manager.DeinitializeLoader();
        ownsLoader = false;
    }
}
