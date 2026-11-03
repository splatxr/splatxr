using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatXR
{
    // The splat mesh read by the splat shaders: four corner vertices (POSITION) per splat "slot" that share
    // the splat's center (TEXCOORD1), color (TEXCOORD2), opacity (TEXCOORD3) and covariance (TEXCOORD5/6).
    // The vertex buffer is only rebuilt when the set of splats changes; re-sorting only rewrites the index
    // buffer, which draws the slots back to front.
    public static class SplatQuads
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 corner;
            public Vector3 center;
            public Vector3 color;
            public float opacity;
            public Vector3 cov0_3;
            public Vector3 cov3_6;
        }

        private static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 3),
        };

        private static readonly Vector3[] Corners =
        {
            new Vector3(-1, -1, 0),
            new Vector3( 1, -1, 0),
            new Vector3(-1,  1, 0),
            new Vector3( 1,  1, 0),
        };

        private const MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

        public static Mesh CreateMesh()
        {
            var mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            mesh.MarkDynamic();
            return mesh;
        }

        // Writes a slot's four vertices from USplat data, mirroring x into Unity's left-handed space.
        // Returns the splat center in mesh space. Safe to call from a background thread.
        public static Vector3 WriteSlot(Vertex[] vertices, int slot, Vector3 position, Vector3 color, float opacity,
                                        Vector3 cov0_3, Vector3 cov3_6)
        {
            position.x = -position.x;
            cov0_3.y = -cov0_3.y;
            cov0_3.z = -cov0_3.z;

            var vertex = new Vertex { center = position, color = color, opacity = opacity, cov0_3 = cov0_3, cov3_6 = cov3_6 };
            int baseVertex = slot * 4;
            for (int j = 0; j < 4; j++)
            {
                vertex.corner = Corners[j];
                vertices[baseVertex + j] = vertex;
            }
            return position;
        }

        public static Bounds ComputeBounds(Vector3[] centers, int count)
        {
            if (count == 0) return new Bounds();

            Vector3 min = centers[0], max = centers[0];
            for (int i = 1; i < count; i++)
            {
                min = Vector3.Min(min, centers[i]);
                max = Vector3.Max(max, centers[i]);
            }
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        // Sorts the first count slots by ascending key and writes their two triangles in that order.
        // Callers pass the negated squared camera distance as the key, so the farthest splat is drawn first.
        // Safe to call from a background thread.
        public static void SortAndWriteIndices(float[] keys, int[] slots, int count, int[] indices)
        {
            System.Array.Sort(keys, slots, 0, count);

            for (int i = 0; i < count; i++)
            {
                int baseVertex = slots[i] * 4;
                int baseIndex = i * 6;

                indices[baseIndex + 0] = baseVertex + 0;
                indices[baseIndex + 1] = baseVertex + 2;
                indices[baseIndex + 2] = baseVertex + 1;

                indices[baseIndex + 3] = baseVertex + 2;
                indices[baseIndex + 4] = baseVertex + 3;
                indices[baseIndex + 5] = baseVertex + 1;
            }
        }

        // Main thread only: replaces the vertex buffer with slotCount slots and sizes the index buffer to draw all of them.
        public static void UploadVertices(Mesh mesh, Vertex[] vertices, int slotCount)
        {
            mesh.Clear();
            if (slotCount == 0) return;

            mesh.SetVertexBufferParams(slotCount * 4, VertexLayout);
            mesh.SetVertexBufferData(vertices, 0, 0, slotCount * 4, 0, UpdateFlags);
            mesh.SetIndexBufferParams(slotCount * 6, IndexFormat.UInt32);
        }

        // Main thread only: draws the first drawCount slots listed in indices.
        public static void UploadIndices(Mesh mesh, int[] indices, int drawCount, Bounds bounds)
        {
            int vertexCount = mesh.vertexCount;
            if (vertexCount == 0) return;

            mesh.SetIndexBufferData(indices, 0, 0, drawCount * 6, UpdateFlags);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, drawCount * 6, MeshTopology.Triangles)
            {
                firstVertex = 0,
                vertexCount = vertexCount,
                bounds = bounds,
            }, UpdateFlags);
            mesh.bounds = bounds;
        }
    }

    // Crop box test with the rotation inverted once instead of per splat. Bounds are in the renderer's local
    // (x-mirrored) space.
    public readonly struct SplatCropBox
    {
        public readonly bool enabled;
        public readonly Vector3 min;
        public readonly Vector3 max;
        public readonly Vector3 rotation;

        private readonly Vector3 center;
        private readonly Vector3 halfSize;
        private readonly bool rotated;
        private readonly Quaternion inverseRotation;

        public SplatCropBox(bool enabled, Vector3 min, Vector3 max, Vector3 rotation)
        {
            this.enabled = enabled;
            this.min = min;
            this.max = max;
            this.rotation = rotation;

            center = (min + max) * 0.5f;
            halfSize = (max - min) * 0.5f;
            rotated = rotation != Vector3.zero;
            inverseRotation = rotated ? Quaternion.Inverse(Quaternion.Euler(rotation)) : Quaternion.identity;
        }

        public bool Contains(Vector3 position)
        {
            if (!enabled) return true;

            Vector3 localPosition = position - center;
            if (rotated)
            {
                localPosition = inverseRotation * localPosition;
            }

            return Mathf.Abs(localPosition.x) <= halfSize.x &&
                   Mathf.Abs(localPosition.y) <= halfSize.y &&
                   Mathf.Abs(localPosition.z) <= halfSize.z;
        }

        // True when both boxes keep the same splats.
        public bool SelectsSameAs(SplatCropBox other)
        {
            if (!enabled && !other.enabled) return true;
            return enabled == other.enabled && min == other.min && max == other.max && rotation == other.rotation;
        }
    }
}
