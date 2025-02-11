using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[BurstCompile]
[RequireComponent(typeof(CanvasRenderer))]
public class SolidRectBurst : Graphic
{
    protected override void UpdateGeometry()
    {
        var writableMeshData = Mesh.AllocateWritableMeshData(1);
        var meshData = writableMeshData[0];
        GenerateRect(ref meshData, GetPixelAdjustedRect(), color);

        var bounds = meshData.GetSubMesh(0).bounds;
        Mesh.ApplyAndDisposeWritableMeshData(writableMeshData, workerMesh,
            MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontResetBoneBounds
        );
        workerMesh.bounds = bounds;

        canvasRenderer.SetMesh(workerMesh);
    }

    [BurstCompile]
    private static void GenerateRect(ref Mesh.MeshData meshData, in Rect rect, in Color32 color)
    {
        using var vertexAttributeDescriptors =
            UiVertex.Attributes.ToNativeArray(Allocator.Temp);
        using var builder =
            new NativeMeshBuilder<UiVertex>(4, 6, vertexAttributeDescriptors);

        var uiVertex = new UiVertex { Color = color };

        uiVertex.Position = new Vector2(rect.xMin, rect.yMin);
        uiVertex.Uv = new Vector2(0, 0);
        builder.AddVertex(uiVertex);

        uiVertex.Position = new Vector2(rect.xMin, rect.yMax);
        uiVertex.Uv = new Vector2(0, 1);
        builder.AddVertex(uiVertex);

        uiVertex.Position = new Vector2(rect.xMax, rect.yMax);
        uiVertex.Uv = new Vector2(1, 1);
        builder.AddVertex(uiVertex);

        uiVertex.Position = new Vector2(rect.xMax, rect.yMin);
        uiVertex.Uv = new Vector2(1, 0);
        builder.AddVertex(uiVertex);

        builder.AddQuadIndices(0, 1, 2, 3);
        builder.ToMeshData(ref meshData);

    }
}