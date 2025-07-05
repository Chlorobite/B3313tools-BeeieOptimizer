using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

	public static void gDPNoParam(byte[] pkt, RDPCmd cmd) {
		WriteU32(pkt, 0, _SHIFTL((byte)cmd, 24, 8));
		WriteU32(pkt, 4, 0);
	}

	public static void gDPNoOp(byte[] pkt) {
		gDPNoParam(pkt, RDPCmd.NOOP);
	}

	public static void gSPNoOp(byte[] pkt) {
		gDma0p(pkt, RSPCmd.NOOP, 0, 0);
	}

	public static void gSPDisplayList(byte[] pkt, uint dl) {
		gDma1p(pkt, RSPCmd.DisplayList, dl, 0, 0x00);
	}

	public static void gSPEndDisplayList(byte[] pkt) {
		gDma0p(pkt, RSPCmd.EndDisplayList, 0, 0);
	}

	public static void gSPCullDisplayList(byte[] pkt, uint vstart, uint vend) {
		WriteU32(pkt, 0, _SHIFTL((byte)RSPCmd.CullDisplayList, 24, 8) |
						((0x0f & (vstart))*40));
		WriteU32(pkt, 4, (0x0f & (vend+1))*40);
	}


	public static void printCmd(byte[] data, int pad) {
		string debugLine = $"UNKNOWN {string.Join(", ", data.Select(b => b.ToString("X2")))}";
		ConsoleColor color = ConsoleColor.Red;

		switch (data[0]) {
			case (byte)RSPCmd.Vertex:
				ushort loadLength = ReadU16(data, 2);
				debugLine = $"gsSPVertex(0x{ReadU32(data, 4):X8}, {loadLength/0x10}, {data[1] & 0xF})";
				color = ConsoleColor.Green; // dma
				break;
			case (byte)RSPCmd.EndDisplayList:
				debugLine = $"gsSPEndDisplayList()";
				color = ConsoleColor.White; // flow
				break;
			case (byte)RSPCmd.ClearGeometryMode:
				debugLine = $"gsSPClearGeometryMode(0x{ReadU32(data, 4):X8})";
				color = ConsoleColor.Blue; // configuration
				break;
			case (byte)RSPCmd.SetGeometryMode:
				debugLine = $"gsSPSetGeometryMode(0x{ReadU32(data, 4):X8})";
				color = ConsoleColor.Blue; // configuration
				break;
			case (byte)RSPCmd.Tri1:
				debugLine = $"gsSP1Triangle({data[5] / 10}, {data[6] / 10}, {data[7] / 10})";
				color = ConsoleColor.DarkGreen; // tri
				break;

			/* ok so basically
			#define	gDPPipelineMode(pkt, mode)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_PIPELINE, 1, mode)
			#define	gsDPPipelineMode(mode)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_PIPELINE, 1, mode)
			*
			#define	gDPSetCycleType(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_CYCLETYPE, 2, type)
			#define	gsDPSetCycleType(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_CYCLETYPE, 2, type)
			*
			#define	gDPSetTexturePersp(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTPERSP, 1, type)
			#define	gsDPSetTexturePersp(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTPERSP, 1, type)
			*
			#define	gDPSetTextureDetail(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTDETAIL, 2, type)
			#define	gsDPSetTextureDetail(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTDETAIL, 2, type)
			*
			#define	gDPSetTextureLOD(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTLOD, 1, type)
			#define	gsDPSetTextureLOD(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTLOD, 1, type)
			*
			#define	gDPSetTextureLUT(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTLUT, 2, type)
			#define	gsDPSetTextureLUT(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTLUT, 2, type)
			*
			#define	gDPSetTextureFilter(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTFILT, 2, type)
			#define	gsDPSetTextureFilter(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTFILT, 2, type)
			*
			#define	gDPSetTextureConvert(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_TEXTCONV, 3, type)
			#define	gsDPSetTextureConvert(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_TEXTCONV, 3, type)
			*
			#define	gDPSetCombineKey(pkt, type)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_COMBKEY, 1, type)
			#define	gsDPSetCombineKey(type)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_COMBKEY, 1, type)
			*
			#ifndef _HW_VERSION_1
			#define	gDPSetColorDither(pkt, mode)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_RGBDITHER, 2, mode)
			#define	gsDPSetColorDither(mode)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_RGBDITHER, 2, mode)
			#else
			#define gDPSetColorDither(pkt, mode)    \
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_COLORDITHER, 1, mode)
			#define gsDPSetColorDither(mode)                \
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_COLORDITHER, 1, mode)
			#endif
			*
			#ifndef _HW_VERSION_1
			#define	gDPSetAlphaDither(pkt, mode)	\
			gSPSetOtherMode(pkt, G_SETOTHERMODE_H, G_MDSFT_ALPHADITHER, 2, mode)
			#define	gsDPSetAlphaDither(mode)		\
			gsSPSetOtherMode(G_SETOTHERMODE_H, G_MDSFT_ALPHADITHER, 2, mode)
			#endif
			*
			*
			#define	gsSPSetOtherMode(cmd, sft, len, data)				\
			{{									\
			_SHIFTL(cmd, 24, 8) | _SHIFTL(sft, 8, 8) | _SHIFTL(len, 0, 8),	\
			(unsigned int)(data)						\
			}}
			*/
			case (byte)RSPCmd.SetOtherModeH: {
				uint uploadData = ReadU32(data, 4);
				Dictionary<byte, string> functions = new() {
					{ 4, "gsDPSetAlphaDither" },
					{ 6, "gsDPSetColorDither" },
					{ 8, "gsDPSetCombineKey" },
					{ 9, "gsDPSetTextureConvert" },
					{ 12, "gsDPSetTextureFilter" },
					{ 14, "gsDPSetTextureLUT" },
					{ 16, "gsDPSetTextureLOD" },
					{ 17, "gsDPSetTextureDetail" },
					{ 19, "gsDPSetTexturePersp" },
					{ 20, "gsDPSetCycleType" },
					{ 23, "gsDPPipelineMode" },
				};

				if (functions.TryGetValue((byte)(data[2] >> 1), out string func)) {
					debugLine = $"{func}(0x{uploadData:X8})";
					color = ConsoleColor.Blue; // configuration
				}
				else {
					debugLine = $"UNKNOWN SetOtherModeH {string.Join(", ", data.Select(b => b.ToString("X2")))}";
					color = ConsoleColor.Red; // unknown
				}
			}
			break;

			/*
			#define gsSPTexture(s, t, level, tile, on)				\
			{{									\
			(_SHIFTL(G_TEXTURE,24,8)|_SHIFTL(BOWTIE_VAL,16,8)|		\
			_SHIFTL((level),11,3)|_SHIFTL((tile),8,3)|_SHIFTL((on),0,8)),	\
			(_SHIFTL((s),16,16)|_SHIFTL((t),0,16))				\
			}}
			*/
			case (byte)RSPCmd.Texture:
				debugLine = $"gsSPTexture({ReadU16(data, 4)}, {ReadU16(data, 6)}, {(data[2] >> 3) & 0x7}, {(data[2] >> 0) & 0x7}, {data[3]})";
				color = ConsoleColor.Blue; // configuration
				break;

			/*
			#define	gsDPFullSync()		gsDPNoParam(G_RDPFULLSYNC)
			#define	gsDPTileSync()		gsDPNoParam(G_RDPTILESYNC)
			#define	gsDPPipeSync()		gsDPNoParam(G_RDPPIPESYNC)
			#define	gsDPLoadSync()		gsDPNoParam(G_RDPLOADSYNC)
			*/
			case (byte)RDPCmd.FullSync:
				debugLine = $"gsDPFullSync()";
				color = ConsoleColor.Magenta; // sync
				break;
			case (byte)RDPCmd.TileSync:
				debugLine = $"gsDPTileSync()";
				color = ConsoleColor.Magenta; // sync
				break;
			case (byte)RDPCmd.PipeSync:
				debugLine = $"gsDPPipeSync()";
				color = ConsoleColor.Magenta; // sync
				break;
			case (byte)RDPCmd.LoadSync:
				debugLine = $"gsDPLoadSync()";
				color = ConsoleColor.Magenta; // sync
				break;

			/*
			#define gsDPLoadTileGeneric(c, tile, uls, ult, lrs, lrt)		\
			{{									\
			_SHIFTL(c, 24, 8) | _SHIFTL(uls, 12, 12) | _SHIFTL(ult, 0, 12),	\
			_SHIFTL(tile, 24, 3) | _SHIFTL(lrs, 12, 12) | _SHIFTL(lrt, 0, 12)\
			}}

			#define	gDPSetTileSize(pkt, t, uls, ult, lrs, lrt)			\
			gDPLoadTileGeneric(pkt, G_SETTILESIZE, t, uls, ult, lrs, lrt)
			#define	gsDPSetTileSize(t, uls, ult, lrs, lrt)				\
			gsDPLoadTileGeneric(G_SETTILESIZE, t, uls, ult, lrs, lrt)
			#define	gDPLoadTile(pkt, t, uls, ult, lrs, lrt)				\
			gDPLoadTileGeneric(pkt, G_LOADTILE, t, uls, ult, lrs, lrt)
			#define	gsDPLoadTile(t, uls, ult, lrs, lrt)				\
			gsDPLoadTileGeneric(G_LOADTILE, t, uls, ult, lrs, lrt)
			*/
			case (byte)RDPCmd.SetTileSize:
				debugLine = $"gsDPSetTileSize({(ReadU16(data, 1) >> 4) & 0xFFF}, {ReadU16(data, 2) & 0xFFF}, {data[4]}, {(ReadU16(data, 5) >> 4) & 0xFFF}, {ReadU16(data, 6) & 0xFFF})";
				color = ConsoleColor.Blue; // configuration
				break;
				// this command appears to be completely unused:fire:
			case (byte)RDPCmd.LoadTile:
				debugLine = $"gsDPLoadTile({(ReadU16(data, 1) >> 4) & 0xFFF}, {ReadU16(data, 2) & 0xFFF}, {data[4]}, {(ReadU16(data, 5) >> 4) & 0xFFF}, {ReadU16(data, 6) & 0xFFF})";
				color = ConsoleColor.Green; // dma?
				break;

			/*
			#define gsDPLoadBlock(tile, uls, ult, lrs, dxt)				\
			{{									\
			(_SHIFTL(G_LOADBLOCK, 24, 8) | _SHIFTL(uls, 12, 12) | 		\
			_SHIFTL(ult, 0, 12)),						\
			(_SHIFTL(tile, 24, 3) | 					\
			_SHIFTL((MIN(lrs,G_TX_LDBLK_MAX_TXL)), 12, 12) |		\
			_SHIFTL(dxt, 0, 12))						\
			}}
			*/
			case (byte)RDPCmd.LoadBlock:
				debugLine = $"gsDPLoadBlock({data[4]}, {(ReadU16(data, 1) >> 4) & 0xFFF}, {ReadU16(data, 2) & 0xFFF}, {(ReadU16(data, 5) >> 4) & 0xFFF}, {ReadU16(data, 6) & 0xFFF})";
				color = ConsoleColor.Green; // dma
				break;

			/*
			#define	gsDPSetTile(fmt, siz, line, tmem, tile, palette, cmt,		\
			maskt, shiftt, cms, masks, shifts)			\
			{{									\
			(_SHIFTL(G_SETTILE, 24, 8) | _SHIFTL(fmt, 21, 3) | 		\
			_SHIFTL(siz, 19, 2) | _SHIFTL(line, 9, 9) | _SHIFTL(tmem, 0, 9)),\
			(_SHIFTL(tile, 24, 3) | _SHIFTL(palette, 20, 4) | 		\
			_SHIFTL(cmt, 18, 2) | _SHIFTL(maskt, 14, 4) | 			\
			_SHIFTL(shiftt, 10, 4) | _SHIFTL(cms, 8, 2) | 			\
			_SHIFTL(masks, 4, 4) | _SHIFTL(shifts, 0, 4))			\
			}}
			*/
			case (byte)RDPCmd.SetTile:
				debugLine = $"gsDPSetTile({(data[1] >> 5) & 0x7}, {(data[1] >> 3) & 0x3}, {(ReadU32(data, 0) >> 9) & 0x1FF}, {(ReadU32(data, 0) >> 0) & 0x1FF}" +
				$", {(ReadU32(data, 4) >> 24) & 0x7}, {(ReadU32(data, 4) >> 20) & 0xF}, {(ReadU32(data, 4) >> 18) & 0x3}, {(ReadU32(data, 4) >> 14) & 0xF}, {(ReadU32(data, 4) >> 10) & 0xF}" +
				$", {(ReadU32(data, 4) >> 8) & 0x3}, {(ReadU32(data, 4) >> 4) & 0xF}, {(ReadU32(data, 4) >> 0) & 0xF})";
				color = ConsoleColor.Blue; // configuration
				break;

			/*
			#define	gsDPSetColor(c, d)						\
			{{									\
				_SHIFTL(c, 24, 8), (unsigned int)(d)				\
			}}

			#define	sDPRGBColor(cmd, r, g, b, a)					\
				gsDPSetColor(cmd,						\
				(_SHIFTL(r, 24, 8) | _SHIFTL(g, 16, 8) | 	\
				_SHIFTL(b, 8, 8) | _SHIFTL(a, 0, 8)))

			#define	gsDPSetEnvColor(r, g, b, a)					\
				sDPRGBColor(G_SETENVCOLOR, r,g,b,a)
			#define	gsDPSetBlendColor(r, g, b, a)					\
				sDPRGBColor(G_SETBLENDCOLOR, r,g,b,a)
			#define	gsDPSetFogColor(r, g, b, a)					\
				sDPRGBColor(G_SETFOGCOLOR, r,g,b,a)
			#define	gsDPSetFillColor(d)						\
				gsDPSetColor(G_SETFILLCOLOR, (d))
			*/
			case (byte)RDPCmd.SetFillColor:
				debugLine = $"gsDPSetFillColor(0x{ReadU32(data, 4):X8})";
				color = ConsoleColor.Cyan; // color
				break;
			case (byte)RDPCmd.SetFogColor:
				debugLine = $"gsDPSetFogColor(0x{ReadU8(data, 4):X2}, 0x{ReadU8(data, 5):X2}, 0x{ReadU8(data, 6):X2}, 0x{ReadU8(data, 7):X2})";
				color = ConsoleColor.Cyan; // color
				break;
			case (byte)RDPCmd.SetBlendColor:
				debugLine = $"gsDPSetBlendColor(0x{ReadU8(data, 4):X2}, 0x{ReadU8(data, 5):X2}, 0x{ReadU8(data, 6):X2}, 0x{ReadU8(data, 7):X2})";
				color = ConsoleColor.Cyan; // color
				break;
			case (byte)RDPCmd.SetEnvColor:
				debugLine = $"gsDPSetEnvColor(0x{ReadU8(data, 4):X2}, 0x{ReadU8(data, 5):X2}, 0x{ReadU8(data, 6):X2}, 0x{ReadU8(data, 7):X2})";
				color = ConsoleColor.Cyan; // color
				break;

			/*
			#define	GCCc0w0(saRGB0, mRGB0, saA0, mA0)				\
			(_SHIFTL((saRGB0), 20, 4) | _SHIFTL((mRGB0), 15, 5) | 	\
			_SHIFTL((saA0), 12, 3) | _SHIFTL((mA0), 9, 3))
			*
			#define	GCCc1w0(saRGB1, mRGB1)						\
			(_SHIFTL((saRGB1), 5, 4) | _SHIFTL((mRGB1), 0, 5))
			*
			#define GCCc0w1(sbRGB0, aRGB0, sbA0, aA0)				\
			(_SHIFTL((sbRGB0), 28, 4) | _SHIFTL((aRGB0), 15, 3) |	\
			_SHIFTL((sbA0), 12, 3) | _SHIFTL((aA0), 9, 3))
			*
			#define	GCCc1w1(sbRGB1, saA1, mA1, aRGB1, sbA1, aA1)			\
			(_SHIFTL((sbRGB1), 24, 4) | _SHIFTL((saA1), 21, 3) |	\
			_SHIFTL((mA1), 18, 3) | _SHIFTL((aRGB1), 6, 3) |	\
			_SHIFTL((sbA1), 3, 3) | _SHIFTL((aA1), 0, 3))
			*
			#define	gsDPSetCombineLERP(a0, b0, c0, d0, Aa0, Ab0, Ac0, Ad0,		\
			a1, b1, c1, d1,	Aa1, Ab1, Ac1, Ad1)			\
			{{									\
			_SHIFTL(G_SETCOMBINE, 24, 8) |					\
			_SHIFTL(GCCc0w0(G_CCMUX_##a0, G_CCMUX_##c0,			\
			G_ACMUX_##Aa0, G_ACMUX_##Ac0) |			\
			GCCc1w0(G_CCMUX_##a1, G_CCMUX_##c1), 0, 24),		\
			(unsigned int)(GCCc0w1(G_CCMUX_##b0, G_CCMUX_##d0,		\
			G_ACMUX_##Ab0, G_ACMUX_##Ad0) |		\
			GCCc1w1(G_CCMUX_##b1, G_ACMUX_##Aa1,		\
			G_ACMUX_##Ac1, G_CCMUX_##d1,		\
			G_ACMUX_##Ab1, G_ACMUX_##Ad1))		\
			}}
			*/
			case (byte)RDPCmd.SetCombine: {
				uint word0 = ReadU32(data, 0);
				uint word1 = ReadU32(data, 4);

				// Decode GCCc0w0 from word0 (bits 0-23)
				int saRGB0 = (int)((word0 >> 20) & 0xF);
				int mRGB0 = (int)((word0 >> 15) & 0x1F);
				int saA0 = (int)((word0 >> 12) & 0x7);
				int mA0 = (int)((word0 >> 9) & 0x7);

				// Decode GCCc1w0 from word0 (bits 24-31)
				int saRGB1 = (int)((word0 >> 5) & 0xF);
				int mRGB1 = (int)(word0 & 0x1F);

				// Decode GCCc0w1 from word1 (bits 0-31)
				int sbRGB0 = (int)((word1 >> 28) & 0xF);
				int aRGB0 = (int)((word1 >> 15) & 0x7);
				int sbA0 = (int)((word1 >> 12) & 0x7);
				int aA0 = (int)((word1 >> 9) & 0x7);

				// Decode GCCc1w1 from word1 (bits 0-31)
				int sbRGB1 = (int)((word1 >> 24) & 0xF);
				int saA1 = (int)((word1 >> 21) & 0x7);
				int mA1 = (int)((word1 >> 18) & 0x7);
				int aRGB1 = (int)((word1 >> 6) & 0x7);
				int sbA1 = (int)((word1 >> 3) & 0x7);
				int aA1 = (int)(word1 & 0x7);

				debugLine = $"gsDPSetCombineLERP({saRGB0}, {mRGB0}, {saA0}, {mA0}, {sbRGB0}, {aRGB0}, {sbA0}, {aA0}, " +
				$"{saRGB1}, {mRGB1}, {sbRGB1}, {saA1}, {mA1}, {aRGB1}, {sbA1}, {aA1})";
				color = ConsoleColor.Blue; // configuration
			}
			break;

			/*
			#define	gsSetImage(cmd, fmt, siz, width, i)				\
			{{									\
			_SHIFTL(cmd, 24, 8) | _SHIFTL(fmt, 21, 3) |			\
			_SHIFTL(siz, 19, 2) | _SHIFTL((width)-1, 0, 12),		\
			(uintptr_t)(i)						\
			}}
			#define	gsDPSetTextureImage(f, s, w, i)	gsSetImage(G_SETTIMG, f, s, w, i)
			*/
			case (byte)RDPCmd.SetTextureImage:
				debugLine = $"gsDPSetTextureImage({(data[1] >> 5) & 0x7}, {(data[1] >> 3) & 0x3}, {ReadU16(data, 2)}, 0x{ReadU32(data, 4):X8})";
				color = ConsoleColor.Blue; // configuration
				break;
		}

		Console.ForegroundColor = color;
		if (pad == -1)
			Console.WriteLine(debugLine);
		else
			Console.Write(debugLine.PadRight(pad, ' '));
	}
}
