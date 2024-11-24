using System.CodeDom;
using System.IO;
using SM64Lib.Model.Fast3D;
using static ReadWrite;

public struct Vtx_tn {
    public short x, y, z; // Position
    public ushort flag;   // Reserved
    public short texX, texY; // Texture coordinates
    public sbyte nx, ny, nz; // Normal vector
    public byte alpha;       // Alpha value

    public Vtx_tn(short x, short y, short z, ushort flag, short texX, short texY, sbyte nx, sbyte ny, sbyte nz, byte alpha) {
        this.x = x; this.y = y; this.z = z;
        this.flag = flag;
        this.texX = texX; this.texY = texY;
        this.nx = nx; this.ny = ny; this.nz = nz;
        this.alpha = alpha;
    }

    public readonly byte[] ToBytes() {
        byte[] vtx = new byte[16];
        WriteU16(vtx, 0, (ushort)x);
        WriteU16(vtx, 2, (ushort)y);
        WriteU16(vtx, 4, (ushort)z);
        WriteU16(vtx, 6, flag);
        WriteU16(vtx, 8, (ushort)texX);
        WriteU16(vtx, 10, (ushort)texY);
        WriteU8(vtx, 12, (byte)nx);
        WriteU8(vtx, 13, (byte)ny);
        WriteU8(vtx, 14, (byte)nz);
        WriteU8(vtx, 15, alpha);
        return vtx;
    }

    public static Vtx_tn Read(byte[] vtxData, uint vertPtr) {
        short x = (short)ReadU16(vtxData, vertPtr + 0);
        short y = (short)ReadU16(vtxData, vertPtr + 2);
        short z = (short)ReadU16(vtxData, vertPtr + 4);
        ushort flag = ReadU16(vtxData, vertPtr + 6);
        short texX = (short)ReadU16(vtxData, vertPtr + 8);
        short texY = (short)ReadU16(vtxData, vertPtr + 10);
        sbyte nx = (sbyte)ReadU8(vtxData, vertPtr + 12);
        sbyte ny = (sbyte)ReadU8(vtxData, vertPtr + 13);
        sbyte nz = (sbyte)ReadU8(vtxData, vertPtr + 14);
        byte alpha = ReadU8(vtxData, vertPtr + 15);

        return new Vtx_tn(x, y, z, flag, texX, texY, nx, ny, nz, alpha);
    }

    public static Vtx_tn Read(Fast3DBuffer buffer, uint vertPtr) {
        long l = buffer.Position;
        buffer.Seek(vertPtr, SeekOrigin.Begin);
        byte[] vtxData = new byte[16];
        buffer.Read(vtxData, 0, vtxData.Length);

        buffer.Seek(l, SeekOrigin.Begin);
        return Read(vtxData, 0);
    }
}

public static class Gbi {
    public static uint _SHIFTL(uint v, int s, int w) {
        return (uint) ((v & ((0x01 << (w)) - 1)) << (s));
    }

    public static void gDma0p(byte[] pkt, RSPCmd c, uint s, uint l)
    {
        WriteU32(pkt, 0, _SHIFTL((byte)c, 24, 8) |
                _SHIFTL(l, 0, 24));
        WriteU32(pkt, 4, s);
    }

    public static void gDma1p(byte[] pkt, RSPCmd c, uint s, uint l, uint p)
    {
        WriteU32(pkt, 0, _SHIFTL((byte)c, 24, 8) | _SHIFTL(p, 16, 8) |
                _SHIFTL(l, 0, 16));
        WriteU32(pkt, 4, s);
    }

    public static void gSPVertex(byte[] pkt, uint v, uint n, uint v0) {
        gDma1p(pkt, RSPCmd.Vertex, v, 0x10*n, (n-1)<<4|(v0));
    }

    public static uint __gsSP1Triangle_w1f(uint v0, uint v1, uint v2, uint flag) {
        return _SHIFTL(flag, 24,8)|_SHIFTL(v0*10,16,8)|
        _SHIFTL(v1*10, 8,8)|_SHIFTL(v2*10, 0,8);
    }

    public static void gSP1Triangle(byte[] pkt, uint v0, uint v1, uint v2, uint flag) {
        WriteU32(pkt, 0, _SHIFTL((byte)RSPCmd.Tri1, 24, 8));
        WriteU32(pkt, 4, __gsSP1Triangle_w1f(v0, v1, v2, flag));
    }

    public static void gDPNoParam(byte[] pkt, RDPCmd cmd)
    {
        WriteU32(pkt, 0, _SHIFTL((byte)cmd, 24, 8));
        WriteU32(pkt, 4, 0);
    }

    public static void gDPNoOp(byte[] pkt) {
        gDPNoParam(pkt, RDPCmd.NOOP);
    }

    public static void gSPNoOp(byte[] pkt) {
        gDma0p(pkt, RSPCmd.NOOP, 0, 0);
    }
}