using System.Text;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public class PerformanceBenchmark : MonoBehaviour
{
    [Header("Benchmark Settings")]
    [SerializeField] private int warmupIterations = 10;
    [SerializeField] private int benchmarkIterations = 100;

    [Header("Test Configurations")]
    [SerializeField] private int[] testSideCounts = new int[] { 3, 6, 12, 32, 64, 128, 256, 512 };

    private StringBuilder results = new StringBuilder();

    [ContextMenu("Run Full Benchmark Suite")]
    void RunFullBenchmark()
    {
        results.Clear();
        results.AppendLine("=== Burst Mesh Generation Performance Benchmark ===");
        results.AppendLine($"Unity Version: {Application.unityVersion}");
        results.AppendLine($"Platform: {Application.platform}");
        results.AppendLine($"Warmup Iterations: {warmupIterations}");
        results.AppendLine($"Benchmark Iterations: {benchmarkIterations}");
        results.AppendLine();

        // Force Burst compilation by running warmup
        Debug.Log("Warming up Burst compiler...");
        for (int i = 0; i < warmupIterations; i++)
        {
            BenchmarkPrism(6, 1);
        }

        results.AppendLine("| Sides | Vertices | Triangles | Avg Time (ms) | Min (ms) | Max (ms) | Verts/sec | Tris/sec |");
        results.AppendLine("|-------|----------|-----------|---------------|----------|----------|-----------|----------|");

        foreach (int sideCount in testSideCounts)
        {
            BenchmarkPrism(sideCount, benchmarkIterations);
        }

        Debug.Log(results.ToString());

        // Also write to file
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(Application.dataPath, "BenchmarkResults.md"),
            results.ToString()
        );

        Debug.Log("Benchmark complete! Results saved to Assets/BenchmarkResults.md");
    }

    private void BenchmarkPrism(int sideCount, int iterations)
    {
        float totalTime = 0f;
        float minTime = float.MaxValue;
        float maxTime = float.MinValue;

        int vertexCount = 0;
        int triangleCount = 0;

        var attributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp);
        attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
        attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);
        attributes[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

        for (int i = 0; i < iterations; i++)
        {
            var startTime = Time.realtimeSinceStartup;

            var meshBuilder = CreatePrism(sideCount, 1.0f, 2.0f, attributes, Allocator.TempJob);

            // Create mesh to ensure full pipeline runs
            Mesh mesh = new Mesh();
            Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshData = meshDataArray[0];
            meshBuilder.ToMeshData(ref meshData);
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

            vertexCount = mesh.vertexCount;
            triangleCount = mesh.triangles.Length / 3;

            meshBuilder.Dispose();
            Destroy(mesh);

            var endTime = Time.realtimeSinceStartup;
            float iterationTime = (endTime - startTime) * 1000f;

            totalTime += iterationTime;
            minTime = Mathf.Min(minTime, iterationTime);
            maxTime = Mathf.Max(maxTime, iterationTime);
        }

        attributes.Dispose();

        float avgTime = totalTime / iterations;
        float vertsPerSec = (vertexCount * iterations) / (totalTime / 1000f);
        float trisPerSec = (triangleCount * iterations) / (totalTime / 1000f);

        results.AppendLine($"| {sideCount,5} | {vertexCount,8} | {triangleCount,9} | {avgTime,13:F4} | {minTime,8:F4} | {maxTime,8:F4} | {vertsPerSec,9:F0} | {trisPerSec,8:F0} |");
    }

    // Simplified version of CreatePrism from TestPrism - just for benchmarking
    private static NativeMeshBuilder<SimpleVertex> CreatePrism(
        int sideCount,
        float radius,
        float height,
        NativeArray<VertexAttributeDescriptor> attributes,
        Allocator allocator)
    {
        int topCapVertexCount = sideCount;
        int bottomCapVertexCount = sideCount;
        int sideVertexCount = sideCount * 4;
        int totalVertexCount = topCapVertexCount + bottomCapVertexCount + sideVertexCount;

        int topCapTriCount = sideCount - 2;
        int bottomCapTriCount = sideCount - 2;
        int sideTriCount = sideCount * 2;
        int totalTriCount = topCapTriCount + bottomCapTriCount + sideTriCount;
        int totalIndexCount = totalTriCount * 3;

        var meshBuilder = new NativeMeshBuilder<SimpleVertex>(
            totalVertexCount,
            totalIndexCount,
            attributes,
            allocator
        );

        NativeArray<float3> circlePositions = new NativeArray<float3>(sideCount * 2, Allocator.TempJob);
        NativeArray<SimpleVertex> vertices = new NativeArray<SimpleVertex>(totalVertexCount, allocator);
        NativeArray<int> indices = new NativeArray<int>(totalIndexCount, Allocator.TempJob);

        // Generate geometry (simplified, synchronous for benchmarking)
        for (int i = 0; i < sideCount * 2; i++)
        {
            bool isTop = i < sideCount;
            int circleIndex = isTop ? i : i - sideCount;
            float angle = 2 * Mathf.PI * circleIndex / sideCount;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            float y = isTop ? height / 2 : -height / 2;
            circlePositions[i] = new float3(x, y, z);
        }

        // Build vertices (simplified inline version)
        for (int i = 0; i < sideCount; i++)
        {
            // Top cap
            vertices[i] = new SimpleVertex
            {
                Position = circlePositions[i],
                Normal = new float3(0, 1, 0),
                UV = new float2((circlePositions[i].x / 2.0f) + 0.5f, (circlePositions[i].z / 2.0f) + 0.5f)
            };

            // Bottom cap
            vertices[topCapVertexCount + i] = new SimpleVertex
            {
                Position = circlePositions[i + sideCount],
                Normal = new float3(0, -1, 0),
                UV = new float2((circlePositions[i + sideCount].x / 2.0f) + 0.5f, (circlePositions[i + sideCount].z / 2.0f) + 0.5f)
            };
        }

        // Side vertices
        for (int i = 0; i < sideCount; i++)
        {
            int nextI = (i + 1) % sideCount;
            int quadBaseIndex = topCapVertexCount + bottomCapVertexCount + (i * 4);

            float3 topLeft = circlePositions[i];
            float3 topRight = circlePositions[nextI];
            float3 bottomLeft = circlePositions[i + sideCount];
            float3 bottomRight = circlePositions[nextI + sideCount];

            float3 diagonal1 = bottomRight - topLeft;
            float3 diagonal2 = bottomLeft - topRight;
            float3 faceNormal = Vector3.Normalize(Vector3.Cross(diagonal1, diagonal2));

            vertices[quadBaseIndex] = new SimpleVertex { Position = topLeft, Normal = faceNormal, UV = new float2((float)i / sideCount, 1) };
            vertices[quadBaseIndex + 1] = new SimpleVertex { Position = topRight, Normal = faceNormal, UV = new float2((float)(i + 1) / sideCount, 1) };
            vertices[quadBaseIndex + 2] = new SimpleVertex { Position = bottomRight, Normal = faceNormal, UV = new float2((float)(i + 1) / sideCount, 0) };
            vertices[quadBaseIndex + 3] = new SimpleVertex { Position = bottomLeft, Normal = faceNormal, UV = new float2((float)i / sideCount, 0) };
        }

        // Build indices
        int indexOffset = 0;

        // Top cap fan
        for (int i = 0; i < sideCount - 2; i++)
        {
            indices[indexOffset++] = 0;
            indices[indexOffset++] = i + 2;
            indices[indexOffset++] = i + 1;
        }

        // Bottom cap fan (reversed)
        for (int i = 0; i < sideCount - 2; i++)
        {
            indices[indexOffset++] = topCapVertexCount;
            indices[indexOffset++] = topCapVertexCount + i + 1;
            indices[indexOffset++] = topCapVertexCount + i + 2;
        }

        // Side quads
        for (int i = 0; i < sideCount; i++)
        {
            int quadBase = topCapVertexCount + bottomCapVertexCount + (i * 4);
            indices[indexOffset++] = quadBase;
            indices[indexOffset++] = quadBase + 1;
            indices[indexOffset++] = quadBase + 2;
            indices[indexOffset++] = quadBase;
            indices[indexOffset++] = quadBase + 2;
            indices[indexOffset++] = quadBase + 3;
        }

        // Copy to mesh builder
        var meshVertices = meshBuilder.GetVertices();
        meshVertices.ResizeUninitialized(totalVertexCount);
        vertices.CopyTo(meshVertices.AsArray());

        var meshIndices = meshBuilder.GetIndices();
        meshIndices.ResizeUninitialized(totalIndexCount);
        for (int i = 0; i < totalIndexCount; i++)
        {
            meshIndices[i] = (ushort)indices[i];
        }

        circlePositions.Dispose();
        indices.Dispose();
        vertices.Dispose();

        return meshBuilder;
    }
}
