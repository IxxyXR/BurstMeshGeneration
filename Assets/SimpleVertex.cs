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
        const float epsilon = 0.0001f;
        return math.distancesq(Position, other.Position) < epsilon &&
               math.distancesq(Normal, other.Normal) < epsilon &&
               math.distancesq(UV, other.UV) < epsilon;
    }
}