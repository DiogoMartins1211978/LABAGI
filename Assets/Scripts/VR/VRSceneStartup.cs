using UnityEngine;

// Scene entry point; the session owns XR across the VR test/menu transition.
[DisallowMultipleComponent]
public sealed class VRSceneStartup : MonoBehaviour
{
    void Start() => VRRuntimeSession.GetOrCreate().StartXR();
}
