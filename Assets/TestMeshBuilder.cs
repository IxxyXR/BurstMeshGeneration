using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public class TestMeshBuilder : MonoBehaviour
{
    private MeshFilter meshFilter;
    
    [Header("Debug Visualization")]
    [SerializeField] private bool showNormals;
    [SerializeField] private float normalLength = 0.5f;
    [SerializeField] private Color normalColor = Color.yellow;
    
    // Store mesh data for debugging
    private Vector3[] vertices;
    private Vector3[] normals;

    void Start()
    {
        meshFilter = gameObject.GetComponent<MeshFilter>();
        Go();
    }

    [ContextMenu("Go")]
    void Go()
    {
        // Define vertex attributes
        var attributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.TempJob);
        attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
        attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
        attributes[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);
        
        // Define the cube vertices - 24 vertices (4 per face) for split vertex cube
        NativeArray<SimpleVertex> vertexData = new NativeArray<SimpleVertex>(24, Allocator.TempJob);
        
        // Front face vertices (0-3)
        vertexData[0] = new SimpleVertex {Position = new float3(-1, -1, -1), UV = new float2(0, 0)};
        vertexData[1] = new SimpleVertex {Position = new float3(1, -1, -1), UV = new float2(1, 0)};
        vertexData[2] = new SimpleVertex {Position = new float3(1, 1, -1), UV = new float2(1, 1)};
        vertexData[3] = new SimpleVertex {Position = new float3(-1, 1, -1), UV = new float2(0, 1)};
        
        // Back face vertices (4-7)
        vertexData[4] = new SimpleVertex {Position = new float3(1, -1, 1), UV = new float2(0, 0)};
        vertexData[5] = new SimpleVertex {Position = new float3(-1, -1, 1), UV = new float2(1, 0)};
        vertexData[6] = new SimpleVertex {Position = new float3(-1, 1, 1), UV = new float2(1, 1)};
        vertexData[7] = new SimpleVertex {Position = new float3(1, 1, 1), UV = new float2(0, 1)};
        
        // Left face vertices (8-11)
        vertexData[8] = new SimpleVertex {Position = new float3(-1, -1, 1), UV = new float2(0, 0)};
        vertexData[9] = new SimpleVertex {Position = new float3(-1, -1, -1), UV = new float2(1, 0)};
        vertexData[10] = new SimpleVertex {Position = new float3(-1, 1, -1), UV = new float2(1, 1)};
        vertexData[11] = new SimpleVertex {Position = new float3(-1, 1, 1), UV = new float2(0, 1)};
        
        // Right face vertices (12-15)
        vertexData[12] = new SimpleVertex {Position = new float3(1, -1, -1), UV = new float2(0, 0)};
        vertexData[13] = new SimpleVertex {Position = new float3(1, -1, 1), UV = new float2(1, 0)};
        vertexData[14] = new SimpleVertex {Position = new float3(1, 1, 1), UV = new float2(1, 1)};
        vertexData[15] = new SimpleVertex {Position = new float3(1, 1, -1), UV = new float2(0, 1)};
        
        // Top face vertices (16-19)
        vertexData[16] = new SimpleVertex {Position = new float3(-1, 1, -1), UV = new float2(0, 0)};
        vertexData[17] = new SimpleVertex {Position = new float3(1, 1, -1), UV = new float2(1, 0)};
        vertexData[18] = new SimpleVertex {Position = new float3(1, 1, 1), UV = new float2(1, 1)};
        vertexData[19] = new SimpleVertex {Position = new float3(-1, 1, 1), UV = new float2(0, 1)};
        
        // Bottom face vertices (20-23)
        vertexData[20] = new SimpleVertex {Position = new float3(-1, -1, 1), UV = new float2(0, 0)};
        vertexData[21] = new SimpleVertex {Position = new float3(1, -1, 1), UV = new float2(1, 0)};
        vertexData[22] = new SimpleVertex {Position = new float3(1, -1, -1), UV = new float2(1, 1)};
        vertexData[23] = new SimpleVertex {Position = new float3(-1, -1, -1), UV = new float2(0, 1)};
        
        // Define the cube faces using sequential indices since vertices are no longer shared
        NativeArray<int> faceIndices = new NativeArray<int>(new int[] {
            // Front face
            3, 2, 1, 0,
            // Back face
            7, 6, 5, 4,
            // Left face
            11, 10, 9, 8,
            // Right face
            15, 14, 13, 12,
            // Top face
            19, 18, 17, 16,
            // Bottom face
            23, 22, 21, 20
        }, Allocator.TempJob);

        // Define the size of each face (4 vertices per face for a quad)
        NativeArray<int> faceSizes = new NativeArray<int>(6, Allocator.TempJob);
        for (int i = 0; i < 6; i++)
        {
            faceSizes[i] = 4; // Each face is a quad with 4 vertices
        }

        // Calculate face normals in parallel
        var faceNormals = new NativeArray<float3>(6, Allocator.TempJob);
        
        // Pre-calculate face offsets
        var faceOffsets = new NativeArray<int>(faceSizes.Length, Allocator.TempJob);
        int currentOffset = 0;
        for (int i = 0; i < faceSizes.Length; i++)
        {
            faceOffsets[i] = currentOffset;
            currentOffset += faceSizes[i];
        }

        var calculateNormalsJob = new CalculateFaceNormalsJob
        {
            Vertices = vertexData,
            FaceIndices = faceIndices,
            FaceOffsets = faceOffsets,
            FaceNormals = faceNormals
        };

        var calculateNormalsJobHandle = calculateNormalsJob.Schedule(6, 32);

        // Build reverse lookup: vertex index -> list of face indices
        // This eliminates the O(n×m×k) nested loop problem
        var vertexToFacesMap = new NativeArray<int>(vertexData.Length * 6, Allocator.TempJob); // Max 6 faces per vertex
        var vertexToFacesCount = new NativeArray<int>(vertexData.Length, Allocator.TempJob);

        var buildVertexToFacesJob = new BuildVertexToFacesMapJob
        {
            FaceIndices = faceIndices,
            FaceSizes = faceSizes,
            FaceOffsets = faceOffsets,
            VertexToFacesMap = vertexToFacesMap,
            VertexToFacesCount = vertexToFacesCount,
            MaxFacesPerVertex = 6
        };

        var buildMapHandle = buildVertexToFacesJob.Schedule(calculateNormalsJobHandle);

        // After face normals are calculated, accumulate them for each vertex
        var accumulateNormalsJob = new AccumulateNormalsJob
        {
            Vertices = vertexData,
            FaceNormals = faceNormals,
            VertexToFacesMap = vertexToFacesMap,
            VertexToFacesCount = vertexToFacesCount,
            MaxFacesPerVertex = 6
        };

        var accumulateNormalsJobHandle = accumulateNormalsJob.Schedule(vertexData.Length, 32, buildMapHandle);
        accumulateNormalsJobHandle.Complete();

        vertexToFacesMap.Dispose();
        vertexToFacesCount.Dispose();

        var meshBuilder = new NativeMeshBuilder<SimpleVertex>(
            vertexData,
            faceIndices,
            faceSizes,
            attributes: attributes
        );

        Mesh mesh = new Mesh();
        Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
        var meshData = meshDataArray[0];

        meshBuilder.ToMeshData(ref meshData);
        Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

        // Calculate bounds for proper culling
        mesh.RecalculateBounds();

        // Clean up
        meshBuilder.Dispose();
        attributes.Dispose();
        faceIndices.Dispose();
        faceSizes.Dispose();
        vertexData.Dispose();
        faceNormals.Dispose();
        faceOffsets.Dispose();

        meshFilter.sharedMesh = mesh;

        // Store the mesh data for debug visualization (only if enabled)
        if (showNormals)
        {
            vertices = mesh.vertices;
            normals = mesh.normals;
        }
    }
    
    private void OnDrawGizmos()
    {
        if (!showNormals || vertices == null || normals == null || vertices.Length == 0)
            return;
            
        Gizmos.color = normalColor;
        
        // Apply the object's transform to the visualization
        Matrix4x4 transformMatrix = transform.localToWorldMatrix;
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertexWorldPos = transformMatrix.MultiplyPoint3x4(vertices[i]);
            Vector3 normalWorldDir = transformMatrix.MultiplyVector(normals[i]);
            
            Gizmos.DrawLine(vertexWorldPos, vertexWorldPos + normalWorldDir * normalLength);
        }
    }

    [BurstCompile]
    private struct CalculateFaceNormalsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<SimpleVertex> Vertices;
        [ReadOnly] public NativeArray<int> FaceIndices;
        [ReadOnly] public NativeArray<int> FaceOffsets;
        public NativeArray<float3> FaceNormals;

        public void Execute(int faceIndex)
        {
            int faceStartIndex = FaceOffsets[faceIndex];
            
            // Get vertices in correct winding order (counter-clockwise)
            // Using first three vertices of the face to calculate normal
            float3 v0 = Vertices[FaceIndices[faceStartIndex]].Position;
            float3 v1 = Vertices[FaceIndices[faceStartIndex + 1]].Position;
            float3 v2 = Vertices[FaceIndices[faceStartIndex + 2]].Position;
            
            // Calculate face normal using cross product
            float3 edge1 = v1 - v0;
            float3 edge2 = v2 - v0;
            float3 normal = math.normalize(math.cross(edge1, edge2));
            
            // Store the face normal
            FaceNormals[faceIndex] = normal;
        }
    }

    [BurstCompile]
    private struct BuildVertexToFacesMapJob : IJob
    {
        [ReadOnly] public NativeArray<int> FaceIndices;
        [ReadOnly] public NativeArray<int> FaceSizes;
        [ReadOnly] public NativeArray<int> FaceOffsets;
        [WriteOnly] public NativeArray<int> VertexToFacesMap;
        public NativeArray<int> VertexToFacesCount;
        public int MaxFacesPerVertex;

        public void Execute()
        {
            // Initialize counts to zero
            for (int i = 0; i < VertexToFacesCount.Length; i++)
            {
                VertexToFacesCount[i] = 0;
            }

            // Build the reverse mapping
            for (int faceIndex = 0; faceIndex < FaceSizes.Length; faceIndex++)
            {
                int faceStartIndex = FaceOffsets[faceIndex];
                int faceSize = FaceSizes[faceIndex];

                for (int i = 0; i < faceSize; i++)
                {
                    int vertexIndex = FaceIndices[faceStartIndex + i];
                    int count = VertexToFacesCount[vertexIndex];

                    if (count < MaxFacesPerVertex)
                    {
                        VertexToFacesMap[vertexIndex * MaxFacesPerVertex + count] = faceIndex;
                        VertexToFacesCount[vertexIndex] = count + 1;
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct AccumulateNormalsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<SimpleVertex> Vertices;
        [ReadOnly] public NativeArray<float3> FaceNormals;
        [ReadOnly] public NativeArray<int> VertexToFacesMap;
        [ReadOnly] public NativeArray<int> VertexToFacesCount;
        public int MaxFacesPerVertex;

        public void Execute(int vertexIndex)
        {
            float3 accumulatedNormal = float3.zero;

            // Only iterate over faces that actually use this vertex
            int faceCount = VertexToFacesCount[vertexIndex];
            for (int i = 0; i < faceCount; i++)
            {
                int faceIndex = VertexToFacesMap[vertexIndex * MaxFacesPerVertex + i];
                accumulatedNormal += FaceNormals[faceIndex];
            }

            // Normalize the accumulated normal
            if (!math.all(accumulatedNormal == 0))
            {
                var vertex = Vertices[vertexIndex];
                vertex.Normal = math.normalize(accumulatedNormal);
                Vertices[vertexIndex] = vertex;
            }
        }
    }
}