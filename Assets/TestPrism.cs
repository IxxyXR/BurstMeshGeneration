using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public class TestPrism : MonoBehaviour
{

    public int SideCount = 32;

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

    [ContextMenu("Bench")]
    void Bench()
    {
        // Start timing
        var startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < 1000; i++)
        {
            Go();
        }

        // End timing and log results
        var endTime = Time.realtimeSinceStartup;
        var totalTime = (endTime - startTime) * 1000f; // Convert to milliseconds
        Debug.Log($"Prism generation {SideCount} sides completed in {totalTime:F2}ms (Parallel)");
    }

    [ContextMenu("Go")]
    void Go()
    {
        // Define vertex attributes
        var attributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.TempJob);
        attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
        attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);
        attributes[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);
        
        // Create a prism with 6 sides, radius 1, height 2
        var meshBuilder = CreatePrism(
            sideCount: SideCount, // Hexagonal prism
            radius: 1.0f, // Radius of 1 unit
            height: 2.0f, // Height of 2 units
            attributes: attributes, // Vertex attributes
            allocator: Allocator.TempJob // Temporary allocation
        );

        // Use the mesh builder to create a Unity mesh
        Mesh mesh = new Mesh();
        Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
        var meshData = meshDataArray[0];

        meshBuilder.ToMeshData(ref meshData);
        Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

        // Clean up
        meshBuilder.Dispose();
        attributes.Dispose();

        meshFilter.sharedMesh = mesh;
        
        // Store the mesh data for debug visualization
        vertices = mesh.vertices;
        normals = mesh.normals;
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
    private struct BuildCapVerticesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> CirclePositions;
        [NativeDisableParallelForRestriction] public NativeArray<SimpleVertex> OutputVertices;
        public int SideCount;
        public int TopCapStartIndex;
        public int BottomCapStartIndex;
        public bool IsTop;
        
        public void Execute(int i)
        {
            if (IsTop)
            {
                float3 position = CirclePositions[i];
                float3 normal = new float3(0, 1, 0); // Top cap points up
                
                float2 uv = new float2(
                    (position.x / 2.0f) + 0.5f,
                    (position.z / 2.0f) + 0.5f
                );
                
                OutputVertices[TopCapStartIndex + i] = new SimpleVertex
                {
                    Position = position,
                    Normal = normal,
                    UV = uv
                };
            }
            else
            {
                float3 position = CirclePositions[i + SideCount];
                float3 normal = new float3(0, -1, 0); // Bottom cap points down
                
                float2 uv = new float2(
                    (position.x / 2.0f) + 0.5f,
                    (position.z / 2.0f) + 0.5f
                );
                
                OutputVertices[BottomCapStartIndex + i] = new SimpleVertex
                {
                    Position = position,
                    Normal = normal,
                    UV = uv
                };
            }
        }
    }

    [BurstCompile]
    private struct BuildSideVerticesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> CirclePositions;
        [NativeDisableParallelForRestriction] public NativeArray<SimpleVertex> OutputVertices;
        public int SideCount;
        public int SideVertexBaseIndex;
        
        public void Execute(int i)
        {
            int nextI = (i + 1) % SideCount;
            
            float3 topLeft = CirclePositions[i];
            float3 topRight = CirclePositions[nextI];
            float3 bottomLeft = CirclePositions[i + SideCount];
            float3 bottomRight = CirclePositions[nextI + SideCount];
            
            float3 diagonal1 = bottomRight - topLeft;
            float3 diagonal2 = bottomLeft - topRight;
            float3 faceNormal = math.normalize(math.cross(diagonal1, diagonal2));
            
            float uLeft = (float)i / SideCount;
            float uRight = (float)(i + 1) / SideCount;
            
            int quadBaseIndex = SideVertexBaseIndex + (i * 4);
            
            OutputVertices[quadBaseIndex] = new SimpleVertex
            {
                Position = topLeft,
                Normal = faceNormal,
                UV = new float2(uLeft, 1)
            };
            
            OutputVertices[quadBaseIndex + 1] = new SimpleVertex
            {
                Position = topRight,
                Normal = faceNormal,
                UV = new float2(uRight, 1)
            };
            
            OutputVertices[quadBaseIndex + 2] = new SimpleVertex
            {
                Position = bottomRight,
                Normal = faceNormal,
                UV = new float2(uRight, 0)
            };
            
            OutputVertices[quadBaseIndex + 3] = new SimpleVertex
            {
                Position = bottomLeft,
                Normal = faceNormal,
                UV = new float2(uLeft, 0)
            };
        }
    }

    [BurstCompile]
    private struct ProcessIndicesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> SourceIndices;
        [NativeDisableParallelForRestriction] public NativeArray<ushort> DestIndices;
        public int OrigIndicesCount;
        
        public void Execute(int index)
        {
            int srcIdx = index * 3;
            int dstIdx = OrigIndicesCount + (index * 3);
            
            DestIndices[dstIdx] = (ushort)SourceIndices[srcIdx];
            DestIndices[dstIdx + 1] = (ushort)SourceIndices[srcIdx + 1];
            DestIndices[dstIdx + 2] = (ushort)SourceIndices[srcIdx + 2];
        }
    }

    private static NativeMeshBuilder<SimpleVertex> CreatePrism(
        int sideCount,
        float radius,
        float height,
        NativeArray<VertexAttributeDescriptor> attributes,
        Allocator allocator = Allocator.TempJob)
    {
        if (sideCount < 3)
            throw new ArgumentException("Prism must have at least 3 sides", nameof(sideCount));

        // For non-shared vertices:
        // Top cap: sideCount vertices (including center)
        // Bottom cap: sideCount vertices (including center)
        // Sides: 4 vertices per quad * sideCount (not shared between sides)
        int topCapVertexCount = sideCount;
        int bottomCapVertexCount = sideCount;
        int sideVertexCount = sideCount * 4;
        int totalVertexCount = topCapVertexCount + bottomCapVertexCount + sideVertexCount;
        
        // Calculate triangle counts
        int topCapTriCount = sideCount - 2;      // Fan triangulation of n-gon needs (n-2) triangles
        int bottomCapTriCount = sideCount - 2;    // Same for bottom cap
        int sideTriCount = sideCount * 2;         // Each quad face needs 2 triangles
        int totalTriCount = topCapTriCount + bottomCapTriCount + sideTriCount;
        int totalIndexCount = totalTriCount * 3;  // Each triangle has 3 indices
        
        // Create mesh builder first to ensure proper capacity
        var meshBuilder = new NativeMeshBuilder<SimpleVertex>(
            totalVertexCount,
            totalIndexCount,
            attributes,
            allocator
        );
        
        // Allocate temporary arrays
        NativeArray<float3> circlePositions = new NativeArray<float3>(sideCount * 2, Allocator.TempJob);
        NativeArray<SimpleVertex> vertices = new NativeArray<SimpleVertex>(totalVertexCount, allocator);
        NativeArray<int> indices = new NativeArray<int>(totalIndexCount, Allocator.TempJob);
        
        // Calculate circle points
        var calculateCirclePointsJob = new CalculateCirclePointsJob
        {
            CirclePositions = circlePositions,
            SideCount = sideCount,
            Radius = radius,
            Height = height
        }.Schedule(sideCount * 2, 32);
        
        // Build vertices in parallel
        var buildTopCapJob = new BuildCapVerticesJob
        {
            CirclePositions = circlePositions,
            OutputVertices = vertices,
            SideCount = sideCount,
            TopCapStartIndex = 0,
            BottomCapStartIndex = topCapVertexCount,
            IsTop = true
        }.Schedule(sideCount, 32, calculateCirclePointsJob);
        
        var buildBottomCapJob = new BuildCapVerticesJob
        {
            CirclePositions = circlePositions,
            OutputVertices = vertices,
            SideCount = sideCount,
            TopCapStartIndex = 0,
            BottomCapStartIndex = topCapVertexCount,
            IsTop = false
        }.Schedule(sideCount, 32, buildTopCapJob);
        
        var buildSidesJob = new BuildSideVerticesJob
        {
            CirclePositions = circlePositions,
            OutputVertices = vertices,
            SideCount = sideCount,
            SideVertexBaseIndex = topCapVertexCount + bottomCapVertexCount
        }.Schedule(sideCount, 32, buildBottomCapJob);

        
        var topCapIndicesJob = new FanTriangulateJob
        {
            OutputIndices = indices,
            OutputOffset = 0,
            StartIndex = 0,
            Reversed = false
        }.Schedule(sideCount - 2, 32, buildSidesJob);
        
        var bottomCapIndicesJob = new FanTriangulateJob
        {
            OutputIndices = indices,
            OutputOffset = topCapTriCount * 3,
            StartIndex = topCapVertexCount,
            Reversed = true
        }.Schedule(sideCount - 2, 32, topCapIndicesJob);
        
        var sideQuadsJob = new TriangulateQuadsJob
        {
            StartIndex = topCapVertexCount + bottomCapVertexCount,
            OutputIndices = indices,
            OutputOffset = (topCapTriCount + bottomCapTriCount) * 3
        }.Schedule(sideCount, 1, bottomCapIndicesJob);

        // Wait for all vertex and index generation to complete
        sideQuadsJob.Complete();
        
        // Copy vertices
        var meshVertices = meshBuilder.GetVertices();
        meshVertices.ResizeUninitialized(totalVertexCount);
        vertices.CopyTo(meshVertices.AsArray());
        
        // Copy and convert indices
        var meshIndices = meshBuilder.GetIndices();
        meshIndices.ResizeUninitialized(totalIndexCount);
        
        var processIndicesJob = new ProcessIndicesJob
        {
            SourceIndices = indices,
            DestIndices = meshIndices.AsArray(),
            OrigIndicesCount = 0
        }.Schedule(totalTriCount, 64);
        
        processIndicesJob.Complete();
        
        // Clean up temporary arrays
        circlePositions.Dispose();
        indices.Dispose();
        vertices.Dispose();
        
        return meshBuilder;
    }

    [BurstCompile]
    private struct CalculateCirclePointsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<float3> CirclePositions;
        public int SideCount;
        public float Radius;
        public float Height;

        public void Execute(int index)
        {
            bool isTop = index < SideCount;
            int circleIndex = isTop ? index : index - SideCount;

            // Calculate position on circle
            float angle = 2 * math.PI * circleIndex / SideCount;
            float x = math.cos(angle) * Radius;
            float z = math.sin(angle) * Radius;
            float y = isTop ? Height / 2 : -Height / 2;

            CirclePositions[index] = new float3(x, y, z);
        }
    }
    
    [BurstCompile]
    private struct FanTriangulateJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> OutputIndices;
        public int StartIndex;
        public int OutputOffset;
        public bool Reversed;
        
        public void Execute(int i)
        {
            int baseIdx = OutputOffset + i * 3;
            OutputIndices[baseIdx] = StartIndex + 0;         // Center vertex

            if (Reversed)
            {
                OutputIndices[baseIdx + 1] = StartIndex + i + 1; // Current edge vertex
                OutputIndices[baseIdx + 2] = StartIndex + i + 2; // Next edge vertex
            }
            else
            {
                OutputIndices[baseIdx + 1] = StartIndex + i + 2; // Next edge vertex
                OutputIndices[baseIdx + 2] = StartIndex + i + 1; // Current edge vertex
            }
        }
    }
    
    [BurstCompile]
    private struct TriangulateQuadsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> OutputIndices;
        [ReadOnly] public int StartIndex;
        [ReadOnly] public int OutputOffset;

        public void Execute(int i)
        {
            // Calculate the base index of the quad's vertices
            int quadBaseVertexIndex = StartIndex + (i * 4);
            
            // Each quad is two triangles
            int triangleOffset = OutputOffset + i * 6;
            
            // First triangle (top-left, top-right, bottom-right)
            OutputIndices[triangleOffset] = quadBaseVertexIndex;         // top-left
            OutputIndices[triangleOffset + 1] = quadBaseVertexIndex + 1; // top-right
            OutputIndices[triangleOffset + 2] = quadBaseVertexIndex + 2; // bottom-right
            
            // Second triangle (top-left, bottom-right, bottom-left)
            OutputIndices[triangleOffset + 3] = quadBaseVertexIndex;     // top-left
            OutputIndices[triangleOffset + 4] = quadBaseVertexIndex + 2; // bottom-right
            OutputIndices[triangleOffset + 5] = quadBaseVertexIndex + 3; // bottom-left
        }
    }
}