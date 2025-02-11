using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UiVertex
{
    public static readonly FixedList512Bytes<VertexAttributeDescriptor> Attributes =
        new FixedList512Bytes<VertexAttributeDescriptor>
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4)
        };

    public Vector3 Position;
    public Color32 Color;
    public Vector4 Uv;
}