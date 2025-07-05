using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SM64Lib;
using SM64Lib.Geolayout;
using SM64Lib.Levels;
using SM64Lib.Levels.Script;
using SM64Lib.Levels.Script.Commands;
using SM64Lib.Model.Fast3D;

using static Simplicity;


//HashSet<string> noCollidema = ["Haunted Mansion"];
Config configma = new("BeeieOptimizer.json");



void PrintRomSize(RomManager manger, string context) {
    RomSpaceInfo info = manger.GetRomSpaceInfo();
    Console.WriteLine($"ROM size {context}: {((0x1210000 + info.TotalUsedSpace) / 1024) / 1024.0} MiB");
}

void SaveAndPrintRomSize(RomManager manger, string context) {
    manger.SaveRom(true, true, RecalcChecksumBehavior.Never);
    PrintRomSize(manger, context);
}





Dictionary<int, Dictionary<byte, (byte destAreaID, byte destLevelID, byte destWarpID)>> ExtractWarps(RomManager manger) {
    Dictionary<int, Dictionary<byte, (byte destAreaID, byte destLevelID, byte destWarpID)>> result = [];

    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            Dictionary<byte, (byte destAreaID, byte destLevelID, byte destWarpID)> extractedWarps = [];
            var immaculateVisione = new List<List<LevelscriptCommand>> { area.Warps, area.WarpsForGame };

            foreach (IEnumerable<LevelscriptCommand> list in immaculateVisione) {
                foreach (LevelscriptCommand warp in list) {
                    byte warpId = clWarp.GetWarpID(warp);
                    byte destAreaID = clWarp.GetDestinationAreaID(warp);
                    byte destLevelID = (byte)clWarp.GetDestinationLevelID(warp); // Levels enum is sion
                    byte destWarpID = clWarp.GetDestinationWarpID(warp);

                    if (!(warpId == 0xF0 || warpId == 0xF1)) continue;
                    if (extractedWarps.ContainsKey(warpId)) {
                        byte newAreaID = extractedWarps[warpId].destAreaID;
                        byte newLevelID = extractedWarps[warpId].destLevelID;
                        byte newWarpID = extractedWarps[warpId].destWarpID;
                        bool changes = destAreaID != newAreaID || destLevelID != newLevelID || destWarpID != newWarpID;

                        if (changes) throw new("ok zro");
                    }
                    else {
                        extractedWarps.Add(warpId, (destAreaID, destLevelID, destWarpID));
                    }
                }
            }

            if (extractedWarps.Count > 0) {
                int resultKey = (level.LevelID << 16) | area.AreaID;
                if (!result.ContainsKey(resultKey)) {
                    result.Add(resultKey, []);
                }

                foreach (var kvp in extractedWarps)
                    result[resultKey].Add(kvp.Key, kvp.Value);
            }
        }
    }

    return result;
}



void PatchWarps(RomManager manger, Dictionary<int, Dictionary<byte, (byte destAreaID, byte destLevelID, byte destWarpID)>> warps) {
    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            bool anyChanges = false;
            string log = $"Level 0x{level.LevelID:X2} '{GetLevelName(manger, level)}' area {area.AreaID} '{GetAreaName(manger, level, area.AreaID)}':";

            var immaculateVisione = new List<List<LevelscriptCommand>> { area.Warps, area.WarpsForGame };
            int key = (level.LevelID << 16) | area.AreaID;
            if (!warps.ContainsKey(key)) continue;
            Dictionary<byte, (byte destAreaID, byte destLevelID, byte destWarpID)> localWarps = warps[key];

            foreach (IEnumerable<LevelscriptCommand> list in immaculateVisione) {
                foreach (LevelscriptCommand warp in list) {
                    byte warpId = clWarp.GetWarpID(warp);
                    byte destAreaID = clWarp.GetDestinationAreaID(warp);
                    byte destLevelID = (byte)clWarp.GetDestinationLevelID(warp); // Levels enum is sion
                    byte destWarpID = clWarp.GetDestinationWarpID(warp);

                    if (!localWarps.ContainsKey(warpId)) continue;
                    byte newAreaID = localWarps[warpId].destAreaID;
                    byte newLevelID = localWarps[warpId].destLevelID;
                    byte newWarpID = localWarps[warpId].destWarpID;
                    bool changes = destAreaID != newAreaID || destLevelID != newLevelID || destWarpID != newWarpID;

                    if (changes) {
                        anyChanges = true;
                        Level mangleDest = manger.Levels.First(lvl => lvl.LevelID == destLevelID);
                        Level mangleNew = manger.Levels.First(lvl => lvl.LevelID == newLevelID);
                        log += $"\n\tRerouting warp 0x{warpId:X2} from level 0x{destLevelID:X2} '{GetLevelName(manger, mangleDest)}' area {destAreaID} '{GetAreaName(manger, mangleDest, destAreaID)}' warp {destWarpID}\n" +
                        $"\t\tto level 0x{newLevelID:X2} '{GetLevelName(manger, mangleNew)}' area {newAreaID} '{GetAreaName(manger, mangleNew, newAreaID)}' warp {newWarpID}";
                        clWarp.SetDestinationAreaID(warp, newAreaID);
                        clWarp.SetDestinationLevelID(warp, (Levels)newLevelID);
                        clWarp.SetDestinationWarpID(warp, newWarpID);
                    }
                }
            }
            
            if (anyChanges)
                Console.WriteLine(log);
        }
    }
}


Dictionary<int, (CameraPresets preset, bool hasCameraObject)> ExtractCameras(RomManager manger) {
    Dictionary<int, (CameraPresets preset, bool hasCameraObject)> result = [];

    foreach (Level level in manger.Levels) {
        foreach (LevelArea area in level.Areas) {
            CameraPresets preset = area.Geolayout.CameraPreset;
            bool hasCameraObject = area.Objects.Any(cmd => clNormal3DObject.GetSegBehaviorAddr(cmd) == 0x1F000500);

            int dictKey = (level.LevelID << 16) | area.AreaID;
            if (!result.ContainsKey(dictKey)) {
                result.Add(dictKey, (CameraPresets.OpenCamera, false));
            }
            result[dictKey] = (preset, hasCameraObject);
        }
    }

    return result;
}



void MIO0_Fast3D(RomManager manger) {
	const string temp_bin = $"tmp.bin";
	const string temp_mio0 = $"tmp.mio0";

	int maxMio0Size = 0;

	foreach (Level level in manger.Levels) {
		foreach (LevelArea area in level.Areas) {
			Fast3DBuffer buffer = area.AreaModel.Fast3DBuffer;
			byte[] newBuffer = new byte[buffer.Length];

			buffer.Seek(0, SeekOrigin.Begin);
			buffer.Read(newBuffer, 0, (int)buffer.Length);

			// mio0
			{
				File.WriteAllBytes(temp_bin, newBuffer);

				RunProcess($"tools/mio0 \"{temp_bin}\" \"{temp_mio0}\"");

				newBuffer = File.ReadAllBytes(temp_mio0);
				if (newBuffer.Length > maxMio0Size) {
					maxMio0Size = newBuffer.Length;
				}
			}

			buffer.SetLength(newBuffer.Length);
			buffer.Position = 0;
			foreach (byte b in newBuffer)
				buffer.WriteByte(b);
		}
	}
	Console.WriteLine($"Largest mio0 data size: {maxMio0Size/1024} KiB!");

	rm_f(temp_bin);
	rm_f(temp_mio0);
}


Dictionary<(byte, byte), AreaPaintingCfg> LoadPainting64Cfg(string path) {
	Dictionary<(byte, byte), AreaPaintingCfg> newCfg = [];

	AreaPaintingCfg current = null;
	StreamReader sr = new(path);
	byte levelID = 0;
	int linen = 0;
	try {
		uint half1 = 0;
		uint half2 = 0;
		bool optimizeIgnore = false;

		void CommitPaintingIfAny() {
			if (current != null && half1 != 0) {
				current.textureSegmentedAddresses.Add(half1);
				current.textureSegmentedAddresses.Add(half2);
				if (optimizeIgnore) {
					current.textureSegmentedAddresses_NoPurge.Add(half1);
					current.textureSegmentedAddresses_NoPurge.Add(half2);
				}
			}
			half1 = 0;
			half2 = 0;
			optimizeIgnore = false;
		}

		while (true) {
			if (sr.EndOfStream) {
				CommitPaintingIfAny();
				break;
			}

			string ln = sr.ReadLine();
			linen++;

			string key = ln.Split('=')[0].ToLowerInvariant();
			if (key == "new_painting") {
				if (current != null)
					current.paintingCount++;
				CommitPaintingIfAny();
			}
			else if (ln.Length > key.Length + 1) {
				string strValue = ln.Substring(key.Length + 1);
				string value = strValue;

				if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
					try {
						ulong u = Convert.ToUInt64(value.Substring(2), 16);
						value = u.ToString();
					}
					catch { }
				}

				switch (key) {
					case "level_id":
						CommitPaintingIfAny();

						current = null;
						levelID = byte.Parse(value);
						break;
					case "area_id":
						CommitPaintingIfAny();

						byte areaID = byte.Parse(value);
						if (!newCfg.ContainsKey((levelID, areaID))) {
							newCfg.Add((levelID, areaID), new AreaPaintingCfg());
						}
						current = newCfg[(levelID, areaID)];
						break;
					case "base_rom_address":
						throw new Exception("base_rom_address in Painting64 config not supported!");
					case "base_segmented_address":
						current.baseSegmentedAddress = uint.Parse(value);
						break;
					case "rom_address":
						throw new Exception("painting rom_address in Painting64 config not supported!");
					case "segmented_address":
						throw new Exception("painting segmented_address in Painting64 config not supported!");
					case "texture_segmented_address":
						half1 = uint.Parse(value);
						half2 = half1 + 0x1000;
						break;
					case "texture_segmented_address_half2":
						half2 = uint.Parse(value);
						break;
					case "optimize_ignore":
						optimizeIgnore = true;
						break;
				}
			}

			if (current != null) {
				current.config += ln + "\n";
			}
		}

		sr.Close();
		return newCfg;
	}
	catch (Exception e) {
		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine($"Exception at line {linen} in paintingcfg.txt:\n\n{e}");
		sr.Close();
		Console.ReadKey(true);
		return null;
	}
}


/*RomManager manger = new("b3313 silved.z64");
manger.BoxSystemA3Mode = true;
manger.LoadRom();
Console.WriteLine(manger.GameName);


Console.WriteLine($"there are {manger.Levels.Length} levels");

foreach (var level in manger.Levels)
{
    Console.WriteLine($"level {level.LevelID} has areas:");
    foreach (var area in level.Areas)
    {
        Console.WriteLine($"area {area.AreaID} has warps: ");
        Console.WriteLine(area.Warps);
    }
}*/


/// from gpt:
/// Specifically: on Linux, Environment.GetCommandLineArgs()[0] is silently rewritten to the full absolute path
/// when you run a published self-contained app or sometimes even a trimmed dotnet app.
string DotnetAndItsConsequences()
{
	if (OperatingSystem.IsLinux()) {
		byte[] raw = File.ReadAllBytes("/proc/self/cmdline");
		int nul = Array.IndexOf(raw, (byte)0);
		return System.Text.Encoding.UTF8.GetString(raw, 0, nul);
	}

	return Environment.GetCommandLineArgs()[0];
}


string[] split = Environment.CommandLine.Split(' ');
string cmdLineName = DotnetAndItsConsequences();

string usage() {
	return string.Join(Environment.NewLine, [
		$"Usage: {cmdLineName} <path to ROM> <command> <command args...>",
		$"Commands:",
		$"\trun <path to Painting64> <path to paintingcfg.txt>",
		$"\tsearch_obj <object bhv address>",
		$"\tsearch_warps_to <area selector JSON>",
		"",
		$"Texture utils:",
		$"\tdump <target directory>",
		$"\treplace <target directory>",
		"NOTE: Texture dumping requires populating TextureDumpList in the BeeieOptimizer.json config file!",
		"To dump everything, simply set \"TextureDumpList\": [{\"AreaName\": \"*\"}]",
		"",
		$"Leftover A2AE utils (requires specific rom names):",
					   $"\tpatch_warps",
					$"\tverify_cameraballs",
	]);
}

Console.WriteLine("The Great Bee Optimizator Unabandoned Versione A0");
if (args.Length < 2) {
	Console.WriteLine(usage());
	return;
}

switch (args[1].ToLowerInvariant()) {
	// TEMPORARY and UNDOCUMENTED for a reason
	case "mio0_stage": {
		RomManager manger = new(args[0]);
		manger.BoxSystemA3Mode = true;
		manger.LoadRom();
		MIO0_Fast3D(manger);
		SaveAndPrintRomSize(manger, "post Fast3D mio0 compression");
		break;
	}

	default: {
		Console.WriteLine(usage());
		break;
	}

	case "run": {
		Dictionary<(byte, byte), AreaPaintingCfg> painting64Cfg = LoadPainting64Cfg(args[3]);
		Console.WriteLine($"Loaded {painting64Cfg.Sum(kvp => kvp.Value.paintingCount)} paintings from Painting64 config!");

		RomManager manger = new(args[0]);
		manger.BoxSystemA3Mode = true;
		string painting64Path = args[2];
		Console.WriteLine("a manger?");
		manger.LoadRom();
		PrintRomSize(manger, "pre compression");

		Stages.OptimizeCollision(manger, configma);
		SaveAndPrintRomSize(manger, "post collision optimization");
		//EvaporateCollision(manger);
		//SaveAndPrintRomSize(manger, "post collision evaporation");

		Stages.OptimizeFast3D(manger, configma, painting64Path, painting64Cfg);
		SaveAndPrintRomSize(manger, "post Fast3D optimization");
		//EvaporateFast3D(manger, painting64Path, painting64Cfg);
		//SaveAndPrintRomSize(manger, "post Fast3D evaporation");

		Stages.OptimizeObjects(manger, configma);
		//EvaporateObjects(manger);
		SaveAndPrintRomSize(manger, "post object purge");

		if (File.Exists("paintingcfg.txt")) {
			Console.WriteLine($"Applying new Painting64 cfg to rom...");
			string romPath = manger.RomFile;
			manger = null;
			RunProcess($"\"{painting64Path}\" \"{romPath}\" --automatic");
			rm_f("paintingcfg.txt");
		}

		// HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK HACK!!!!!!!!!!!!!
		// calling self for mio0, because it refuses to reload painting64 changes otherwise is incredibly cursed
		RunProcess($"{cmdLineName} \"{args[0]}\" MIO0_STAGE");
		break;
	}
	case "dump": {
		string dumpPath = args[2];

		if (!Directory.Exists(dumpPath)) {
			Directory.CreateDirectory(dumpPath);
		}

		RomManager manger = new(args[0]);
		manger.BoxSystemA3Mode = true;
		Console.WriteLine("a manger?");
		manger.LoadRom();

		Console.WriteLine("what's dumpma?");
		Stages.DumpTextures(manger, configma, dumpPath);

		break;
	}
	case "replace": {
		string dumpPath = args[2];

		if (!Directory.Exists(dumpPath)) {
			Directory.CreateDirectory(dumpPath);
		}

		RomManager manger = new(args[0]);
		Console.WriteLine("a manger?");
		manger.BoxSystemA3Mode = true;
		manger.LoadRom();

		Console.WriteLine("what's replacema?");
		Stages.ReplaceTextures(manger, configma, dumpPath);

		SaveAndPrintRomSize(manger, "post texture replace");
		break;
	}
	case "search_obj": {
		if (TryParseUIntma(args[2], out uint bhv)) {
			RomManager manger = new(args[0]);
			Console.WriteLine("a manger?");
			manger.BoxSystemA3Mode = true;
			manger.LoadRom();

			Console.WriteLine("what's findma?");
			Stages.FindObjects(manger, configma, bhv);
		}
		else {
			Console.WriteLine($"L, {args[2]} is NOT an address");
		}
		break;
	}
	case "search_warps_to": {
		var options = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
			IncludeFields = true,
		};

		// best solutione
		AreaMatchPattern ptn = JsonSerializer.Deserialize<AreaMatchPattern>(args[2], options);
		if (ptn.IsEmptyPattern()) {
			throw new Exception("failed to read jsone or like idk what happened");
		}

		RomManager manger = new(args[0]);
		manger.BoxSystemA3Mode = true;
		Console.WriteLine("a manger?");
		manger.LoadRom();

		Console.WriteLine("what's findma?");
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				var immaculateVisione = new List<List<LevelscriptCommand>> { area.Warps, area.WarpsForGame };

				foreach (IEnumerable<LevelscriptCommand> list in immaculateVisione) {
					foreach (LevelscriptCommand warp in list) {
						byte warpId = clWarp.GetWarpID(warp);
						byte destAreaID = clWarp.GetDestinationAreaID(warp);
						byte destLevelID = (byte)clWarp.GetDestinationLevelID(warp); // Levels enum is sion
						byte destWarpID = clWarp.GetDestinationWarpID(warp);

						Level destLevel = manger.Levels.Where(lvl => lvl.LevelID == destLevelID).FirstOrDefault();
						if (destLevel == null) continue;
						LevelArea destArea = destLevel.Areas.Where(area => area.AreaID == destAreaID).FirstOrDefault();
						if (destArea == null) continue;

						if (ptn.Match(manger, destLevel, destArea)) {
							Console.WriteLine($"Found warp:\n\tfrom level 0x{level.LevelID:X2} '{GetLevelName(manger, level)}' area {area.AreaID} '{GetAreaName(manger, level, area.AreaID)}' warp {warpId}" +
							$"\n\tto level 0x{destLevel.LevelID:X2} '{GetLevelName(manger, destLevel)}' area {destArea.AreaID} '{GetAreaName(manger, destLevel, destArea.AreaID)}' warp {destWarpID}");
						}
					}
				}
			}
		}
		break;
	}

	case "the_great_area_randomizator": {
		RomManager manger = new(args[0]);
		manger.BoxSystemA3Mode = true;
		Console.WriteLine("a manger?");
		manger.LoadRom();

		List<string> stagema = [];
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				stagema.Add(GetAreaName(manger, level, area.AreaID));
			}
		}

		string[] stageballs = stagema.ToArray();
		Random.Shared.Shuffle(stageballs);

		Console.WriteLine("area list:");
		foreach (string stage in stageballs) {
			Console.WriteLine(stage);
		}
		break;
	}

	// a2ae development leftovers that may be useful
	case "patch_warps": {
		RomManager manger = new("b3313 a3.z64");
		manger.BoxSystemA3Mode = true;
		manger.LoadRom();

		var warps = ExtractWarps(manger);
		manger = null;
		RomManager manger2 = new("b3313 a2.z64");
		manger2.LoadRom();
		PatchWarps(manger2, warps);
		SaveAndPrintRomSize(manger2, "patchma?");
		break;
	}
	case "verify_cameraballs": {
		RomManager manger2 = new("b3313 ref.z64");
		manger2.BoxSystemA3Mode = true;
		manger2.LoadRom();
		var camerasRef = ExtractCameras(manger2);
		manger2 = null;

		RomManager manger = new("b3313 silved.z64");
		manger.BoxSystemA3Mode = true;
		manger.LoadRom();
		var cameras = ExtractCameras(manger);

		HashSet<int> keys = new(cameras.Keys);
		keys.IntersectWith(camerasRef.Keys);

		Console.WriteLine("mangled to 0x01 (Open Camera):");
		foreach (int i in keys) {
			CameraPresets prev = camerasRef[i].preset;
			CameraPresets next = cameras[i].preset;
			ushort levelID = (ushort)((i >> 16) & 0xFFFF);
			byte areaID = (byte)((i >> 0) & 0xFF);

			if (prev != next) {
				if (next == CameraPresets.OpenCamera) {
					Level level = manger.Levels.First(l => l.LevelID == levelID);

					Console.WriteLine($"\tlevel 0x{levelID:X2} '{GetLevelName(manger, level)}' area {areaID} '{GetAreaName(manger, level, areaID)}'");
				}
			}
		}

		Console.WriteLine("honorable mention:");
		foreach (int i in keys) {
			CameraPresets prev = camerasRef[i].preset;
			CameraPresets next = cameras[i].preset;
			ushort levelID = (ushort)((i >> 16) & 0xFFFF);
			byte areaID = (byte)((i >> 0) & 0xFF);

			if (prev != next) {
				if (next != CameraPresets.OpenCamera) {
					Level level = manger.Levels.First(l => l.LevelID == levelID);

					Console.WriteLine($"\tlevel 0x{levelID:X2} '{GetLevelName(manger, level)}' area {areaID} '{GetAreaName(manger, level, areaID)}'");
					Console.WriteLine($"\tchanged from preset 0x{(byte)prev:X2} {prev} to 0x{(byte)next:X2} {next}");
				}
			}
		}

		Console.WriteLine("missing area center objects:");
		foreach (int i in keys) {
			ushort levelID = (ushort)((i >> 16) & 0xFFFF);
			byte areaID = (byte)((i >> 0) & 0xFF);

			if (cameras[i].preset == CameraPresets.OpenCamera && !cameras[i].hasCameraObject) {
				Level level = manger.Levels.First(l => l.LevelID == levelID);

				Console.WriteLine($"\tlevel 0x{levelID:X2} '{GetLevelName(manger, level)}' area {areaID} '{GetAreaName(manger, level, areaID)}'");
			}
		}
		break;
	}
}
