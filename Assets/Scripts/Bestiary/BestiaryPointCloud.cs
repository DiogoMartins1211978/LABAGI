using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Surface-area sampling from the isolated preview only; no physics or gameplay scanner.
public static class BestiaryPointCloud
{
    struct Triangle { public Vector3 a, b, c, normal; public double cumulativeArea; }

    public static Mesh Build(List<Renderer> renderers, Transform root, int count, float offset)
    {
        var triangles = new List<Triangle>();
        double totalArea = 0;
        foreach (var renderer in renderers)
        {
            Mesh temporary = null;
            try
            {
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    temporary = new Mesh();
                    skin.BakeMesh(temporary);
                    mesh = temporary;
                }
                else mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    throw new InvalidOperationException("Enable Read/Write on the preview mesh import settings: " + renderer.name);
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                Matrix4x4 matrix = root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                    var b = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    var c = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]);
                    var cross = Vector3.Cross(b - a, c - a);
                    double area = cross.magnitude * 0.5;
                    if (area <= 0.000000001) continue;
                    totalArea += area;
                    triangles.Add(new Triangle { a = a, b = b, c = c, normal = cross.normalized, cumulativeArea = totalArea });
                }
            }
            finally { if (temporary != null) UnityEngine.Object.Destroy(temporary); }
        }
        if (triangles.Count == 0) throw new InvalidOperationException("The preview has no sampleable mesh triangles.");
        count = Mathf.Clamp(count, 100, 50000);
        var positions = new Vector3[count * 4];
        var uvs = new Vector2[count * 4];
        var faces = new int[count * 6];
        var random = new System.Random(137);
        for (int i = 0; i < count; i++)
        {
            double target = random.NextDouble() * totalArea;
            int low = 0, high = triangles.Count - 1;
            while (low < high) { int mid = (low + high) / 2; if (triangles[mid].cumulativeArea < target) low = mid + 1; else high = mid; }
            var triangle = triangles[low];
            float u = Mathf.Sqrt((float)random.NextDouble());
            float v = (float)random.NextDouble();
            var point = triangle.a * (1 - u) + triangle.b * (u * (1 - v)) + triangle.c * (u * v) + triangle.normal * offset;
            int vertex = i * 4, face = i * 6;
            for (int j = 0; j < 4; j++) positions[vertex + j] = point;
            uvs[vertex] = new Vector2(0, 0); uvs[vertex + 1] = new Vector2(1, 0);
            uvs[vertex + 2] = new Vector2(1, 1); uvs[vertex + 3] = new Vector2(0, 1);
            faces[face] = vertex; faces[face + 1] = vertex + 1; faces[face + 2] = vertex + 2;
            faces[face + 3] = vertex; faces[face + 4] = vertex + 2; faces[face + 5] = vertex + 3;
        }
        var result = new Mesh { name = "Bestiary LiDAR Surface Points", indexFormat = count * 4 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        result.vertices = positions;
        result.uv = uvs;
        result.triangles = faces;
        result.RecalculateBounds();
        var bounds = result.bounds;
        bounds.Expand(Mathf.Max(0.01f, bounds.size.magnitude * 0.05f));
        result.bounds = bounds;
        return result;
    }
}
