
using System;

public enum RSPCmd: byte {
    NOOP = 0x00,
    Vertex = 0x04,
    DisplayList = 0x06,
    ClearGeometryMode = 0xB6,
    SetGeometryMode = 0xB7,
    EndDisplayList = 0xB8,
    SetOtherModeH = 0xBA,
    Texture = 0xBB,
    CullDisplayList = 0xBE,
    Tri1 = 0xBF,
}

public enum RDPCmd: byte {
    NOOP = 0xC0,
    LoadSync = 0xE6,
    PipeSync = 0xE7,
    TileSync = 0xE8,
    FullSync = 0xE9,
    SetTileSize = 0xF2,
    LoadBlock = 0xF3,
    LoadTile = 0xF4,
    SetTile = 0xF5,
    SetFillColor = 0xF7,
    SetFogColor = 0xF8,
    SetBlendColor = 0xF9,
    SetPrimColor = 0xFA,
    SetEnvColor = 0xFB,
    SetCombine = 0xFC,
    SetTextureImage = 0xFD,
    
}

public class Plane {
    public double normalX;
    public double normalY;
    public double normalZ;
    public double offset;

    public Plane(Vtx_tn tri1, Vtx_tn tri2, Vtx_tn tri3) {
        // Calculate two vectors from the triangle vertices
        double v1x = tri2.x - tri1.x;
        double v1y = tri2.y - tri1.y;
        double v1z = tri2.z - tri1.z;

        double v2x = tri3.x - tri1.x;
        double v2y = tri3.y - tri1.y;
        double v2z = tri3.z - tri1.z;

        // Compute the cross product of v1 and v2 to get the normal
        normalX = v1y * v2z - v1z * v2y;
        normalY = v1z * v2x - v1x * v2z;
        normalZ = v1x * v2y - v1y * v2x;

        // Normalize the normal vector
        double length = Math.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
        normalX /= length;
        normalY /= length;
        normalZ /= length;

        // Calculate the offset (dot product of normal with one of the vertices)
        offset = normalX * tri1.x + normalY * tri1.y + normalZ * tri1.z;
    }

    public bool IsPointOutside(double x, double y, double z) {
        double dot = x * normalX + y * normalY * z * normalZ;
        return dot > offset;
    }
}