using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SM64Lib;
using SM64Lib.Geolayout;
using SM64Lib.Levels;
using SM64Lib.Levels.ScrolTex;
using SM64Lib.Model.Fast3D;

using static Gbi;
using static ReadWrite;
using static Simplicity;

static partial class Stages {
	public static void OptimizeFast3D(RomManager manger, Config config, string painting64Path, Dictionary<(byte, byte), AreaPaintingCfg> painting64Cfg) {
		uint GetTextureID(uint ptr, byte texType) {
			return (ptr & 0xFFFFFF) | ((uint)texType << 24);
		}

		const int GLOBAL_TEXTURE_BANK_LIMIT = 256*1024;
		const int LOCAL_TEXTURE_BANK_LIMIT = 256*1024;

		int totalSaves = 0;
		Console.WriteLine($"Purely hypothetically speaking, by merging all duplicate textures...");
		List<(byte[] data, int frequency)> globalTextureBank = [];
		foreach (Level level in manger.Levels) {
			List<byte[]> uniqueTextures = [];

			foreach (LevelArea area in level.Areas) {
				AreaPaintingCfg areaPainting64Cfg = null;
				if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
					areaPainting64Cfg = value;
				}

				Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;

				List<(uint pointer, int length)> loads = [];

				foreach (Geopointer ptr in buffer.DLPointers) {
					uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
					buffer.Seek(dataptr, SeekOrigin.Begin);
					//Console.WriteLine($"Load DL {dataptr:X2}");
					byte[] cmdBuffer = new byte[8];
					uint textureImage = 0;
					byte texType = 0;

					while (dataptr < buffer.Length) {
						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						dataptr += 8;

						uint loadPtr = 0;
						int loadLength = 0;
						// /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

						switch (cmdBuffer[0]) {
							case (byte)RSPCmd.DisplayList:
								throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
							case (byte)RSPCmd.EndDisplayList:
								goto dlEnd;
							case (byte)RDPCmd.LoadBlock: // (contains texture size)
								//Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
								loadPtr = textureImage;

								int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
								switch (texType & 3) {
									case 2: // 16 bit
										length *= 2;
										break;
									case 3: // 32 bit
										length *= 4;
										break;
								}

								if (areaPainting64Cfg != null) {
									if (textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
										length = 0; // do not load the fuck texture, let painting64 handle it
									}
								}

								loadLength = length;
								break;
									case (byte)RDPCmd.SetTextureImage:
										//Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
										texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
										textureImage = ReadU32(cmdBuffer, 4);
										if ((textureImage & 0xFF000000) != 0x0E000000) {
											throw new Exception($"{textureImage:X8} is NOT segment 0E!");
										}
										textureImage &= 0xFFFFFF;
										//Console.WriteLine($"textureImage: {textureImage:X8}");
										break;
						}

						if (loadLength > 0) {
							if (!loads.Contains((loadPtr, loadLength))) {
								loads.Add((loadPtr, loadLength));
							}
						}
					}

					dlEnd:
					continue;
				}

				// Merge intersecting data loads for optimization and to prevent issues.
				loads = loads.OrderBy((load) => load.pointer).ToList();
				for (int i = 0; i < loads.Count - 1; i++) {
					var load1 = loads[i];
					var load2 = loads[i + 1];

					if (load1.pointer + load1.length > load2.pointer) {
						loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer + load2.length));
						loads.RemoveAt(i + 1);
						i--;
					}
				}

				// Now, fetch all the data we need.
				foreach ((uint pointer, int _length) in loads) {
					int length = (_length + 0xF) & ~0xF;
					byte[] data = new byte[length];
					buffer.Seek(pointer, SeekOrigin.Begin);
					buffer.Read(data, 0, length);
					bool exists = false;

					foreach (byte[] texData in uniqueTextures) {
						if (data.SequenceEqual(texData)) {
							exists = true;
							break;
						}
					}

					if (!exists) {
						uniqueTextures.Add(data);
					}
				}
			}

			// Once gathered, add the textures to the global bank.
			foreach (byte[] texData in uniqueTextures) {
				bool exists = false;

				for (int i = 0; i < globalTextureBank.Count; i++) {
					if (globalTextureBank[i].data.SequenceEqual(texData)) {
						globalTextureBank[i] = (globalTextureBank[i].data, globalTextureBank[i].frequency + 1);

						exists = true;
						break;
					}
				}

				if (!exists) {
					globalTextureBank.Add((texData, 1));
				}
			}
		}

		{
			globalTextureBank = globalTextureBank.Where(x=>x.frequency > 1).OrderBy(x => 0-(x.data.Length * (x.frequency - 1))).ToList();
			int saveable = 0;
			int ramPressure = 0;
			int ii = 0;
			for (; ii < globalTextureBank.Count; ii++) {
				int n = globalTextureBank[ii].data.Length * (globalTextureBank[ii].frequency - 1);

				if (ramPressure + globalTextureBank[ii].data.Length > GLOBAL_TEXTURE_BANK_LIMIT)
					break;

				saveable += n;
				if (n > 0) {
					ramPressure += globalTextureBank[ii].data.Length;
				}
			}
			while (globalTextureBank.Count > ii)
				globalTextureBank.RemoveAt(ii);
			Console.WriteLine($"Global texture bank: {ramPressure/1024} KiB (save {saveable/1024} KiB)");
			totalSaves += saveable;
		}

		foreach (Level level in manger.Levels) {
			List<(byte[] data, int frequency)> localTextureBank = [];

			foreach (LevelArea area in level.Areas) {
				AreaPaintingCfg areaPainting64Cfg = null;
				if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
					areaPainting64Cfg = value;
				}

				Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;

				List<(uint pointer, int length)> loads = [];
				List<byte[]> uniqueTextures = [];

				foreach (Geopointer ptr in buffer.DLPointers) {
					uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
					buffer.Seek(dataptr, SeekOrigin.Begin);
					//Console.WriteLine($"Load DL {dataptr:X2}");
					byte[] cmdBuffer = new byte[8];
					uint textureImage = 0;
					byte texType = 0;

					while (dataptr < buffer.Length) {
						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						dataptr += 8;

						uint loadPtr = 0;
						int loadLength = 0;
						// /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

						switch (cmdBuffer[0]) {
							case (byte)RSPCmd.DisplayList:
								throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
							case (byte)RSPCmd.EndDisplayList:
								goto dlEnd;
							case (byte)RDPCmd.LoadBlock: // (contains texture size)
								//Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
								loadPtr = textureImage;

								int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
								switch (texType & 3) {
									case 2: // 16 bit
										length *= 2;
										break;
									case 3: // 32 bit
										length *= 4;
										break;
								}

								if (areaPainting64Cfg != null) {
									if (textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
										length = 0; // do not load the fuck texture, let painting64 handle it
									}
								}

								loadLength = length;
								break;
									case (byte)RDPCmd.SetTextureImage:
										//Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
										texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
										textureImage = ReadU32(cmdBuffer, 4);
										if ((textureImage & 0xFF000000) != 0x0E000000) {
											throw new Exception($"{textureImage:X8} is NOT segment 0E!");
										}
										textureImage &= 0xFFFFFF;
										//Console.WriteLine($"textureImage: {textureImage:X8}");
										break;
						}

						if (loadLength > 0) {
							if (!loads.Contains((loadPtr, loadLength))) {
								loads.Add((loadPtr, loadLength));
							}
						}
					}

					dlEnd:
					continue;
				}

				// Merge intersecting data loads for optimization and to prevent issues.
				loads = loads.OrderBy((load) => load.pointer).ToList();
				for (int i = 0; i < loads.Count - 1; i++) {
					var load1 = loads[i];
					var load2 = loads[i + 1];

					if (load1.pointer + load1.length > load2.pointer) {
						loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer + load2.length));
						loads.RemoveAt(i + 1);
						i--;
					}
				}

				// Now, fetch all the data we need.
				foreach ((uint pointer, int _length) in loads) {
					int length = (_length + 0xF) & ~0xF;
					byte[] data = new byte[length];
					buffer.Seek(pointer, SeekOrigin.Begin);
					buffer.Read(data, 0, length);
					bool exists = false;

					foreach (byte[] texData in uniqueTextures) {
						if (data.SequenceEqual(texData)) {
							exists = true;
							break;
						}
					}

					if (!exists) {
						uniqueTextures.Add(data);
					}
				}

				// Finally, add the textures to top textures.
				foreach (byte[] texData in uniqueTextures) {
					bool exists = false;

					for (int i = 0; i < localTextureBank.Count; i++) {
						if (localTextureBank[i].data.SequenceEqual(texData)) {
							localTextureBank[i] = (localTextureBank[i].data, localTextureBank[i].frequency + 1);

							exists = true;
							break;
						}
					}

					if (!exists) {
						localTextureBank.Add((texData, 1));
					}
				}
			}

			localTextureBank = localTextureBank.Where(x=>x.frequency > 1 && !globalTextureBank.Any(y=>y.data.SequenceEqual(x.data)))
			.OrderBy(x => 0-(x.data.Length * (x.frequency - 1))).ToList();
			int saveable = 0;
			int ramPressure = 0;
			int ii = 0;
			for (; ii < localTextureBank.Count; ii++) {
				int n = localTextureBank[ii].data.Length * (localTextureBank[ii].frequency - 1);

				if (ramPressure + localTextureBank[ii].data.Length > LOCAL_TEXTURE_BANK_LIMIT)
					break;

				saveable += n;
				if (n > 0) {
					ramPressure += localTextureBank[ii].data.Length;
				}
			}
			while (localTextureBank.Count > ii)
				localTextureBank.RemoveAt(ii);
			Console.WriteLine($"Local texture bank for level {level.LevelID:X2}: {ramPressure/1024} KiB (save {saveable/1024} KiB)");
			totalSaves += saveable;

			int brothersInChrist = 0;
			foreach (LevelArea area in level.Areas) {
				bool verboseDebug = config.VerboseDebugAreas.Any(ptn => ptn.Match(manger, level, area));
				bool disableUVFix = config.Fast3DUVPatchBlacklist.Any(ptn => ptn.Match(manger, level, area));

				void _printCmd(byte[] data, int pad) {
					if (verboseDebug) {
						printCmd(data, pad);
					}
				}

				AreaPaintingCfg areaPainting64Cfg = null;
				if (painting64Cfg.TryGetValue(((byte)level.LevelID, (byte)area.AreaID), out AreaPaintingCfg value)) {
					areaPainting64Cfg = value;
				}

				Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
				if (buffer.DLPointers.Length == 0) {
					// nothing to do here. in fact this breaks sunken ghost ship
					continue;
				}

				if (verboseDebug) {
					Console.WriteLine($"verbosely debugging level {level.LevelID:X2} area {area.AreaID} which has {area.AreaModel.Fast3DBuffer.DLPointers.Length}x fast3d the buffer is {area.AreaModel.Fast3DBuffer.Length} in size");
				}
				//Console.WriteLine($"OptimizeFast3D: area {level.LevelID:X2}:{area.AreaID} original size {buffer.Length:X2}");

				List<byte> newBuffer = [];
				List<(uint pointer, int length, int type, int _texWidth, int _texHeight, byte tex_fmt, byte tex_siz, uint bpl)> loads = [];
				Dictionary<uint, byte[]> newData = [];
				Dictionary<uint, uint> oldToNewPtrMap = [];
				Dictionary<uint, (bool u, bool v)> clamp = [];

				int STAT_totalLoads = 0;
				int STAT_uniqueLoads = 0;
				int vertexGeometryCount = 1;

				// Scan for all data loads first.
				foreach (Geopointer ptr in buffer.DLPointers) {
					uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
					buffer.Seek(dataptr, SeekOrigin.Begin);
					//Console.WriteLine($"Load DL {dataptr:X2}");
					byte[] cmdBuffer = new byte[8];
					uint textureImage = 0;
					uint texturerImager = 0;
					bool inGeometry = false;

					uint loadPtr = 0;
					int loadLength = 0;
					int loadType = -1;
					byte tex_fmt = 0;
					byte tex_siz = 0;
					uint dxt = 0;
					uint line = 0;
					uint b_txl = 0;
					bool isRGBA16 = false;
					int textureWidth = 0;
					int textureHeight = 0;

					while (dataptr < buffer.Length) {
						void pushma() {
							if (loadLength > 0) {
								bool no = false;

								if (areaPainting64Cfg != null) {
									if (loadPtr == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF)) {
										no = true;
									}
								}

								if (!no) {
									uint bpl = 0;

									if (textureWidth > 0 && textureHeight > 0) {
										if (dxt == 0) { // probably no rgba32 because rom mangler
											bpl = line << 3;
										} else {
											if (dxt > 1) {
												dxt = ReverseDXT(dxt, (uint)textureWidth, b_txl);
											}
											bpl = dxt << 3;
										}
									}

									if (!loads.Contains((loadPtr, loadLength, loadType, textureWidth, textureHeight, tex_fmt, tex_siz, bpl))) {
										STAT_uniqueLoads++;
										loads.Add((loadPtr, loadLength, loadType, textureWidth, textureHeight, tex_fmt, tex_siz, bpl));
									}
								}

								loadPtr = 0;
								loadLength = 0;
								loadType = -1;
								dxt = 0;
								line = 0;
								b_txl = 0;
								textureWidth = 0;
								textureHeight = 0;
								isRGBA16 = false;
							}
						}

						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						dataptr += 8;

						// /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

						switch (cmdBuffer[0]) {
							case (byte)RSPCmd.DisplayList:
								if (inGeometry) {
									inGeometry = false;
								}
								throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
							case (byte)RSPCmd.EndDisplayList:
								if (inGeometry) {
									inGeometry = false;
								}
								goto dlEnd;
							case (byte)RDPCmd.LoadBlock: // (contains texture size)
								pushma();
								//Console.WriteLine($"gsDPLoadBlock {(ReadU16(cmdBuffer, 5) >> 4) + 1:X8}");
								if (inGeometry) {
									inGeometry = false;
								}

								STAT_totalLoads++;
								loadPtr = textureImage;

								int length = (ReadU16(cmdBuffer, 5) >> 4) + 1;
								switch (tex_siz) {
									case 2: // 16 bit
										length *= 2;
										b_txl = 2;
										break;
									case 3: // 32 bit
										length *= 4;
										b_txl = 4;
										break;
								}
								dxt = (uint)((ReadU16(cmdBuffer, 6) & 0xFFF) - 1);
								textureWidth = (ushort)((((1 << 11 /*G_TX_DXT_FRAC*/) - 1) * 8) / (dxt * b_txl)); // tanks Nekotina
								textureHeight = (ushort)(length / textureWidth / b_txl); // tanks gpt

								loadLength = length;
								loadType = 1; // texture
								break;
							case (byte)RDPCmd.SetEnvColor:
								texturerImager++; // idfk but this will create a unique number which is what matters
								break;
							case (byte)RDPCmd.SetTextureImage:
								//Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
								if (inGeometry) {
									inGeometry = false;
								}
								textureImage = ReadU32(cmdBuffer, 4);
								tex_fmt = (byte)((ReadU8(cmdBuffer, 1) >> 5) & 0x7);
								tex_siz = (byte)((ReadU8(cmdBuffer, 1) >> 3) & 0x3);
								isRGBA16 = tex_fmt == 0 && tex_siz == 2;
								if ((textureImage & 0xFF000000) != 0x0E000000) {
									throw new Exception($"{textureImage:X8} is NOT segment 0E!");
								}
								textureImage &= 0xFFFFFF;
								texturerImager = textureImage;
								//Console.WriteLine($"textureImage: {textureImage:X8}");
								break;

							case (byte)RDPCmd.SetTileSize: {
								uint width = (ReadU32(cmdBuffer, 4) >> 12) & 0xFFF;
								uint height = (ReadU32(cmdBuffer, 4) >> 0) & 0xFFF;
								if (width > 124)
									texturerImager |= 0x01000000;
								if (height > 124)
									texturerImager |= 0x02000000;

								/*if (isRGBA16) {
									textureWidth = (int)((width / 4) + 1);
									textureHeight = (int)((height / 4) + 1);
								}*/
							}
							break;

							case (byte)RSPCmd.Vertex:
								pushma();
								//Console.WriteLine($"gsSPVertex {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
								loadLength = ReadU16(cmdBuffer, 2);
								loadPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
								loadPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));

								if (!inGeometry) {
									inGeometry = true;
									vertexGeometryCount++;
								}
								loadType = (int)texturerImager;
								break;
							case (byte)RSPCmd.Tri1:
								pushma();
								break;
							case 0x03: // G_MOVEMEM
								//Console.WriteLine($"G_MOVEMEM {ReadU16(cmdBuffer, 2)} {ReadU32(cmdBuffer, 4):X8}");
								if (inGeometry) {
									inGeometry = false;
								}
								loadLength = ReadU16(cmdBuffer, 2);
								loadPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
								loadType = 0; // lightma
								pushma();
								break;
							case (byte)RDPCmd.SetTile:
								if (inGeometry) {
									inGeometry = false;
								}
								line = (ReadU32(cmdBuffer, 0) >> 9) & 0x1FF;
								// textureLUT << 17 (always 0)
								// texLevel << 19 (always 0)
								break;
							default:
								if (inGeometry) {
									inGeometry = false;
								}
								break;
						}
					}

					dlEnd:
					continue;
				}

				bool tryMapPtr(uint oldPtr, out uint newPtr) {
					newPtr = 0;

					foreach ((uint _old, uint _new) in oldToNewPtrMap) {
						int length = newData[_new].Length;

						if (oldPtr >= _old && oldPtr - _old < length) {
							newPtr = _new + (oldPtr - _old);
							if ((oldPtr == 0x03C6C0 && newPtr == 0x02A6C0) || (oldPtr == 0x03C7A0 && newPtr == 0x02A7B0)) {
								Console.WriteLine($"the mapping visione: {_new:X8} + ({oldPtr:X8} - {_old:X8})");
							}
							return true;
						}
					}

					return false;
				}

				// Merge intersecting data loads for optimization and to prevent issues.
				loads = loads.OrderBy((load) => load.pointer).ToList();
				for (int i = 0; i < loads.Count - 1; i++) {
					var load1 = loads[i];
					var load2 = loads[i + 1];

					if (load1.pointer + load1.length > load2.pointer) {
						loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer - load1.pointer + load2.length), load1.type, load1._texWidth, load1._texHeight, load1.tex_fmt, load1.tex_siz, load1.bpl);
						loads.RemoveAt(i + 1);
						i--;
					}
				}

				// Merge consecutive data loads of similar data.
				for (int i = 0; i < loads.Count - 1; i++) {
					var load1 = loads[i];
					var load2 = loads[i + 1];

					if (load1.pointer + load1.length >= load2.pointer && load1.type > 1 && load1.type == load2.type && load1._texWidth == load2._texWidth && load1._texHeight == load2._texHeight && load1.tex_fmt == load2.tex_fmt && load1.tex_siz == load2.tex_siz && load1.bpl == load2.bpl) {
						loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer - load1.pointer + load2.length), load1.type, load1._texWidth, load1._texHeight, load1.tex_fmt, load1.tex_siz, load1.bpl);
						loads.RemoveAt(i + 1);
						i--;
					}
				}

				// Now, fetch all the data we need.
				foreach ((uint pointer, int length, int type, int texWidth, int texHeight, byte tex_fmt, byte tex_siz, uint bpl) in loads) {
					byte[] data = new byte[length];
					buffer.Seek(pointer, SeekOrigin.Begin);
					buffer.Read(data, 0, length);
					byte[] pad = new byte[0x10 - (length & 0xF)];
					bool exists = false;

					if (type > 1 && (length & 0xF) == 0 && !disableUVFix) {
						// vertex load,
						// see if we can like unshit the uvs on this or something
						Vtx_tn[] vertices = new Vtx_tn[length / 0x10];
						for (int i = 0; i < vertices.Length; i++) {
							vertices[i] = Vtx_tn.Read(data, (uint)(i * 0x10));
						}

						uint texPtr = (uint)(type & 0xFFFFFF);
						bool wide = (type & 0x01000000) != 0;
						bool tall = (type & 0x02000000) != 0;

						const short UV_SNAP = 1024;
						// geometries spanning a lot of surfaces will have a lot of uv wraparound which causes artifacts when scaled,
						// a max size is necessary to prevent these artifacts
						const short UV_SNAP_MAX_SIZE = 4096;
						const short UV_SNAP_MARGIN = 128;
						const short UV_HALF_PIXEL = 16;
						const short U_OFFSET = -16;
						const short V_OFFSET = -16;

						short uMin = vertices.Min(v => v.texX);
						short uMax = vertices.Max(v => v.texX);
						short vMin = vertices.Min(v => v.texY);
						short vMax = vertices.Max(v => v.texY);

						if (uMin < uMax - (UV_SNAP - UV_SNAP_MARGIN * 2) && vMin < vMax - (UV_SNAP - UV_SNAP_MARGIN * 2)) {
							short uMinNew = uMin;
							short uMaxNew = uMax;
							short vMinNew = vMin;
							short vMaxNew = vMax;

							if ((uMax - uMin) < UV_SNAP_MAX_SIZE) {
								short dist = (short)uvmod(uMinNew, UV_SNAP);
								if (dist > UV_SNAP / 2)
									dist -= UV_SNAP;
								if (Math.Abs(dist) < UV_SNAP_MARGIN) {
									uMinNew = (short)(uMinNew - dist + UV_HALF_PIXEL + U_OFFSET);
								}

								dist = (short)uvmod(uMaxNew, UV_SNAP);
								if (dist > UV_SNAP / 2)
									dist -= UV_SNAP;
								if (Math.Abs(dist) < UV_SNAP_MARGIN) {
									uMaxNew = (short)(uMaxNew - dist - UV_HALF_PIXEL + U_OFFSET);
								}
							}

							if ((vMax - vMin) < UV_SNAP_MAX_SIZE) {
								short dist = (short)uvmod(vMinNew, UV_SNAP);
								if (dist > UV_SNAP / 2)
									dist -= UV_SNAP;
								if (Math.Abs(dist) < UV_SNAP_MARGIN) {
									vMinNew = (short)(vMinNew - dist + UV_HALF_PIXEL + V_OFFSET);
								}

								dist = (short)uvmod(vMaxNew, UV_SNAP);
								if (dist > UV_SNAP / 2)
									dist -= UV_SNAP;
								if (Math.Abs(dist) < UV_SNAP_MARGIN) {
									vMaxNew = (short)(vMaxNew - dist - UV_HALF_PIXEL + V_OFFSET);
								}
							}

							short uRange = (short)(UV_SNAP * (wide ? 2 : 1) - UV_HALF_PIXEL * 2);
							short uEnd = (short)(uRange + UV_HALF_PIXEL + U_OFFSET);
							short vRange = (short)(UV_SNAP * (tall ? 2 : 1) - UV_HALF_PIXEL * 2);
							short vEnd = (short)(vRange + UV_HALF_PIXEL + V_OFFSET);
							if ((uMaxNew - uMinNew) == uRange) {
								// how convenient...
								uMinNew = UV_HALF_PIXEL + U_OFFSET;
								uMaxNew = uEnd;
							}
							if ((vMaxNew - vMinNew) == vRange) {
								// how convenient...
								vMinNew = UV_HALF_PIXEL + V_OFFSET;
								vMaxNew = vEnd;
							}

							for (int i = 0; i < vertices.Length; i++) {
								Vtx_tn v = vertices[i];
								if (uMin != uMinNew || uMax != uMaxNew)
									v.texX = (short)Math.Round(Remap(v.texX, uMin, uMax, uMinNew, uMaxNew));
								if (vMin != vMinNew || vMax != vMaxNew)
									v.texY = (short)Math.Round(Remap(v.texY, vMin, vMax, vMinNew, vMaxNew));
								vertices[i] = v;
							}

							(bool u, bool v) clampma = (false, false);
							if (uMinNew == UV_HALF_PIXEL + U_OFFSET && uMaxNew == uEnd) {
								clampma.u = true;
							}
							if (vMinNew == UV_HALF_PIXEL + V_OFFSET && vMaxNew == vEnd) {
								clampma.v = true;
							}

							if (!clamp.ContainsKey(texPtr))
								clamp.Add(texPtr, (true, true));
							clamp[texPtr] = (clamp[texPtr].u && clampma.u, clamp[texPtr].v && clampma.v);
						}

						for (int i = 0; i < vertices.Length; i++) {
							byte[] bytes = vertices[i].ToBytes();
							Array.Copy(bytes, 0, data, i * 0x10, 0x10);
						}
					}

					// rgba16 textures
					if (type == 1 && texWidth > 0 && texHeight > 0 && tex_fmt == 0 && tex_siz == 2) {
						if (verboseDebug) {
							Console.WriteLine($"attempted silly on a {texWidth}x{texHeight} texture set of {data.Length / 2} pixels");
						}

						ushort[] silly = new ushort[texWidth * texHeight];
						byte[] sillyBytes = new byte[texWidth * texHeight * 2];
						int texByteSize = texWidth * texHeight * 2;

						for (int i = 0; i <= data.Length - texByteSize; i += texByteSize) {
							// copy in
							for (int j = 0; j < texByteSize; j += 2) {
								silly[j / 2] = (ushort)((data[i + j] << 8) | data[i + j + 1]);
								sillyBytes[j] = data[i + j];
								sillyBytes[j + 1] = data[i + j + 1];
							}

							uint crc = TexUtils.RiceCRC32(sillyBytes, (int)texWidth, (int)texHeight, (int)tex_siz, (int)bpl);
							string crcStr = crc.ToString("X8");

							if (config.TextureCutoutHashBlacklist.Contains(crcStr)) {
								if (verboseDebug) {
									Console.WriteLine($"crc: {crcStr} (from tex_siz {tex_siz} bpl {bpl}), so no");
								}
							}
							else {
								if (verboseDebug) {
									Console.WriteLine($"crc: {crcStr} (from tex_siz {tex_siz} bpl {bpl}), so yes");
								}

								// fix cutout
								TexUtils.RGBA16_cutoutfix(silly, texWidth, texHeight);

								// copy out
								for (int j = 0; j < texByteSize; j += 2) {
									data[i + j] = (byte)(silly[j / 2] >> 8);
									data[i + j + 1] = (byte)(silly[j / 2]);
								}
							}
						}
					}

					foreach (KeyValuePair<uint, byte[]> dataEntry in newData) {
						if (data.SequenceEqual(dataEntry.Value)) {
							exists = true;
							oldToNewPtrMap.Add(pointer, dataEntry.Key);
							break;
						}
					}

					if (!exists) {
						uint newPtr = (uint)newBuffer.Count;
						newBuffer.AddRange(data);
						if (pad.Length < 0x10)
							newBuffer.AddRange(pad);

						newData.Add(newPtr, data);
						oldToNewPtrMap.Add(pointer, newPtr);
					}
				}
				if (areaPainting64Cfg != null) {
					uint newPtr = (uint)newBuffer.Count;
					byte[] fuck = new byte[areaPainting64Cfg.paintingCount * 0x80];
					newBuffer.AddRange(fuck);
					newData.Add(newPtr, fuck);
					oldToNewPtrMap.Add(areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF, newPtr);
				}

				//Console.WriteLine($"Loads: {STAT_totalLoads} total, {STAT_uniqueLoads} unique.");
				//Console.WriteLine($"New data size: {newBuffer.Count:X2}");

				// We have everything, construct display lists now.
				foreach (Geopointer ptr in buffer.DLPointers) {
					uint startptr = (uint)(ptr.SegPointer & 0xFFFFFF);
					uint dataptr = startptr;
					byte[] cmdBuffer = new byte[8];
					byte[] newCmdBuffer = new byte[8];

					// Geometry scan, this optimizes rom manager's gsSPVertex and gsSP1Triangle visione
					Dictionary<uint, byte[]> geometryBalls = [];
					//if (level.LevelID == 0x10 && area.AreaID == 5)
					{
						// Step 1: extract every gsSPVertex and gsSP1Triangle call.
						List<List<(uint ptr, byte[] cmd)>> drawCommandmas = [];
						{
							List<(uint ptr, byte[] cmd)> drawCommands = [];
							uint vertPtr = 0;
							buffer.Seek(dataptr, SeekOrigin.Begin);
							while (dataptr < buffer.Length) {
								buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
								dataptr += 8;

								switch (cmdBuffer[0]) {
									case (byte)RSPCmd.DisplayList:
										throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
									case (byte)RSPCmd.EndDisplayList:
										if (drawCommands.Count > 0)
											drawCommandmas.Add(drawCommands);
									goto __dlEnd;

									case (byte)RSPCmd.Vertex: {
										byte[] newBuf = new byte[8];
										Array.Copy(cmdBuffer, newBuf, cmdBuffer.Length);
										drawCommands.Add((dataptr - 8, newBuf));

										vertPtr = ReadU32(newBuf, 4) & 0xFFFFFF;
									}
									break;
									case (byte)RSPCmd.Tri1: {
										byte[] newBuf = new byte[8];
										Array.Copy(cmdBuffer, newBuf, cmdBuffer.Length);

										uint v1i = cmdBuffer[5] / 10u, v2i = cmdBuffer[6] / 10u, v3i = cmdBuffer[7] / 10u, flag = cmdBuffer[4];
										if (v1i == v2i || v1i == v3i || v2i == v3i) {
											// yes, somehow there's even these type of obvious 0 area triangles
											gSPNoOp(newBuf);
											geometryBalls.Add(dataptr - 8, newBuf);
											break;
										}
										// Read vertices from buffer
										Vtx_tn v1 = Vtx_tn.Read(buffer, vertPtr + (v1i * 0x10));
										Vtx_tn v2 = Vtx_tn.Read(buffer, vertPtr + (v2i * 0x10));
										Vtx_tn v3 = Vtx_tn.Read(buffer, vertPtr + (v3i * 0x10));
										//if (verboseDebug)
										//    Console.Write($"Area: {TriArea(v1, v2, v3)}");

										if (IsTriDegenerate(v1, v2, v3)) {
											gSPNoOp(newBuf);
											geometryBalls.Add(dataptr - 8, newBuf);
											break;
										}

										drawCommands.Add((dataptr - 8, newBuf));
									}
									break;
									default:
										if (drawCommands.Count > 0)
											drawCommandmas.Add(drawCommands);
									drawCommands = [];
									break;
								}
							}
						}

						__dlEnd:
						// Step 2: search for possible reductions
						foreach (List<(uint ptr, byte[] cmd)> drawCommands in drawCommandmas) {
							int lastVertexLoadIndex = -1;
							uint vertexPtr = 0;
							uint vertexCount = 0;

							int debugballs = 0;
							for (int i = 0; i < drawCommands.Count; i++) {
								uint currentCmdPtr = drawCommands[i].ptr;
								Array.Copy(drawCommands[i].cmd, cmdBuffer, cmdBuffer.Length);

								switch (cmdBuffer[0]) {
									case (byte)RSPCmd.Vertex: {
										ushort loadLength = ReadU16(cmdBuffer, 2);
										uint loadedVertexPtr, newVertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
										uint loadedVertexCount, newVertexCount = loadLength / 0x10u;
										int newVertexLoadIndex = i;
										loadedVertexPtr = newVertexPtr;
										loadedVertexCount = newVertexCount;

										if (newVertexPtr == vertexPtr + vertexCount * 0x10 && vertexCount <= 13) {
											debugballs--;
											/*if (newVertexPtr == 0x06C4C0 && newVertexCount == 9) {
											*                                           debugballs = 5;
											*                                           Console.WriteLine($"debugma 5 balls.");
										}*/
											if (debugballs > 0) {
												Console.WriteLine($"\tvertexPtr: 0x0E{vertexPtr:X6}");
												Console.WriteLine($"\tvertexCount: {vertexCount}");
												Console.WriteLine($"\tnewVertexPtr (just loaded): 0x0E{newVertexPtr:X6}");
											}
											// Perform lookahead scan.
											// If we can borrow (at least some of) the next vertex load's triangles
											// by simply loading more vertices in the first place, then we should.
											byte[] _cmdBuffer = new byte[8];
											int j;

											if (debugballs > 0) {
												Console.WriteLine("scan1");
											}
											bool perfectMerge = false;
											for (j = i + 1; j < drawCommands.Count; j++) {
												if (debugballs > 0) {
													Console.Write($"{drawCommands[j].ptr:X8}: ");
													_printCmd(drawCommands[j].cmd, -1);
													Console.ResetColor();
												}
												Array.Copy(drawCommands[j].cmd, _cmdBuffer, _cmdBuffer.Length);

												switch (_cmdBuffer[0]) {
													case (byte)RSPCmd.Tri1:
														int v1 = _cmdBuffer[5] / 10, v2 = _cmdBuffer[6] / 10, v3 = _cmdBuffer[7] / 10;

														if (debugballs > 0) {
															Console.WriteLine($"{vertexCount} + Math.Max({v1}, Math.Max({v2}, {v3})) >= 16");
														}
														if (vertexCount + Math.Max(v1, Math.Max(v2, v3)) >= 16) {
															if (debugballs > 0) {
																Console.WriteLine("break condition: vertexCount");
															}
															goto _search1end;
														}
														break;
													case (byte)RSPCmd.Vertex:
														if (debugballs > 0) {
															Console.WriteLine("break condition: vertex command");
														}
														perfectMerge = true;
														goto _search1end;
												}
											}
											_search1end:
											j--; // to get the last valid command

											if (debugballs > 0) {
												Console.WriteLine("writema balls");
											}
											for (int k = i + 1; k <= j; k++) {
												Array.Copy(drawCommands[k].cmd, _cmdBuffer, _cmdBuffer.Length);
												if (_cmdBuffer[0] != (byte)RSPCmd.Tri1) continue;

												uint v1 = _cmdBuffer[5] / 10u, v2 = _cmdBuffer[6] / 10u, v3 = _cmdBuffer[7] / 10u, flag = _cmdBuffer[4];
												if (newVertexPtr != vertexPtr) {
													newVertexPtr = vertexPtr;
													newVertexCount = 0;
													newVertexLoadIndex = lastVertexLoadIndex;
												}
												newVertexCount = Math.Max(newVertexCount, vertexCount + 1 + Math.Max(v1, Math.Max(v2, v3)));
												if (debugballs > 0) {
													Console.WriteLine($"\tnewVertexPtr: 0x0E{newVertexPtr:X6}");
													Console.WriteLine($"\tnewVertexCount: {newVertexCount}");
												}

												gSP1Triangle(drawCommands[k - 1].cmd, vertexCount + v1, vertexCount + v2, vertexCount + v3, flag);
												if (debugballs > 0) {
													Console.Write($"{drawCommands[k - 1].ptr:X8} = ");
													_printCmd(drawCommands[k - 1].cmd, -1);
													Console.ResetColor();
												}
											}
											if (j > i) {
												uint vertexOffset = 16;
												// These 2 loops start at the first invalid command, which is either a triangle or a vertex.
												// We're only interested if they're triangles, we want to reduce the vertex indices in this case
												for (int k = j + 1; k < drawCommands.Count; k++) {
													Array.Copy(drawCommands[k].cmd, _cmdBuffer, _cmdBuffer.Length);

													switch (_cmdBuffer[0]) {
														case (byte)RSPCmd.Tri1:
															uint v1 = _cmdBuffer[5] / 10u, v2 = _cmdBuffer[6] / 10u, v3 = _cmdBuffer[7] / 10u;

															vertexOffset = Math.Min(vertexOffset, Math.Min(v1, Math.Min(v2, v3)));
															break;
														case (byte)RSPCmd.Vertex:
															goto _search2end;
													}
												}
												_search2end:
												for (int k = j + 1; k < drawCommands.Count; k++) {
													Array.Copy(drawCommands[k].cmd, _cmdBuffer, _cmdBuffer.Length);

													switch (_cmdBuffer[0]) {
														case (byte)RSPCmd.Tri1:
															uint v1 = _cmdBuffer[5] / 10u, v2 = _cmdBuffer[6] / 10u, v3 = _cmdBuffer[7] / 10u, flag = _cmdBuffer[4];

															gSP1Triangle(drawCommands[k].cmd, v1 - vertexOffset, v2 - vertexOffset, v3 - vertexOffset, flag);
															if (debugballs > 0) {
																Console.Write($"{drawCommands[k].ptr:X8} = ");
																_printCmd(drawCommands[k].cmd, -1);
																Console.ResetColor();
															}
															break;
														case (byte)RSPCmd.Vertex:
															goto _search3end;
													}
												}
												_search3end:
												vertexOffset &= 0xF;
												// Then, we insert the new vertex load command :D
												if (perfectMerge || (j >= drawCommands.Count - 1 || drawCommands[j + 1].cmd[0] != (byte)RSPCmd.Tri1))
													gSPNoOp(drawCommands[j].cmd);
												else
													gSPVertex(drawCommands[j].cmd, /*newVertexPtr + newVertexCount * 0x10*/ loadedVertexPtr + vertexOffset * 0x10, loadedVertexCount - vertexOffset, 0);
												if (debugballs > 0) {
													Console.Write($"{drawCommands[j].ptr:X8} = ");
													_printCmd(drawCommands[j].cmd, -1);
													Console.ResetColor();
												}
												gSPVertex(drawCommands[lastVertexLoadIndex].cmd, newVertexPtr, newVertexCount, 0);
												if (debugballs > 0) {
													Console.Write($"{drawCommands[lastVertexLoadIndex].ptr:X8} = ");
													_printCmd(drawCommands[lastVertexLoadIndex].cmd, -1);
													Console.ResetColor();
												}
											}
										}

										vertexPtr = newVertexPtr;
										vertexCount = newVertexCount;
										lastVertexLoadIndex = newVertexLoadIndex;
									}
									break;
								}
							}

							// post analysis to make sure shit isn't being fucked
							if (verboseDebug) {
								Console.WriteLine("where is my ass?");
							}

							int vertMin = 0, vertMax = 0;
							uint vertPtr = 0;
							foreach ((uint ptr, byte[] cmd) bal in drawCommands) {
								if (verboseDebug) {
									_printCmd(bal.cmd, 40);
									Console.ResetColor();
								}

								switch (bal.cmd[0]) {
									case (byte)RSPCmd.Vertex: {
										/* (was chatgpt note ig this can be helpful for whoever reads or something)
										*                                   This command takes vertex data at vertPtr, skips vertMin vertices, and loads *((u16*)(cmd + 2)) bytes of data.
										*                                   Each vertex is 0x10 bytes long and uses the following C struct:
										*
										* Vertex (set up for use with normals)
										*
										t *ypedef struct {
										short		ob[3];	 * x, y, z *
										unsigned short	flag;
										short		tc[2];	 * texture coord *
										signed char	n[3];	 * normal *
										unsigned char   a;       * alpha  *
									} Vtx_tn; // this is a 16 (0x10) byte struct
									Vertex data can be obtained using a prior loaded variable:
									`Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;`
									which implements C# MemoryStream (seek and fetch bytes).

									I have only created ReadU8, ReadU16, ReadU32, ReadU64 functions. Converting to signed requires (short)ReadU16()
									*/


										vertMin = ReadU8(bal.cmd, 1) & 0xF;
										vertMax = ReadU16(bal.cmd, 2) / 0x10 + vertMin;
										vertPtr = ReadU32(bal.cmd, 4) & 0xFFFFFF;
									}
									break;
									case (byte)RSPCmd.Tri1: {
										uint v1i = bal.cmd[5] / 10u, v2i = bal.cmd[6] / 10u, v3i = bal.cmd[7] / 10u, flag = bal.cmd[4];
										if (v1i < vertMin || v1i >= vertMax || v2i < vertMin || v2i >= vertMax || v3i < vertMin || v3i >= vertMax)
											throw new Exception("Bad vertex load");

										/*if (v1i == v2i || v1i == v3i || v2i == v3i) {
										*                                       // yes, somehow there's even these type of obvious 0 area triangles
										*                                       gSPNoOp(bal.cmd);
									}
									// Read vertices from buffer
									Vtx_tn v1 = Vtx_tn.Read(buffer, vertPtr, (int)v1i);
									Vtx_tn v2 = Vtx_tn.Read(buffer, vertPtr, (int)v2i);
									Vtx_tn v3 = Vtx_tn.Read(buffer, vertPtr, (int)v3i);
									//if (verboseDebug)
									//    Console.Write($"Area: {TriArea(v1, v2, v3)}");

									if (IsTriDegenerate(v1, v2, v3)) {
										gSPNoOp(bal.cmd);
									}*/
									}
									break;
								}
								if (verboseDebug)
									Console.WriteLine();
							}

							// finally we geometry the balls
							foreach ((uint ptr, byte[] cmd) bal in drawCommands) {
								geometryBalls.Add(bal.ptr, bal.cmd);
							}
						}
					}

					List<(List<byte> dl, uint sourceStart, uint sourceEnd, uint destStart)> materials = [];
					(List<byte> dl, uint sourceStart, uint sourceEnd, uint destStart)? currentMat = null;
					byte texType = 0;
					bool thefunnytol = false;

					Dictionary<string, ulong> rcpState = [];
					Dictionary<string, ulong> rcpStateKnownBits = [];
					byte envColorA = 255;
					// Returns whether any state was modified.
					bool RcpSetState(string name, ulong value, ulong mask) {
						if (!rcpState.ContainsKey(name))
							rcpState.Add(name, 0);
						if (!rcpStateKnownBits.ContainsKey(name))
							rcpStateKnownBits.Add(name, 0);

						value &= mask; // just to make sure
						/*if (verboseDebug) {
						*                       Console.WriteLine($"Write {name} 0x{value:X8} mask 0x{mask:X8}");
						*                       Console.WriteLine($"Current state {name} 0x{value:X8} mask 0x{mask:X8}");
					}*/
						if ((rcpStateKnownBits[name] & mask) == mask) {
							if ((rcpState[name] & mask) == value) {
								return false;
							}
						}

						rcpState[name] = (rcpState[name] & ~mask) | (value & mask);
						rcpStateKnownBits[name] |= mask;
						return true;
					}

					// first, we identify the materials and extract them.
					dataptr = startptr;
					buffer.Seek(dataptr, SeekOrigin.Begin);
					uint currentTex = 0;
					bool drawingGeometry = false;
					while (dataptr < buffer.Length) {
						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						byte[] tro = new byte[cmdBuffer.Length];
						Array.Copy(cmdBuffer, tro, cmdBuffer.Length);
						if (geometryBalls.TryGetValue(dataptr, out byte[] overrideCmd)) {
							Array.Copy(overrideCmd, cmdBuffer, cmdBuffer.Length);
						}
						Array.Copy(cmdBuffer, newCmdBuffer, cmdBuffer.Length);
						dataptr += 8;
						bool skipCmd = false;
						bool _drawingGeometry = false;
						// /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

						switch (cmdBuffer[0]) {
							case (byte)RSPCmd.NOOP:
							case (byte)RDPCmd.NOOP:
								skipCmd = true; // should be obvious why
								break;
							case 0x03: { // G_MOVEMEM
								// usually used at the end of the prologue of a DL, to set up the lights
								// this isn't drawing geometry, but let's signal that the end of this is the start of the first material
								_drawingGeometry = true;

								uint lightPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

								if (!tryMapPtr(lightPtr, out uint val)) {
									throw new Exception($"G_MOVEMEM with unmapped ptr {lightPtr:X2}??");
								}

								WriteU32(newCmdBuffer, 4, val | 0x0E000000);
							}
							break;
							case (byte)RSPCmd.Vertex: {
								//thefunnytol = Random.Shared.NextDouble() < 0.8;
								skipCmd = thefunnytol;
								_drawingGeometry = true;

								if (!skipCmd) {
									uint vertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
									vertexPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));

									if (!tryMapPtr(vertexPtr, out uint val)) {
										throw new Exception($"gsSPVertex with unmapped ptr {vertexPtr:X2}??");
									}

									val -= (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));
									//if (verboseDebug)
									//    Console.WriteLine($"mapping {vertexPtr | 0x0E000000:X8} -> {val | 0x0E000000:X8}");
									WriteU32(newCmdBuffer, 4, val | 0x0E000000);
								}
							}
							break;
							case (byte)RSPCmd.DisplayList:
								throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
							case (byte)RSPCmd.EndDisplayList:
								goto dlEnd1;
							case (byte)RDPCmd.SetEnvColor:
								envColorA = ReadU8(cmdBuffer, 7);
								currentTex = 0;
								thefunnytol = envColorA <= 1;
								break;
							case (byte)RDPCmd.SetTextureImage: {
								texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
								uint textureImage = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
								currentTex = textureImage;
								//uint textureID = GetTextureID(textureImage, texType);

								thefunnytol = envColorA <= 1;
								if (areaPainting64Cfg != null) {
									thefunnytol = textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF) ||
									(areaPainting64Cfg.textureSegmentedAddresses.Any(addr => textureImage == (addr & 0xFFFFFF)) &&
									!areaPainting64Cfg.textureSegmentedAddresses_NoPurge.Any(addr => textureImage == (addr & 0xFFFFFF)));
								}

								if (!thefunnytol) {
									if (!tryMapPtr(textureImage, out uint val)) {
										throw new Exception($"gsDPSetTextureImage with unmapped ptr {textureImage:X2}??");
									}
									WriteU32(newCmdBuffer, 4, (val & 0x00FFFFFF) | 0x0E000000);
									skipCmd = false;
								}
								else {
									skipCmd = true;
								}

								//if (!newTextureCmds.ContainsKey(val))
								//    newTextureCmds.Add(val, []);
								//Console.WriteLine($"Entering texture {val:X8}");
								//currentList = newTextureCmds[val];

								//WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
								/* TODO: CI4 conversion
								*                           if (texType == ((2 << 2) | 0)) {
								*                               Console.WriteLine("Conversion to CI4 in effect!");
								*                               Console.WriteLine($"{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");
								*                               Console.WriteLine($"{string.Join("", newCmdBuffer.Select(b => b.ToString("X2")))}");
							}*/
							}
							break;
							case (byte)RSPCmd.Tri1:
								_drawingGeometry = true;
								skipCmd = thefunnytol;
								break;

							case (byte)RSPCmd.SetGeometryMode:
								skipCmd = !RcpSetState("SPGeometryMode", 0xFFFFFFFF, ReadU32(cmdBuffer, 4));
								break;
							case (byte)RSPCmd.ClearGeometryMode:
								skipCmd = !RcpSetState("SPGeometryMode", 0x00000000, ReadU32(cmdBuffer, 4));
								break;
							case (byte)RSPCmd.SetOtherModeH:
								skipCmd = !RcpSetState("SPOtherModeH", ReadU32(cmdBuffer, 4), (uint)((1 << cmdBuffer[2]) - 1) << cmdBuffer[3]);
								break;

							case (byte)RDPCmd.FullSync:
							case (byte)RDPCmd.LoadSync:
							case (byte)RDPCmd.PipeSync:
							case (byte)RDPCmd.TileSync:
							case (byte)RDPCmd.LoadBlock: // (contains texture size)
								//skipCmd = currentListContainsCmd((byte)RDPCmd.LoadBlock);
								skipCmd = thefunnytol;
								break;

							case (byte)RDPCmd.SetTileSize:
								skipCmd = !RcpSetState("DPTileSize", ((ulong)(ReadU32(cmdBuffer, 0) & 0xFFFFFF) << 32) | ReadU32(cmdBuffer, 4), 0x00FFFFFFFFFFFFFF);
								break;
							case (byte)RDPCmd.SetTile:
								/* TODO: CI4 conversion
								*                           WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
								*                           if (texType == ((2 << 2) | 0)) {
								*                               WriteU8(newCmdBuffer, 2, (byte)(ReadU8(cmdBuffer, 2) / 2));
						}*/
								if (clamp.TryGetValue(currentTex, out (bool u, bool v) clampma)) {
									if (clampma.u) {
										cmdBuffer[6] |= 0x2;
										newCmdBuffer[6] |= 0x2;
									}
									if (clampma.v) {
										cmdBuffer[5] |= 0x2 << 2;
										newCmdBuffer[5] |= 0x2 << 2;
									}
								}

								skipCmd = !RcpSetState("DPTile", ((ulong)(ReadU32(cmdBuffer, 0) & 0xFFFFFF) << 32) | ReadU32(cmdBuffer, 4), 0x00FFFFFFFFFFFFFF);
								break;

							default:
								//    Console.WriteLine($"Unknown DL command {cmdBuffer[0]:X2}, may alter state. Flushing texture buffers now!");
								//    flushTextureCmds();
								break;
						}

						if (!skipCmd) {
							if (drawingGeometry != _drawingGeometry) {
								if (drawingGeometry) {
									// end of material, commit
									if (currentMat.HasValue) {
										currentMat = (currentMat.Value.dl, currentMat.Value.sourceStart, dataptr - 8, currentMat.Value.destStart);
										if (verboseDebug)
											Console.WriteLine($"END MATERIAL {materials.Count + 1}");
										materials.Add(currentMat.Value);
									}
									// geometry starts here :D
									currentMat = (new List<byte>(), dataptr - 8, 0, 0);
									if (verboseDebug)
										Console.WriteLine($"MATERIAL {materials.Count + 1}");
								}
								else {
									// clear rcp state so a new material can be identified after we're done with tris and verts
									rcpState.Clear();
									rcpStateKnownBits.Clear();
								}

								drawingGeometry = _drawingGeometry;
							}
						}

						if (currentMat != null)
							_printCmd(tro, 50);

						if (!skipCmd) {
							if (currentMat.HasValue) {
								_printCmd(newCmdBuffer, -1);
								currentMat.Value.dl.AddRange(newCmdBuffer);
							}
						}
						else if (verboseDebug && currentMat != null) {
							Console.WriteLine();
						}
					}

					dlEnd1:
					// the amount of bound volume that it has to be reduced to compared to the last check before printing again
					const double VTX_BOUNDS_PERCENTAGE = 0.5;
					// minimum vertex load calls in the geometry between bounds checks
					const int VTX_BOUNDS_MIN_VTX = 20;
					// write every material's code to newBuffer
					for (int i = 0; i < materials.Count; i++) {
						byte[] dl = materials[i].dl.ToArray();
						byte[] _newBufferArr = newBuffer.ToArray();
						Dictionary<int, (byte[] data, ulong volume, uint vtxDataPtr)> bounds = [];

						byte[] cmdScratch = new byte[8];
						byte[] vtxScratch = new byte[0x10];

						short[] aabbXRange = [short.MaxValue, short.MinValue];
						short[] aabbYRange = [short.MaxValue, short.MinValue];
						short[] aabbZRange = [short.MaxValue, short.MinValue];
						int skip = 0;

						for (int j = dl.Length - 8; j >= 0; j -= 8) {
							Array.Copy(dl, j, cmdScratch, 0, 8);

							switch (cmdScratch[0]) {
								case (byte)RSPCmd.Vertex:
									ushort loadLength = ReadU16(cmdScratch, 2);
									uint vertPtr = ReadU32(cmdScratch, 4) & 0xFFFFFF;

									for (int k = 0; k <= loadLength - 0x10; k += 0x10) {
										Array.Copy(_newBufferArr, vertPtr + k, vtxScratch, 0, 0x10);
										Vtx_tn vtx = Vtx_tn.Read(vtxScratch, 0);

										if (vtx.x < aabbXRange[0])
											aabbXRange[0] = vtx.x;
										if (vtx.x > aabbXRange[1])
											aabbXRange[1] = vtx.x;

										if (vtx.y < aabbYRange[0])
											aabbYRange[0] = vtx.y;
										if (vtx.y > aabbYRange[1])
											aabbYRange[1] = vtx.y;

										if (vtx.z < aabbZRange[0])
											aabbZRange[0] = vtx.z;
										if (vtx.z > aabbZRange[1])
											aabbZRange[1] = vtx.z;
									}

									if (skip > 0)
										skip--;
								else {
									byte[] boundVtxData = new byte[0x10 * 8];

									Vtx_tn[] boundVtx = new Vtx_tn[8];
									for (int x = 0; x <= 1; x++) {
										for (int y = 0; y <= 1; y++) {
											for (int z = 0; z <= 1; z++) {
												boundVtx[x * 4 + y * 2 + z] = new(aabbXRange[x], aabbYRange[y], aabbZRange[z], 0, 0, 0, 0, 0, 0, 0);
											}
										}
									}

									for (int k = 0; k < boundVtx.Length; k++) {
										Array.Copy(boundVtx[k].ToBytes(), 0, boundVtxData, k * 0x10, 0x10);
									}
									ulong volume = (ulong)(aabbXRange[1] - aabbXRange[0]) * (ulong)(aabbYRange[1] - aabbYRange[0]) * (ulong)(aabbZRange[1] - aabbZRange[0]);
									bounds.Add(j, (boundVtxData, volume, 0));
								}
								break;
							}
						}

						var keys = bounds.Keys.Order();
						ulong previousVolume = ulong.MaxValue;
						skip = 0;
						foreach (int vtxCmdPtr in keys) {
							bool accept = false;
							if (skip > 0) {
								skip--;
							}
							else {
								ulong volume = bounds[vtxCmdPtr].volume;
								if (volume < previousVolume * VTX_BOUNDS_PERCENTAGE) {
									accept = true;
								}
							}

							if (accept) {
								skip = VTX_BOUNDS_MIN_VTX - 1;
							}
							else {
								bounds.Remove(vtxCmdPtr);
							}
						}

						keys = bounds.Keys.Order();
						foreach (int vtxCmdPtr in keys) {
							// add bounds vtx
							byte[] pad = new byte[0x10 - (newBuffer.Count & 0xF)];
							if (pad.Length < 0x10) {
								newBuffer.AddRange(pad);
							}
							uint boundsVtxPtr = (uint)(0x0E000000 | newBuffer.Count);
							newBuffer.AddRange(bounds[vtxCmdPtr].data);

							bounds[vtxCmdPtr] = (bounds[vtxCmdPtr].data, bounds[vtxCmdPtr].volume, boundsVtxPtr);
						}

						materials[i] = (materials[i].dl, materials[i].sourceStart, materials[i].sourceEnd, (uint)(0x0E000000 | newBuffer.Count));
						if (verboseDebug)
							Console.WriteLine($"material possession {i+1}: 0x{materials[i].sourceStart:X8}-0x{materials[i].sourceEnd:X8}");

						skip = 1;
						for (int j = 0; j <= dl.Length - 8; j += 8) {
							Array.Copy(dl, j, cmdScratch, 0, 8);

							switch (cmdScratch[0]) {
								/*case (byte)RDPCmd.SetTextureImage:
								*                               if (bounds.Count > 0 && skip > 0) {
								*                                   skip--;
								*
								*                                   keys = bounds.Keys.Order();
								*                                   int kek = keys.First();
								*                                   (byte[] data, ulong volume, uint vtxDataPtr) boundmaballs = bounds[kek];
								*
								*                                   // the trol.
								*                                   gSPVertex(cmdScratch, boundmaballs.vtxDataPtr, (uint)(boundmaballs.data.Length / 0x10), 0);
								*                                   newBuffer.AddRange(cmdScratch);
								*                                   gSPCullDisplayList(cmdScratch, 0, (uint)(boundmaballs.data.Length / 0x10 - 1));
								*                                   newBuffer.AddRange(cmdScratch);
								*                                   Array.Copy(dl, j, cmdScratch, 0, 8);
								*
								*                                   bounds.Remove(kek);
							}
							break;*/
								case (byte)RSPCmd.Vertex: {
									if (bounds.TryGetValue(j, out (byte[] data, ulong volume, uint vtxDataPtr) boundmaballs)) {
										// the trol.
										gSPVertex(cmdScratch, boundmaballs.vtxDataPtr, (uint)(boundmaballs.data.Length / 0x10), 0);
										newBuffer.AddRange(cmdScratch);
										gSPCullDisplayList(cmdScratch, 0, (uint)(boundmaballs.data.Length / 0x10 - 1));
										newBuffer.AddRange(cmdScratch);
										Array.Copy(dl, j, cmdScratch, 0, 8);
										bounds.Remove(j);
									}
								}
								break;
							}
							newBuffer.AddRange(cmdScratch);
						}

						// fin
						gSPEndDisplayList(cmdScratch);
						newBuffer.AddRange(cmdScratch);
					}

					// then we write the DL
					void printAndAddDL(byte[] data) {
						_printCmd(data, -1);
						/*Console.WriteLine("Writing DL");
						*                   for (int i = 0; i < data.Length; i += 8) {
						*                       Console.WriteLine($"{ReadU32(data, i):X8} {ReadU32(data, i + 4):X8}");
					}*/

						newBuffer.AddRange(data);
					}

					rcpState.Clear();
					rcpStateKnownBits.Clear();
					dataptr = startptr;
					buffer.Seek(dataptr, SeekOrigin.Begin);
					ptr.SegPointer = 0x0E000000 | newBuffer.Count;
					if (verboseDebug) {
						Console.Write("IN".PadRight(40, ' '));
						Console.WriteLine("OUT");
					}
					while (dataptr < buffer.Length) {
						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						_printCmd(cmdBuffer, 50);
						if (geometryBalls.TryGetValue(dataptr, out byte[] overrideCmd)) {
							Array.Copy(overrideCmd, cmdBuffer, cmdBuffer.Length);
						}
						Array.Copy(cmdBuffer, newCmdBuffer, cmdBuffer.Length);
						(List<byte> dl, uint sourceStart, uint sourceEnd, uint destStart) mat = materials.FirstOrDefault(m => m.sourceStart == dataptr);
						dataptr += 8;
						bool skipCmd = false;
						// /*Print DL*/ Console.WriteLine($"\t{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");

						if (mat.dl != null) {
							gSPDisplayList(newCmdBuffer, mat.destStart);
							//skipCmd = true;

							dataptr = mat.sourceEnd;
							buffer.Seek(dataptr, SeekOrigin.Begin);
						}
						else {
							switch (cmdBuffer[0]) {
								case (byte)RSPCmd.NOOP:
								case (byte)RDPCmd.NOOP:
									skipCmd = true; // should be obvious why
									break;
								case 0x03: { // G_MOVEMEM
									uint lightPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;

									if (!tryMapPtr(lightPtr, out uint val)) {
										throw new Exception($"G_MOVEMEM with unmapped ptr {lightPtr:X2}??");
									}

									WriteU32(newCmdBuffer, 4, val | 0x0E000000);
								}
								break;
								case (byte)RSPCmd.Vertex: {
									//thefunnytol = Random.Shared.NextDouble() < 0.8;
									skipCmd = thefunnytol;

									if (!skipCmd) {
										uint vertexPtr = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
										vertexPtr += (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));

										if (!tryMapPtr(vertexPtr, out uint val)) {
											throw new Exception($"gsSPVertex with unmapped ptr {vertexPtr:X2}??");
										}

										val -= (uint)(0x10 * (ReadU8(cmdBuffer, 1) & 0xF));
										//if (verboseDebug)
										//    Console.WriteLine($"mapping {vertexPtr | 0x0E000000:X8} -> {val | 0x0E000000:X8}");
										WriteU32(newCmdBuffer, 4, val | 0x0E000000);
									}
								}
								break;
								case (byte)RSPCmd.DisplayList:
									throw new Exception("Found gsSPDisplayList. Whoopsies, branching display lists are yet to be handled!");
								case (byte)RSPCmd.EndDisplayList:
									printAndAddDL(cmdBuffer);
									goto dlEnd2;
								case (byte)RDPCmd.SetEnvColor:
									envColorA = ReadU8(cmdBuffer, 7);
									currentTex = 0;
									thefunnytol = envColorA <= 1;
									break;
								case (byte)RDPCmd.SetTextureImage: {
									texType = (byte)(ReadU8(cmdBuffer, 1) >> 3);
									uint textureImage = ReadU32(cmdBuffer, 4) & 0xFFFFFF;
									currentTex = textureImage;
									//uint textureID = GetTextureID(textureImage, texType);

									thefunnytol = envColorA <= 1;
									if (areaPainting64Cfg != null) {
										thefunnytol = textureImage == (areaPainting64Cfg.baseSegmentedAddress & 0xFFFFFF) ||
										(areaPainting64Cfg.textureSegmentedAddresses.Any(addr => textureImage == (addr & 0xFFFFFF)) &&
										!areaPainting64Cfg.textureSegmentedAddresses_NoPurge.Any(addr => textureImage == (addr & 0xFFFFFF)));
									}

									if (!thefunnytol) {
										if (!tryMapPtr(textureImage, out uint val)) {
											throw new Exception($"gsDPSetTextureImage with unmapped ptr {textureImage:X2}??");
										}
										WriteU32(newCmdBuffer, 4, (val & 0x00FFFFFF) | 0x0E000000);
										skipCmd = false;
									}
									else {
										skipCmd = true;
									}

									//if (!newTextureCmds.ContainsKey(val))
									//    newTextureCmds.Add(val, []);
									//Console.WriteLine($"Entering texture {val:X8}");
									//currentList = newTextureCmds[val];

									//WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
									/* TODO: CI4 conversion
									*                               if (texType == ((2 << 2) | 0)) {
									*                                   Console.WriteLine("Conversion to CI4 in effect!");
									*                                   Console.WriteLine($"{string.Join("", cmdBuffer.Select(b => b.ToString("X2")))}");
									*                                   Console.WriteLine($"{string.Join("", newCmdBuffer.Select(b => b.ToString("X2")))}");
								}*/
								}
								break;
								case (byte)RSPCmd.Tri1:
									skipCmd = thefunnytol;
									break;

								case (byte)RSPCmd.SetGeometryMode:
									skipCmd = !RcpSetState("SPGeometryMode", 0xFFFFFFFF, ReadU32(cmdBuffer, 4));
									break;
								case (byte)RSPCmd.ClearGeometryMode:
									skipCmd = !RcpSetState("SPGeometryMode", 0x00000000, ReadU32(cmdBuffer, 4));
									break;
								case (byte)RSPCmd.SetOtherModeH:
									skipCmd = !RcpSetState("SPOtherModeH", ReadU32(cmdBuffer, 4), (uint)((1 << cmdBuffer[2]) - 1) << cmdBuffer[3]);
									break;

								case (byte)RDPCmd.FullSync:
								case (byte)RDPCmd.LoadSync:
								case (byte)RDPCmd.PipeSync:
								case (byte)RDPCmd.TileSync:
								case (byte)RDPCmd.LoadBlock: // (contains texture size)
									//skipCmd = currentListContainsCmd((byte)RDPCmd.LoadBlock);
									skipCmd = thefunnytol;
									break;

								case (byte)RDPCmd.SetTileSize:
									skipCmd = !RcpSetState("DPTileSize", ((ulong)(ReadU32(cmdBuffer, 0) & 0xFFFFFF) << 32) | ReadU32(cmdBuffer, 4), 0x00FFFFFFFFFFFFFF);
									break;
								case (byte)RDPCmd.SetTile:
									/* TODO: CI4 conversion
									*                               WriteU8(newCmdBuffer, 1, (byte)((texType << 3) | (ReadU8(cmdBuffer, 1) & 7)));
									*                               if (texType == ((2 << 2) | 0)) {
									*                                   WriteU8(newCmdBuffer, 2, (byte)(ReadU8(cmdBuffer, 2) / 2));
							}*/
									if (clamp.TryGetValue(currentTex, out (bool u, bool v) clampma)) {
										if (clampma.u) {
											cmdBuffer[6] |= 0x2;
											newCmdBuffer[6] |= 0x2;
										}
										if (clampma.v) {
											cmdBuffer[5] |= 0x2 << 2;
											newCmdBuffer[5] |= 0x2 << 2;
										}
									}

									skipCmd = !RcpSetState("DPTile", ((ulong)(ReadU32(cmdBuffer, 0) & 0xFFFFFF) << 32) | ReadU32(cmdBuffer, 4), 0x00FFFFFFFFFFFFFF);
									break;

								default:
									//    Console.WriteLine($"Unknown DL command {cmdBuffer[0]:X2}, may alter state. Flushing texture buffers now!");
									//    flushTextureCmds();
									break;
							}
						}

						if (!skipCmd) {
							printAndAddDL(newCmdBuffer);
						}
						else if (verboseDebug) {
							Console.WriteLine();
						}
					}

					dlEnd2:
					Console.ResetColor();
					continue;
				}

				// Check these moving texture objects
				for (int i = 0; i < area.ScrollingTextures.Count; i++) {
					ManagedScrollingTexture scrollingTexture = area.ScrollingTextures[i];

					//Console.WriteLine($"Texture scroll object - points to {scrollingTexture.VertexPointer:X8}");

					if (!tryMapPtr((uint)scrollingTexture.VertexPointer & 0xFFFFFF, out uint val)) {
						throw new Exception($"Texture scroll object with unmapped ptr {scrollingTexture.VertexPointer:X8}??");
					}

					scrollingTexture.VertexPointer = (int)(val | 0x0E000000);
				}

				buffer.SetLength(newBuffer.Count);
				buffer.Position = 0;
				buffer.Write(newBuffer.ToArray(), 0, newBuffer.Count);

				if (areaPainting64Cfg != null) {
					List<uint> trol = [ .. areaPainting64Cfg.textureSegmentedAddresses, areaPainting64Cfg.baseSegmentedAddress ];
					// Update Painting64 config.
					foreach (uint oldPtr in trol) {
						List<string> oldStr = [
							$"0x0E{oldPtr & 0xFFFFFF:X6}".ToLowerInvariant(),
							$"0xE{oldPtr & 0xFFFFFF:X6}".ToLowerInvariant(),
						];
						if (!tryMapPtr(oldPtr & 0xFFFFFF, out uint newPtr)) {
							throw new Exception($"Level 0x{level.LevelID:X2} area {area.AreaID}: areaPainting64Cfg contains unmapped ptr {oldPtr:X8}?? mappings:\n{string.Join("\n", oldToNewPtrMap.Select(mapping => $"{mapping.Key:X8} -> {mapping.Value:X8} len: {(newData.TryGetValue(mapping.Value, out byte[] data) ? data.Length : 0):X8}"))}");
						}
						string newStr = $"0x00E{newPtr & 0xFFFFFF:X6}";
						foreach (string _oldStr in oldStr)
							areaPainting64Cfg.config = areaPainting64Cfg.config.ToLowerInvariant().Replace(_oldStr, newStr);
					}
				}
			}
			Console.WriteLine($"sisters in christ: {brothersInChrist}");
		}
		Console.WriteLine($"We've hypothetically saved a grand total of {totalSaves/1024} KiB!");

		StreamWriter sw = new("paintingcfg.txt");
		foreach (KeyValuePair<(byte, byte), AreaPaintingCfg> kvp in painting64Cfg) {
			sw.WriteLine($"LEVEL_ID={kvp.Key.Item1}");
			sw.WriteLine(kvp.Value.config);
		}
		sw.Close();
	}


	public static void EvaporateFast3D(RomManager manger, string painting64Path, Dictionary<(byte, byte), AreaPaintingCfg> painting64Cfg) {
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
				buffer.SetLength(0);
			}
		}
	}
}
