using System;
using System.Linq;
using SM64Lib;
using SM64Lib.Levels;
using SM64Lib.Levels.Script;

using static ReadWrite;
using static Simplicity;

static partial class Stages {
	public static void OptimizeObjects(RomManager manger, Config config) {
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				bool verboseDebug = config.VerboseDebugAreas.Any(ptn => ptn.Match(manger, level, area));

				if (verboseDebug)
					Console.WriteLine($"OptimizeObjects: area {level.LevelID:X2}:{area.AreaID} {area.Objects.Count}");

				for (int i = area.Objects.Count - 1; i >= 0; i--) {
					LevelscriptCommand objCmd = area.Objects[i];

					if (objCmd.CommandType == LevelscriptCommandTypes.Normal3DObject) {
						byte[] data = objCmd.ToArray();
						if (data[2] == 0) {
							if (verboseDebug)
								Console.WriteLine("Removing object (acts = 0)");
							area.Objects.RemoveAt(i);
						}

						/*
						 *                   uint bhv = ReadU32(data, 0x14);
						 *                   if (bhv == 0x1F002C00) {
						 *                       Console.WriteLine("Mirror object!");
					}
					*/
					}
				}
				if (verboseDebug)
					Console.WriteLine($"new object count: {area.Objects.Count}");
			}
		}
	}


	public static void EvaporateObjects(RomManager manger, Config config) {
		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				for (int i = area.Objects.Count - 1; i >= 0; i--) {
					LevelscriptCommand objCmd = area.Objects[i];

					if (objCmd.CommandType == LevelscriptCommandTypes.Normal3DObject) {
						area.Objects.RemoveAt(i);
					}
				}
			}
		}
	}


	public static void FindObjects(RomManager manger, Config config, uint bhvFind) {
		bool sorryNothing = true;

		foreach (Level level in manger.Levels) {
			foreach (LevelArea area in level.Areas) {
				int objCount = 0;
				for (int i = area.Objects.Count - 1; i >= 0; i--) {
					LevelscriptCommand objCmd = area.Objects[i];

					if (objCmd.CommandType == LevelscriptCommandTypes.Normal3DObject) {
						byte[] data = objCmd.ToArray();

						uint bhv = ReadU32(data, 0x14);
						if (bhv == bhvFind) {
							objCount++;
						}
					}
				}

				if (objCount > 0) {
					Console.WriteLine($"Found {objCount} in level 0x{level.LevelID:X2} '{GetLevelName(manger, level)}' area {area.AreaID} '{GetAreaName(manger, level, area.AreaID)}'");
					sorryNothing = false;
				}
			}
		}

		if (sorryNothing) {
			Console.WriteLine("SORRY NOTHING");
		}
	}
}
