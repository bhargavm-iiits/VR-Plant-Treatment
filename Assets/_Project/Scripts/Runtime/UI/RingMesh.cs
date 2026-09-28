using UnityEngine;

namespace BTP.UI
{
    /// <summary>Builds flat ring meshes (in the XZ plane) for highlights and markers.</summary>
    public static class RingMesh
    {
        public static Mesh Build(float innerRadius, float outerRadius, int segments = 64)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 6];

            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * innerRadius;
                vertices[i * 2 + 1] = direction * outerRadius;
                uvs[i * 2] = new Vector2((float)i / segments, 0f);
                uvs[i * 2 + 1] = new Vector2((float)i / segments, 1f);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
            }

            for (var i = 0; i < segments; i++)
            {
                var v = i * 2;
                var t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = $"Ring_{innerRadius:0.##}_{outerRadius:0.##}" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
