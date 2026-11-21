using UnityEngine;

public class BookScaleReference : MonoBehaviour
{
    [SerializeField] CharacterController playerCapsule;
    [SerializeField] Transform bookRoot;
    [SerializeField] Camera previewCamera;
    [SerializeField, Min(.01f)] float heightMetres = .2794f;
    public float PlayerHeight => playerCapsule != null ? playerCapsule.height * Mathf.Abs(playerCapsule.transform.lossyScale.y) : 0;

    [ContextMenu("Apply physical book height")]
    public void Apply()
    {
        if (bookRoot == null || previewCamera == null) return;
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (var renderer in bookRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.name.StartsWith("Page_")) continue;
            var mesh = new Mesh();
            renderer.BakeMesh(mesh);
            foreach (var vertex in mesh.vertices)
            {
                float z = bookRoot.InverseTransformPoint(renderer.transform.TransformPoint(vertex)).z;
                min = Mathf.Min(min, z); max = Mathf.Max(max, z);
            }
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
        if (max <= min) return;
        var cameraOffset = bookRoot.InverseTransformPoint(previewCamera.transform.position);
        float scale = heightMetres / (max - min);
        float ratio = scale / bookRoot.localScale.x;
        bookRoot.localScale = Vector3.one * scale;
        previewCamera.transform.position = bookRoot.TransformPoint(cameraOffset);
        previewCamera.orthographicSize *= ratio;
        previewCamera.nearClipPlane *= ratio;
        previewCamera.farClipPlane *= ratio;
    }
}
