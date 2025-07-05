using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SM64Lib;
using SM64Lib.Geolayout;
using SM64Lib.Levels;
using SM64Lib.Model.Fast3D;

using static ReadWrite;
using static Simplicity;

static partial class Stages {
	static uint max(uint one, uint two) {
		return one > two ? one : two;
	}

	static uint Txl2Words(uint width, uint size) {
		if (size == 0) {
			return max(1U, width / 16);
		} else {
			return max(1U, width*size / 8);
		}
	}

	static uint CalculateDXT(uint txl2words) {
		if (txl2words == 0) {
			return 1;
		} else {
			return (2048 + txl2words - 1) / txl2words;
		}
	}

	static uint ReverseDXT(uint val, uint width, uint size) {
		if (val == 0x800) {
			return 1;
		}

		uint low = 2047 / val;
		if (CalculateDXT(low) > val) {
			low++;
		}
		uint high = 2047 / (val - 1);

		if (low == high) {
			return low;
		}

		for (uint i = low; i <= high; i++) {
			if (Txl2Words(width, size) == (uint)i) {
				return i;
			}
		}

		return (low + high) / 2;
	}

	static string GetTexFileName(RomManager manger, uint crc, byte fmt, byte size) {
		string crcStr = crc.ToString("X8");
		return $"{manger.GameName}#{crcStr}#{fmt}#{size}_all";
	}

	static string GetTypeFromFmtAndSize(byte tex_fmt, byte tex_siz) {
		string tex_siz_str = (4 << tex_siz).ToString();
		switch (tex_fmt) {
			case 0:
				return $"rgba{tex_siz_str}";
			case 3:
				return $"ia{tex_siz_str}";
			case 4:
				return $"i{tex_siz_str}";
		}
		throw new Exception($"Unsupported texture type ({tex_fmt} {tex_siz}).");
	}

	public static void DumpTextures(RomManager manger, Config config, string texDirectory) {
		string temp_bin = ".tmp.bin";

		if (config.TextureDumpList.Count == 0) {
			Console.WriteLine($"fun fact: you did NOT specify any areas to dump in the BeeieOptimizer.json config file");
			Console.WriteLine("To dump everything, simply set \"TextureDumpList\": [{\"AreaName\": \"*\"}]");
			Console.WriteLine("or you can dump a specific level ID \"TextureDumpList\": [{\"LevelID\": 6}]");
			Console.WriteLine("or a level+area ID \"TextureDumpList\": [{\"LevelID\": 6, \"AreaID\": 9}]");
			return;
		}

		Console.WriteLine($"Dumping textures...");
		int texCount = 0;
		int areaCount = 0;
		foreach (Level level in manger.Levels) {
			string levelDir = Path.Join(texDirectory, $"{level.LevelID} {GetLevelName(manger, level)}");
			foreach (LevelArea area in level.Areas) {
				if (!config.TextureDumpList.Any(ptn => ptn.Match(manger, level, area))) {
					continue;
				}
				string areaDir = Path.Join(levelDir, $"{area.AreaID} {GetAreaName(manger, level, area.AreaID)}");
				if (!Directory.Exists(areaDir)) {
					Directory.CreateDirectory(areaDir);
				}
				areaCount++;

				Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;

				List<(uint pointer, int length, ushort width, ushort height, byte tex_fmt, byte tex_siz, uint bpl)> loads = [];

				foreach (Geopointer ptr in buffer.DLPointers) {
					uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
					buffer.Seek(dataptr, SeekOrigin.Begin);
					byte[] cmdBuffer = new byte[8];
					uint textureImage = 0;
					byte tex_fmt = 0;
					byte tex_siz = 0;

					while (dataptr < buffer.Length) {
						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						dataptr += 8;

						ushort width = 0;
						ushort height = 0;
						uint loadPtr = 0;
						uint dxt = 0;
						uint line = 0;
						int loadLength = 0;
						uint b_txl = 0;
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
								// (((width)*(height) + siz##_INCR) >> siz##_SHIFT)-1, CALC_DXT(width, siz##_BYTES)),
								switch (tex_siz) {
									case 2: // 16 bit
										length *= 2;
										b_txl = 2;
										break;
									case 3: // 32 bit
										length *= 4;
										b_txl = 4;
										break;
									default:
										throw new Exception("Other texture types not supported.");
								}
								dxt = (uint)((ReadU16(cmdBuffer, 6) - 1) & 0xFFF);
								width = (ushort)((((1 << 11 /*G_TX_DXT_FRAC*/) - 1) * 8) / (dxt * b_txl)); // tanks Nekotina
								height = (ushort)(length / width / b_txl); // tanks gpt

								loadLength = length;
								break;
							case (byte)RDPCmd.SetTextureImage:
								//Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
								tex_fmt = (byte)((ReadU8(cmdBuffer, 1) >> 5) & 0x7);
								tex_siz = (byte)((ReadU8(cmdBuffer, 1) >> 3) & 0x3);
								textureImage = ReadU32(cmdBuffer, 4);
								if ((textureImage & 0xFF000000) != 0x0E000000) {
									throw new Exception($"{textureImage:X8} is NOT segment 0E!");
								}
								textureImage &= 0xFFFFFF;
								//Console.WriteLine($"textureImage: {textureImage:X8}");
								break;
							case (byte)RDPCmd.SetTile:
								line = (ReadU32(cmdBuffer, 0) >> 9) & 0x1FF;
								// textureLUT << 17 (always 0)
								// texLevel << 19 (always 0)
								break;
						}

						if (loadLength > 0) {
							uint bpl = 0;

							if (dxt == 0) { // probably no rgba32 because rom mangler
								bpl = line << 3;
							} else {
								if (dxt > 1) {
									dxt = ReverseDXT(dxt, width, b_txl);
								}
								bpl = dxt << 3;
							}
							if (!loads.Contains((loadPtr, loadLength, width, height, tex_fmt, tex_siz, bpl))) {
								loads.Add((loadPtr, loadLength, width, height, tex_fmt, tex_siz, bpl));
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
						loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer + load2.length), load1.width, load1.height, load1.tex_fmt, load1.tex_siz, load1.bpl);
						loads.RemoveAt(i + 1);
						i--;
					}
				}

				// Now, fetch all the data we need.
				List<byte[]> uniqueTextures = [];
				foreach ((uint pointer, int _length, ushort width, ushort height, byte tex_fmt, byte tex_siz, uint bpl) in loads) {
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
						uint crc = TexUtils.RiceCRC32(data, (int)width, (int)height, (int)tex_siz, (int)bpl);
						string hash = GetTexFileName(manger, crc, tex_fmt, tex_siz);
						string type = GetTypeFromFmtAndSize(tex_fmt, tex_siz);
						string pngPath = Path.Join(areaDir, $"{hash}.png");

						rm_f(pngPath);
						File.WriteAllBytes(temp_bin, data);
						RunProcess($"tools/n64graphics -e \"{temp_bin}\" -g \"{pngPath}\" -w {width} -h {height} -f \"{type}\"");
						RunProcess($"tools/n64graphics -i \"{temp_bin}\" -g \"{pngPath}\" -w {width} -h {height} -f \"{type}\"");
						byte[] newdata = File.ReadAllBytes(temp_bin);
						uint newcrc = TexUtils.RiceCRC32(newdata, (int)width, (int)height, (int)tex_siz, (int)bpl);

						if (GetTexFileName(manger, newcrc, tex_fmt, tex_siz) != hash) {
							rm_f(pngPath);
							Console.ForegroundColor = ConsoleColor.Red; // epic hack!!!
							Console.WriteLine($"[Level {level.LevelID} Area {area.AreaID}] {hash} somehow not exported correctly... ({width} {height})");
							Console.ForegroundColor = ConsoleColor.White;
						} else {
							Console.WriteLine($"[Level {level.LevelID} Area {area.AreaID}] {hash} ({width} {height})");
						}
						texCount++;

						uniqueTextures.Add(data);
					}
				}
			}
		}

		rm_f(temp_bin);
		Console.WriteLine($"Dumped {texCount} textures from {areaCount} areas.");
		Console.WriteLine($"- Each texture is in its own area subdirectory.");
		Console.WriteLine($"- For the replace operation, you might want to move identical textures up the tree to affect other areas in the same level, or all areas in the ROM completely.");
		Console.WriteLine($"- To exclude a texture inside TextureCutoutHashBlacklist, you are looking for the hash in the filename: {manger.GameName}#<HASH HERE>#3#313_all");
	}

	public static void ReplaceTextures(RomManager manger, Config config, string texDirectory) {
		string temp_bin = ".tmp.bin";

		Console.WriteLine($"Replacing textures...");
		List<(string oldHash, string newHash)> replacedTextures = [];

		int texCount = 0;
		int areaCount = 0;
		foreach (Level level in manger.Levels) {
			string levelDir = Path.Join(texDirectory, $"{level.LevelID} {GetLevelName(manger, level)}");
			foreach (LevelArea area in level.Areas) {
				string areaDir = Path.Join(levelDir, $"{area.AreaID} {GetAreaName(manger, level, area.AreaID)}");
				bool areaIncremented = false;

				Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;

				void ReplaceAtPointer(uint pointer, string oldHash, string newHash) {
					byte[] newdata = File.ReadAllBytes(temp_bin);
					buffer.Position = pointer;
					foreach (byte b in newdata)
						buffer.WriteByte(b);
					Console.WriteLine($"[Level {level.LevelID} Area {area.AreaID}] {oldHash} has been replaced.");
				}

				List<(uint pointer, int length, int width, int height, byte tex_fmt, byte tex_siz, uint bpl)> loads = [];

				foreach (Geopointer ptr in buffer.DLPointers) {
					uint dataptr = (uint)(ptr.SegPointer & 0xFFFFFF);
					buffer.Seek(dataptr, SeekOrigin.Begin);
					byte[] cmdBuffer = new byte[8];
					uint textureImage = 0;
					byte tex_fmt = 0;
					byte tex_siz = 0;

					while (dataptr < buffer.Length) {
						buffer.Read(cmdBuffer, 0, cmdBuffer.Length);
						dataptr += 8;

						ushort width = 0;
						ushort height = 0;
						uint loadPtr = 0;
						uint dxt = 0;
						uint line = 0;
						int loadLength = 0;
						uint b_txl = 0;
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
								// (((width)*(height) + siz##_INCR) >> siz##_SHIFT)-1, CALC_DXT(width, siz##_BYTES)),
								switch (tex_siz) {
									case 2: // 16 bit
										length *= 2;
										b_txl = 2;
										break;
									case 3: // 32 bit
										length *= 4;
										b_txl = 4;
										break;
									default:
										throw new Exception("Other texture types not supported.");
								}
								dxt = (uint)((ReadU16(cmdBuffer, 6) - 1) & 0xFFF);
								width = (ushort)((((1 << 11 /*G_TX_DXT_FRAC*/) - 1) * 8) / (dxt * b_txl)); // tanks Nekotina
								height = (ushort)(length / width / b_txl); // tanks gpt

								loadLength = length;
								break;
							case (byte)RDPCmd.SetTextureImage:
								//Console.WriteLine($"gsDPSetTextureImage {ReadU32(cmdBuffer, 4):X8}");
								tex_fmt = (byte)((ReadU8(cmdBuffer, 1) >> 5) & 0x7);
								tex_siz = (byte)((ReadU8(cmdBuffer, 1) >> 3) & 0x3);
								textureImage = ReadU32(cmdBuffer, 4);
								if ((textureImage & 0xFF000000) != 0x0E000000) {
									throw new Exception($"{textureImage:X8} is NOT segment 0E!");
								}
								textureImage &= 0xFFFFFF;
								//Console.WriteLine($"textureImage: {textureImage:X8}");
								break;
							case (byte)RDPCmd.SetTile:
								line = (ReadU32(cmdBuffer, 0) >> 9) & 0x1FF;
								// textureLUT << 17 (always 0)
								// texLevel << 19 (always 0)
								break;
						}

						if (loadLength > 0) {
							uint bpl = 0;

							if (dxt == 0) { // probably no rgba32 because rom mangler
								bpl = line << 3;
							} else {
								if (dxt > 1) {
									dxt = ReverseDXT(dxt, width, b_txl);
								}
								bpl = dxt << 3;
							}
							if (!loads.Contains((loadPtr, loadLength, width, height, tex_fmt, tex_siz, bpl))) {
								loads.Add((loadPtr, loadLength, width, height, tex_fmt, tex_siz, bpl));
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
						loads[i] = (load1.pointer, (int)Math.Max(load1.length, load2.pointer + load2.length), load1.width, load1.height, load1.tex_fmt, load1.tex_siz, load1.bpl);
						loads.RemoveAt(i + 1);
						i--;
					}
				}

				// Now, fetch all the data we need.
				foreach ((uint pointer, int _length, int width, int height, byte tex_fmt, byte tex_siz, uint bpl) in loads) {
					int length = (_length + 0xF) & ~0xF;
					byte[] data = new byte[length];
					buffer.Seek(pointer, SeekOrigin.Begin);
					buffer.Read(data, 0, length);
					bool exists = false;
					uint crc = TexUtils.RiceCRC32(data, (int)width, (int)height, (int)tex_siz, (int)bpl);
					string hash = GetTexFileName(manger, crc, tex_fmt, tex_siz);
					string type = GetTypeFromFmtAndSize(tex_fmt, tex_siz);


					foreach ((string oldHash, string newHash) in replacedTextures) {
						if (oldHash.SequenceEqual(hash)) {
							//ReplaceAtPointer(pointer, oldHash, newHash);
							break;
						}
					}

					if (!exists) {
						string pngName = $"{hash}.png";
						// File override list from highest to lowest priority
						string[] candidates = [Path.Join(areaDir, pngName), Path.Join(levelDir, pngName), Path.Join(texDirectory, pngName)];

						foreach (string png in candidates) {
							if (File.Exists(png)) {
								RunProcess($"tools/n64graphics -i \"{temp_bin}\" -g \"{png}\" -w {width} -h {height} -f \"{type}\"");
								byte[] newdata = File.ReadAllBytes(temp_bin);
								crc = TexUtils.RiceCRC32(newdata, (int)width, (int)height, (int)tex_siz, (int)bpl);
								string newhash = GetTexFileName(manger, crc, tex_fmt, tex_siz);
								if (newhash != hash) {
									replacedTextures.Add((hash, newhash));
									ReplaceAtPointer(pointer, hash, newhash);
									texCount++;
									if (!areaIncremented) {
										areaCount++;
										areaIncremented = true;
									}
								}
								break;
							}
						}
					}
				}
			}
		}

		rm_f(temp_bin);
		Console.WriteLine($"Replaced {texCount} textures across {areaCount} areas.");
		Console.WriteLine($"Since the hashes changed, you might want to re-dump the textures next");
	}
}
