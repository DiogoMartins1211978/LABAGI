using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Editor-only: render scratch copies in a preview scene, never alter the source prefab.
public class BestiaryArtworkWindow : EditorWindow
{
    [SerializeField] GameObject enemyPrefab;
    [SerializeField] BestiaryEntry entry;
    [SerializeField] float yaw = 25f;
    [SerializeField] float pitch = 15f;
    [SerializeField] float framing = 1.15f;
    Texture2D portrait;
    Texture2D silhouette;
    string status;
    const int Resolution = 512;
    const string OutputFolder = "Assets/Bestiary/Artwork";

    [MenuItem("Tools/Bestiary/Capture Enemy Artwork")]
    static void Open()
    {
        var window = GetWindow<BestiaryArtworkWindow>("Bestiary Artwork");
        window.minSize = new Vector2(580f, 480f);
        if (window.enemyPrefab == null)
            window.enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/LowPolySnake1.prefab");
        if (window.entry == null)
            window.entry = AssetDatabase.LoadAssetAtPath<BestiaryEntry>("Assets/Bestiary/Entries/SnakeEntry.asset");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Bestiary artwork from your enemy model", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Preview a grayscale portrait and a white silhouette. Saving creates new transparent PNG sprites; assigning an Entry also updates its artwork fields.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        enemyPrefab = (GameObject)EditorGUILayout.ObjectField("Enemy Prefab", enemyPrefab, typeof(GameObject), false);
        entry = (BestiaryEntry)EditorGUILayout.ObjectField("Entry (optional)", entry, typeof(BestiaryEntry), false);
        yaw = EditorGUILayout.Slider("View angle", yaw, -180f, 180f);
        pitch = EditorGUILayout.Slider("View from above", pitch, -30f, 80f);
        framing = EditorGUILayout.Slider("Frame margin", framing, 1.05f, 2f);
        if (EditorGUI.EndChangeCheck()) ClearPreviews();

        using (new EditorGUI.DisabledScope(enemyPrefab == null || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Generate Previews")) GeneratePreviews();

        EditorGUILayout.BeginHorizontal();
        DrawPreview("Portrait", portrait);
        DrawPreview("Silhouette", silhouette);
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(portrait == null || silhouette == null || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Save Sprites and Assign to Entry")) SaveSprites();
        EditorGUILayout.LabelField("Output: " + OutputFolder, EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
    }

    static void DrawPreview(string label, Texture2D texture)
    {
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(240f, 210f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
        if (texture != null) EditorGUI.DrawTextureTransparent(rect, texture, ScaleMode.ScaleToFit);
        EditorGUILayout.EndVertical();
    }

    void GeneratePreviews()
    {
        ClearPreviews();
        try
        {
            portrait = Capture(false);
            silhouette = Capture(true);
            status = "Check the pose and framing. Adjust the sliders and regenerate if needed.";
        }
        catch (Exception exception)
        {
            ClearPreviews();
            status = exception.Message;
            Debug.LogException(exception);
        }
    }

    Texture2D Capture(bool flat)
    {
        var preview = new PreviewRenderUtility();
        Material material = null;
        bool previewStarted = false;
        try
        {
            var model = Instantiate(enemyPrefab);
            model.hideFlags = HideFlags.HideAndDontSave;
            model.transform.position = Vector3.zero;
            preview.AddSingleGO(model);
            model.SetActive(true);
            foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var light in model.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var camera in model.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var audio in model.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;

            var renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy
                    && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("The prefab has no visible mesh renderers.");
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);

            Shader shader = Shader.Find(flat ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidOperationException("The required URP shader was not found.");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.black);
            foreach (var renderer in renderers)
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            Camera cameraView = preview.camera;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            float distance = radius * 3f + 1f;
            cameraView.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
            cameraView.orthographic = true;
            cameraView.aspect = 1f;
            cameraView.orthographicSize = ProjectedHalfSize(bounds, rotation) * framing;
            cameraView.nearClipPlane = 0.01f;
            cameraView.farClipPlane = distance + radius * 3f + 1f;
            cameraView.clearFlags = CameraClearFlags.SolidColor;
            cameraView.backgroundColor = Color.clear;
            cameraView.allowHDR = false;
            cameraView.allowMSAA = false;
            var cameraData = cameraView.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.allowXRRendering = false;

            preview.ambientColor = new Color(0.18f, 0.18f, 0.18f);
            preview.lights[0].color = preview.lights[1].color = Color.white;
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = rotation * Quaternion.Euler(35f, -35f, 0f);
            preview.lights[1].intensity = 0.35f;
            preview.lights[1].transform.rotation = rotation * Quaternion.Euler(-20f, 140f, 0f);
            foreach (var light in preview.lights) light.shadows = LightShadows.None;

            float points = Resolution / EditorGUIUtility.pixelsPerPoint;
            preview.BeginPreview(new Rect(0, 0, points, points), GUIStyle.none);
            previewStarted = true;
            preview.Render(true);
            var texture = preview.EndPreview() as RenderTexture;
            previewStarted = false;
            if (texture == null) throw new InvalidOperationException("The preview renderer did not return a render texture.");
            RenderTexture previous = RenderTexture.active;
            Texture2D output = null;
            try
            {
                RenderTexture.active = texture;
                output = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                output.Apply();
                return output;
            }
            catch
            {
                if (output != null) DestroyImmediate(output);
                throw;
            }
            finally { RenderTexture.active = previous; }
        }
        finally
        {
            if (previewStarted) preview.EndPreview();
            preview.Cleanup();
            if (material != null) DestroyImmediate(material);
        }
    }

    static float ProjectedHalfSize(Bounds bounds, Quaternion cameraRotation)
    {
        Quaternion inverse = Quaternion.Inverse(cameraRotation);
        float halfSize = 0.01f;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = inverse * Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    halfSize = Mathf.Max(halfSize, Mathf.Abs(corner.x), Mathf.Abs(corner.y));
                }
        return halfSize;
    }

    void SaveSprites()
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();
            string stem = entry != null && !string.IsNullOrWhiteSpace(entry.enemyId) ? entry.enemyId : enemyPrefab.name;
            stem = Regex.Replace(stem, "[^a-zA-Z0-9_-]", "_");
            Sprite portraitSprite = SaveSprite(portrait, stem + "_Portrait");
            Sprite silhouetteSprite = SaveSprite(silhouette, stem + "_Silhouette");
            if (entry != null)
            {
                Undo.RecordObject(entry, "Assign bestiary artwork");
                entry.portrait = portraitSprite;
                entry.silhouette = silhouetteSprite;
                EditorUtility.SetDirty(entry);
                AssetDatabase.SaveAssetIfDirty(entry);
            }
            status = "Saved both sprites" + (entry != null ? " and assigned them to " + entry.name : "") + ". Open the book to check their size.";
            EditorGUIUtility.PingObject(portraitSprite);
        }
        catch (Exception exception)
        {
            status = exception.Message;
            Debug.LogException(exception);
        }
    }

    static Sprite SaveSprite(Texture2D texture, string name)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + name + ".png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Could not import sprite: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    void ClearPreviews()
    {
        if (portrait != null) DestroyImmediate(portrait);
        if (silhouette != null) DestroyImmediate(silhouette);
        portrait = silhouette = null;
        status = null;
    }

    void OnDisable() => ClearPreviews();
}
