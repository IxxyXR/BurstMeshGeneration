using System;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public struct NativeMeshBuilder<TVertex> : IDisposable where TVertex : unmanaged
{
    private NativeList<TVertex> vertices;
    private NativeList<ushort> indices;
    private NativeArray<VertexAttributeDescriptor> attributes;

    private int indexOffset;

    public int VertexCount => vertices.Length;
    public int IndexCount => indices.Length;

    public NativeMeshBuilder(int vertexCapacity, int indexCapacity,
        NativeArray<VertexAttributeDescriptor> attributes,
        Allocator allocator = Allocator.Temp)
    {
        vertices = new NativeList<TVertex>(vertexCapacity, allocator);
        indices = new NativeList<ushort>(indexCapacity, allocator);
        this.attributes = attributes;
        indexOffset = 0;
    }
    
    public void AddIndex(int i)
    {
        var index = indices.Length;
        indices.Resize(indices.Length + 3, NativeArrayOptions.UninitializedMemory);
        indices[index] = (ushort)(indexOffset + i);
    }

    public void AddTriangleIndices(int i0, int i1, int i2)
    {
        var index = indices.Length;
        indices.Resize(indices.Length + 3, NativeArrayOptions.UninitializedMemory);
        indices[index] = (ushort)(indexOffset + i0);
        indices[index + 1] = (ushort)(indexOffset + i1);
        indices[index + 2] = (ushort)(indexOffset + i2);
    }
    
    public void AddQuadIndices(int i0, int i1, int i2, int i3)
    {
        var index = indices.Length;
        indices.Resize(indices.Length + 6, NativeArrayOptions.UninitializedMemory);
        indices[index] = (ushort)(indexOffset + i0);
        indices[index + 1] = (ushort)(indexOffset + i1);
        indices[index + 2] = (ushort)(indexOffset + i2);
        indices[index + 3] = (ushort)(indexOffset + i0);
        indices[index + 4] = (ushort)(indexOffset + i2);
        indices[index + 5] = (ushort)(indexOffset + i3);
    }

    public void ToMeshData(ref Mesh.MeshData meshData,
        MeshUpdateFlags meshUpdateFlags = MeshUpdateFlags.Default)
    {
        meshData.SetVertexBufferParams(vertices.Length, attributes);
        var vertexData = meshData.GetVertexData<TVertex>();
        vertexData.CopyFrom(vertices.AsArray());

        meshData.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
        var indexData = meshData.GetIndexData<ushort>();
        indexData.CopyFrom(indices.AsArray());

        meshData.subMeshCount = 1;
        meshData.SetSubMesh(0,
            new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles),
            meshUpdateFlags);
    }


    public void Dispose()
    {
        vertices.Dispose();
        indices.Dispose();
    }
}