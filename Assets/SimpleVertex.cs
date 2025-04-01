using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

[StructLayout(LayoutKind.Sequential)]
public struct SimpleVertex : IEquatable<SimpleVertex>
{
    public float3 Position; // Position of the vertex
    public float3 Normal; // Normal of the vertex
    public float2 UV; // UV coordinates

    public bool Equals(SimpleVertex other)
    {
        return Position.Equals(other.Position) &&
               Normal.Equals(other.Normal) &&
               UV.Equals(other.UV);
    }
}