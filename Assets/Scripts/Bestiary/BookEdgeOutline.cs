using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(SkinnedMeshRenderer))]
public class BookEdgeOutline : MonoBehaviour
{
    [SerializeField] Material edgeMaterial;
    [SerializeField] Camera viewCamera;
    [SerializeField, Min(.5f)] float lineWidthPixels = 1.35f;
    Mesh baked, lines;
    int[] triangles;
    Edge[] edges;
    readonly List<int> indices = new List<int>();
    readonly List<Vector3> strokeVertices = new List<Vector3>();
    readonly List<int> strokeTriangles = new List<int>();
    bool[] facing;
    GameObject drawing;
    struct Edge { public int a, b, firstFace, secondFace; }

    // Build adjacency in the open pose, before the closed covers can overlap.
    public void PrepareTopology()
    {
        if (baked == null) baked = new Mesh { name = "Book outline sample" };
        GetComponent<SkinnedMeshRenderer>().BakeMesh(baked);
        var vertices = baked.vertices;
            var representatives = new Dictionary<Vector3Int, int>();
            var ids = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 10000000f);
                if (!representatives.TryGetValue(key, out int id)) { id = i; representatives.Add(key, id); }
                ids[i] = id;
            }
            var adjacency = new Dictionary<(int, int), Edge>();
            triangles = baked.triangles;
            facing = new bool[triangles.Length / 3];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t+1], c = triangles[t+2];
                Add(a,b,t/3); Add(b,c,t/3); Add(c,a,t/3);
            }
            edges = new Edge[adjacency.Count];
            adjacency.Values.CopyTo(edges, 0);
            void Add(int a, int b, int face)
            {
                var key = ids[a]<ids[b] ? (ids[a],ids[b]) : (ids[b],ids[a]);
                if (adjacency.TryGetValue(key, out var edge))
                { edge.secondFace = face; adjacency[key]=edge; }
                else adjacency.Add(key,new Edge {a=a,b=b,firstFace=face,secondFace=-1});
            }
    }

    public void Refresh()
    {
        if (edges == null) PrepareTopology();
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;
        GetComponent<SkinnedMeshRenderer>().BakeMesh(baked);
        var vertices = baked.vertices;
        var towardCamera = transform.InverseTransformDirection(-viewCamera.transform.forward);
        var cameraPosition = transform.InverseTransformPoint(viewCamera.transform.position);
        for (int i = 0; i < facing.Length; i++)
        {
            var a = vertices[triangles[i*3]];
            var b = vertices[triangles[i*3+1]];
            var c = vertices[triangles[i*3+2]];
            var normal = Vector3.Cross(b-a,c-a);
            facing[i] = Vector3.Dot(normal, viewCamera.orthographic ? towardCamera : cameraPosition-(a+b+c)/3) > 0;
        }
        indices.Clear();
        foreach (var edge in edges)
            if (edge.secondFace < 0 || facing[edge.firstFace] != facing[edge.secondFace])
            { indices.Add(edge.a); indices.Add(edge.b); }
        if (lines == null)
        {
            lines = new Mesh { name = "Book white edges", indexFormat = IndexFormat.UInt32 };
            drawing = new GameObject("White Edges",typeof(MeshFilter),typeof(MeshRenderer));
            drawing.layer = gameObject.layer;
            drawing.transform.SetParent(transform,false);
            drawing.GetComponent<MeshFilter>().sharedMesh = lines;
            var renderer = drawing.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = edgeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        // Camera-facing strokes retain their width when the RenderTexture is
        // scaled down; one-pixel hardware lines otherwise break into dashes.
        var offset = transform.InverseTransformVector(-viewCamera.transform.forward * .00008f);
        strokeVertices.Clear();
        strokeTriangles.Clear();
        for (int i = 0; i < indices.Count; i += 2)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i+1]];
            float halfWidth = viewCamera.orthographic ? viewCamera.orthographicSize
                : Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad / 2) * Vector3.Dot(transform.TransformPoint((a+b)/2)-viewCamera.transform.position,viewCamera.transform.forward);
            halfWidth *= lineWidthPixels / viewCamera.pixelHeight;
            var side = transform.InverseTransformVector(Vector3.Cross(transform.TransformVector(b-a),-viewCamera.transform.forward).normalized * halfWidth);
            int first = strokeVertices.Count;
            strokeVertices.Add(a-side+offset);strokeVertices.Add(a+side+offset);
            strokeVertices.Add(b+side+offset);strokeVertices.Add(b-side+offset);
            strokeTriangles.Add(first);strokeTriangles.Add(first+1);strokeTriangles.Add(first+2);
            strokeTriangles.Add(first);strokeTriangles.Add(first+2);strokeTriangles.Add(first+3);
        }
        lines.Clear();
        lines.SetVertices(strokeVertices);
        lines.SetTriangles(strokeTriangles,0);
        lines.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (baked != null) Destroy(baked);
        if (lines != null) Destroy(lines);
        if (drawing != null) Destroy(drawing);
    }
}
