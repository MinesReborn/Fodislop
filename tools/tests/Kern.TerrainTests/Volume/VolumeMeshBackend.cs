using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public enum HideFlags { DontSave }
    public readonly record struct Bounds(Vector3 Center, Vector3 Size);
    // Upload backend: validates topology and layout, not Unity/GPU rendering.
    public sealed class Mesh : Object
    {
        public string name = "";
        public IndexFormat indexFormat;
        public HideFlags hideFlags;
        public Bounds bounds;
        public int subMeshCount;
        public int vertexCount;
        public int UploadCount { get; private set; }
        public int LastUploadDestination { get; private set; }
        public int LastUploadCount { get; private set; }
        public int IndexUploadCount { get; private set; }
        public Array Vertices { get; private set; } = Array.Empty<byte>();
        public int[] Indices { get; private set; } = Array.Empty<int>();
        private readonly Dictionary<int, SubMeshDescriptor> _draws = new();
        public void Clear() { _draws.Clear(); }
        public void SetVertexBufferParams(int count, VertexAttributeDescriptor[] layout)
        {
            vertexCount = count;
            if (layout.Length != 1 || layout[0].Format != VertexAttributeFormat.Float32 || layout[0].Dimension != 4)
                throw new InvalidOperationException("Wrong volume vertex layout");
        }
        public void SetVertexBufferData<T>(List<T> data, int start, int destination, int count, int stream, MeshUpdateFlags flags)
            where T : struct
        {
            if (Vertices is not T[] vertices || vertices.Length != vertexCount)
            {
                vertices = new T[vertexCount];
                Vertices = vertices;
            }
            data.CopyTo(start, vertices, destination, count);
            UploadCount++;
            LastUploadDestination = destination;
            LastUploadCount = count;
        }
        public void SetIndexBufferParams(int count, IndexFormat format) { Indices = new int[count]; }
        public void SetIndexBufferData(List<int> data, int start, int destination, int count, MeshUpdateFlags flags)
        {
            data.CopyTo(Indices);
            IndexUploadCount++;
        }
        public void SetSubMesh(int draw, SubMeshDescriptor descriptor, MeshUpdateFlags flags)
        {
            if (descriptor.indexStart + descriptor.indexCount > Indices.Length)
                throw new InvalidOperationException("Draw reads beyond index buffer");
            _draws[draw] = descriptor;
        }
        public SubMeshDescriptor GetSubMesh(int draw) => _draws[draw];
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt32 }
    public enum VertexAttribute { Position, TexCoord0 }
    public enum VertexAttributeFormat { Float16, Float32 }
    public enum MeshTopology { Triangles }
    public enum MeshUpdateFlags { DontRecalculateBounds }
    public readonly record struct VertexAttributeDescriptor(VertexAttribute Attribute, VertexAttributeFormat Format, int Dimension);
    public struct SubMeshDescriptor(int start, int count, MeshTopology topology)
    {
        public MeshTopology Topology = topology;
        public int indexStart = start;
        public int indexCount = count;
        public UnityEngine.Bounds bounds;
    }
}
