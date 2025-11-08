using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public struct NativeMeshBuilder<TVertex> : IDisposable where TVertex : unmanaged
{
    private NativeList<TVertex> vertices;
    private NativeList<ushort> indices;
    private NativeArray<VertexAttributeDescriptor> attributes;
    private int indexOffset;

    public NativeMeshBuilder(int vertexCapacity, int indexCapacity,
        NativeArray<VertexAttributeDescriptor> attributes,
        Allocator allocator = Allocator.Temp)
    {
        vertices = new NativeList<TVertex>(vertexCapacity, allocator);
        indices = new NativeList<ushort>(indexCapacity, allocator);
        this.attributes = attributes;
        indexOffset = 0;
    }

    public NativeMeshBuilder(
        NativeArray<TVertex> vertexData,
        NativeArray<int> faceIndices,
        NativeArray<int> faceSizes,
        NativeArray<VertexAttributeDescriptor> attributes,
        Allocator allocator = Allocator.TempJob)
    {
        // Initialize vertices buffer and copy data
        vertices = new NativeList<TVertex>(vertexData.Length, allocator);
        vertices.ResizeUninitialized(vertexData.Length);
        vertices.AsArray().CopyFrom(vertexData);
        this.attributes = attributes;
        indexOffset = 0;

        // Calculate total number of triangles needed
        NativeArray<int> faceTriangleCounts = new NativeArray<int>(faceSizes.Length, Allocator.TempJob);
        
        // Calculate vertex offsets for each face
        NativeArray<int> faceVertexOffsets = new NativeArray<int>(faceSizes.Length, Allocator.TempJob);
        int vertexOffset = 0;
        for (int i = 0; i < faceSizes.Length; i++)
        {
            faceVertexOffsets[i] = vertexOffset;
            vertexOffset += faceSizes[i];
        }

        // Count triangles per face
        new CountTrianglesPerFaceJob
        {
            FaceSizes = faceSizes,
            TriangleCounts = faceTriangleCounts
        }.Schedule(faceSizes.Length, 32).Complete();

        // Prefix sum to determine indices start position for each face
        NativeArray<int> faceIndicesOffsets = new NativeArray<int>(faceSizes.Length, Allocator.TempJob);
        int totalIndices = 0;
        for (int i = 0; i < faceSizes.Length; i++)
        {
            faceIndicesOffsets[i] = totalIndices;
            totalIndices += faceTriangleCounts[i] * 3;
        }

        // Initialize indices buffer
        indices = new NativeList<ushort>(totalIndices, allocator);
        indices.Resize(totalIndices, NativeArrayOptions.UninitializedMemory);

        // Generate triangulation
        new TriangulateFacesJob
        {
            FaceIndices = faceIndices,
            FaceSizes = faceSizes,
            OutputIndices = indices.AsArray(),
            FaceOffsets = faceIndicesOffsets,
            FaceVertexOffsets = faceVertexOffsets,
            IndexOffset = indexOffset
        }.Schedule(totalIndices / 3, 32).Complete();

        // Clean up temporary arrays
        faceTriangleCounts.Dispose();
        faceIndicesOffsets.Dispose();
        faceVertexOffsets.Dispose();
    }

    [BurstCompile]
    private struct CountTrianglesPerFaceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> FaceSizes;
        [WriteOnly] public NativeArray<int> TriangleCounts;

        public void Execute(int index)
        {
            int vertexCount = FaceSizes[index];
            TriangleCounts[index] = math.max(0, vertexCount - 2); // n-2 triangles for n vertices
        }
    }

    [BurstCompile]
    private struct TriangulateFacesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> FaceIndices;
        [ReadOnly] public NativeArray<int> FaceSizes;
        [NativeDisableParallelForRestriction]
        public NativeArray<ushort> OutputIndices;
        [ReadOnly] public NativeArray<int> FaceOffsets;
        [ReadOnly] public NativeArray<int> FaceVertexOffsets;
        public int IndexOffset;

        public void Execute(int triangleIndex)
        {
            int indexPosition = triangleIndex * 3;

            // Binary search to find which face this triangle belongs to
            int left = 0;
            int right = FaceOffsets.Length - 1;
            int faceIndex = 0;

            while (left <= right)
            {
                int mid = (left + right) / 2;

                if (mid < FaceOffsets.Length - 1)
                {
                    if (indexPosition >= FaceOffsets[mid] && indexPosition < FaceOffsets[mid + 1])
                    {
                        faceIndex = mid;
                        break;
                    }
                    else if (indexPosition < FaceOffsets[mid])
                    {
                        right = mid - 1;
                    }
                    else
                    {
                        left = mid + 1;
                    }
                }
                else
                {
                    // Last element
                    faceIndex = mid;
                    break;
                }
            }

            int faceSize = FaceSizes[faceIndex];
            if (faceSize < 3)
                return;

            int faceStartIndex = FaceVertexOffsets[faceIndex];
            int localTriIndex = triangleIndex - (FaceOffsets[faceIndex] / 3);

            if (localTriIndex >= 0 && localTriIndex < faceSize - 2)
            {
                int firstVertex = FaceIndices[faceStartIndex];
                int outputIndex = triangleIndex * 3;

                OutputIndices[outputIndex] = (ushort)(IndexOffset + firstVertex);
                OutputIndices[outputIndex + 1] = (ushort)(IndexOffset + FaceIndices[faceStartIndex + localTriIndex + 1]);
                OutputIndices[outputIndex + 2] = (ushort)(IndexOffset + FaceIndices[faceStartIndex + localTriIndex + 2]);
            }
        }
    }

    public void AddVertex(TVertex vertex)
    {
        vertices.Add(vertex);
    }

    public void AddIndex(int i)
    {
        indices.Add((ushort)(indexOffset + i));
    }

    public void AddTriangle(int i0, int i1, int i2)
    {
        indices.Add((ushort)(indexOffset + i0));
        indices.Add((ushort)(indexOffset + i1));
        indices.Add((ushort)(indexOffset + i2));
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
            new SubMeshDescriptor(0, indices.Length),
            meshUpdateFlags);
    }

    public NativeList<TVertex> GetVertices()
    {
        return vertices;
    }

    public NativeList<ushort> GetIndices()
    {
        return indices;
    }

    public void Dispose()
    {
        vertices.Dispose();
        indices.Dispose();
        // Note: attributes are owned by caller and should be disposed by them
        // If this causes issues, consider documenting ownership or making a copy
    }
}