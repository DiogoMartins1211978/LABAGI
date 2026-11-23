using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Attach to a RawImage beside the existing portrait. Only mesh components are
// copied: the gameplay prefab is never instantiated, so its Awake cannot run.
[RequireComponent(typeof(RawImage))]
public sealed class BestiaryModelPreview : MonoBehaviour
{
    [SerializeField] Material previewMaterial;
    [SerializeField] string previewLayer = "EnemyModelPreview";
    [SerializeField, Range(128, 1024)] int textureSize = 512;
    [SerializeField] Vector3 cameraAngles = new Vector3(12f, 155f, 0f);
    [Header("LiDAR appearance (preview only)")]
    [SerializeField] bool usePointCloud = true;
    [SerializeField] Material pointMaterial;
    [SerializeField, Range(100, 50000)] int pointCount = 10000;
    [SerializeField, Range(0.5f, 8f)] float pointSize = 2f;

    RawImage output;
    GameObject stage;
    RenderTexture texture;
    BestiaryEntry current;
    Mesh pointMesh;
    Material runtimeDepthMaterial;
    Material runtimePointMaterial;

    public bool Show(BestiaryEntry entry)
    {
        if (output == null) output = GetComponent<RawImage>();
        output.raycastTarget = false;
        if (entry == null || entry.modelPrefab == null) { Hide(); return false; }
        if (current == entry && stage != null) return true;
        Hide();
        int layer = LayerMask.NameToLayer(previewLayer);
        if (layer < 0 || previewMaterial == null)
        {
            Debug.LogWarning("Bestiary model preview: assign a Preview Material and create the " + previewLayer + " layer. Using the portrait instead.", this);
            return false;
        }

        stage = new GameObject("Bestiary Model Preview (runtime)");
        stage.layer = layer;
        stage.transform.position = new Vector3(0f, -10000f, 0f);
        var map = new Dictionary<Transform, Transform>();
        CopyHierarchy(entry.modelPrefab.transform, stage.transform, layer, map);
        var model = map[entry.modelPrefab.transform];
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.Euler(entry.previewEulerAngles);

        var renderers = new List<Renderer>();
        foreach (var source in entry.modelPrefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!source.enabled || !VisibleInPrefab(source.transform, entry.modelPrefab.transform)) continue;
            Renderer target = null;
            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                mesh = skin.sharedMesh;
                var clone = map[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                clone.sharedMesh = mesh;
                var bones = new Transform[skin.bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    if (skin.bones[i] != null) map.TryGetValue(skin.bones[i], out bones[i]);
                clone.bones = bones;
                if (skin.rootBone != null && map.TryGetValue(skin.rootBone, out var rootBone)) clone.rootBone = rootBone;
                clone.localBounds = skin.localBounds;
                clone.updateWhenOffscreen = true;
                for (int i = 0; i < mesh.blendShapeCount; i++) clone.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                target = clone;
            }
            else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                map[source.transform].gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                target = map[source.transform].gameObject.AddComponent<MeshRenderer>();
            }
            if (target == null) continue;
            var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = previewMaterial;
            target.sharedMaterials = materials;
            target.shadowCastingMode = ShadowCastingMode.Off;
            target.receiveShadows = false;
            renderers.Add(target);
        }
        if (renderers.Count == 0)
        {
            Debug.LogWarning("Bestiary model preview: no supported visible meshes in " + entry.modelPrefab.name, this);
            Hide();
            return false;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        if (usePointCloud && pointMaterial != null) AddPointCloud(renderers, bounds, layer);
        float aspect = output.rectTransform.rect.height > 0f
            ? Mathf.Max(0.1f, output.rectTransform.rect.width / output.rectTransform.rect.height) : 1f;
        texture = new RenderTexture(textureSize, Mathf.Clamp(Mathf.RoundToInt(textureSize / aspect), 128, 2048), 24, RenderTextureFormat.ARGB32);
        texture.name = "Bestiary Enemy Model";
        texture.Create();
        var cameraObject = new GameObject("Enemy Model Camera");
        cameraObject.transform.SetParent(stage.transform, false);
        var view = cameraObject.AddComponent<Camera>();
        view.cullingMask = 1 << layer;
        view.targetTexture = texture;
        view.depth = -100f; // Render before the book's preview camera samples this texture.
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = Color.clear;
        view.allowHDR = view.allowMSAA = false;
        view.stereoTargetEye = StereoTargetEyeMask.None;
        view.orthographic = true;
        view.aspect = (float)texture.width / texture.height;
        Quaternion rotation = Quaternion.Euler(cameraAngles);
        float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
        float distance = radius * 3f + 1f;
        view.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
        float halfHeight = 0.01f;
        for (int i = 0; i < 8; i++)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var projected = Quaternion.Inverse(rotation) * (corner - bounds.center);
            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(projected.y), Mathf.Abs(projected.x) / view.aspect);
        }
        view.orthographicSize = halfHeight * Mathf.Max(1f, entry.previewPadding);
        view.nearClipPlane = 0.01f;
        view.farClipPlane = distance + radius * 3f + 1f;
        output.texture = texture;
        output.color = Color.white;
        output.enabled = true;
        current = entry;
        return true;
    }

    static bool VisibleInPrefab(Transform node, Transform root)
    {
        for (var cursor = node; cursor != null && cursor != root; cursor = cursor.parent)
            if (!cursor.gameObject.activeSelf) return false;
        return true;
    }

    void AddPointCloud(List<Renderer> renderers, Bounds bounds, int layer)
    {
        if (!previewMaterial.HasProperty("_DepthOnly"))
        {
            Debug.LogWarning("Use the LABAGI/Bestiary Model Preview shader for the preview material's depth mask.", this);
            return;
        }
        try
        {
            pointMesh = BestiaryPointCloud.Build(renderers, stage.transform, pointCount, Mathf.Max(0.0001f, bounds.size.magnitude * 0.001f));
            runtimeDepthMaterial = new Material(previewMaterial);
            runtimeDepthMaterial.SetFloat("_DepthOnly", 1f);
            runtimePointMaterial = new Material(pointMaterial);
            runtimePointMaterial.SetFloat("_PointSize", pointSize);
            var cloud = new GameObject("LiDAR Preview Points");
            cloud.layer = layer;
            cloud.transform.SetParent(stage.transform, false);
            cloud.AddComponent<MeshFilter>().sharedMesh = pointMesh;
            var renderer = cloud.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = runtimePointMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            foreach (var surface in renderers)
            {
                var materials = surface.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = runtimeDepthMaterial;
                surface.sharedMaterials = materials;
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Bestiary point preview: " + exception.Message + " Showing the solid model instead.", this);
        }
    }

    static void CopyHierarchy(Transform source, Transform parent, int layer, Dictionary<Transform, Transform> map)
    {
        var copy = new GameObject(source.name);
        copy.layer = layer;
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;
        map.Add(source, copy.transform);
        foreach (Transform child in source) CopyHierarchy(child, copy.transform, layer, map);
    }

    public void Hide()
    {
        if (output == null) output = GetComponent<RawImage>();
        output.texture = null;
        output.enabled = false;
        current = null;
        if (stage != null) { stage.SetActive(false); Destroy(stage); stage = null; }
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
        if (pointMesh != null) { Destroy(pointMesh); pointMesh = null; }
        if (runtimeDepthMaterial != null) { Destroy(runtimeDepthMaterial); runtimeDepthMaterial = null; }
        if (runtimePointMaterial != null) { Destroy(runtimePointMaterial); runtimePointMaterial = null; }
    }

    void OnEnable() { if (current == null) Hide(); }
    void OnDisable() => Hide();
}
