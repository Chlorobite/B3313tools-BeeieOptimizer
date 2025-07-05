using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using SM64Lib;
using SM64Lib.Levels;

using static Simplicity;

struct AreaMatchPattern {
	public int? LevelID;
	public int? AreaID;
	public string AreaName;

	public bool Match(RomManager manger, Level level, LevelArea area) {
		if (LevelID.HasValue && level.LevelID != LevelID) return false;
		if (AreaID.HasValue && area.AreaID != AreaID) return false;

		if (AreaName != null) {
			string areaName = GetAreaName(manger, level, area.AreaID);
			if (!IsWildcardMatch(AreaName, areaName)) {
				return false;
			}
		}

		return true;
	}

	public bool IsEmptyPattern() {
		return LevelID == null && AreaID == null && AreaName == null;
	}

	public override string ToString()
	{
		return $"{{LevelID: {LevelID}, AreaID: {AreaID}, AreaName: {AreaName}, Empty: {IsEmptyPattern()}}}";
	}
}

class Config {
	public List<AreaMatchPattern> CollisionIgnoreAreas { get; init; } = [];
	public List<AreaMatchPattern> VerboseDebugAreas { get; init; } = [];
	public List<AreaMatchPattern> Fast3DUVPatchBlacklist { get; init; } = [];
	public List<AreaMatchPattern> TextureDumpList { get; init; } = [];
	public List<string> TextureCutoutHashBlacklist { get; init; } = [];

	public Config() { }

	public Config(string filePath) {
		if (!File.Exists(filePath)) {
			File.WriteAllText(filePath, """
			{
				"CollisionIgnoreAreas": [],
				"VerboseDebugAreas": [],
				"Fast3DUVPatchBlacklist": [],
				"TextureDumpList": [],
				"TextureCutoutHashBlacklist": [],
			}
			""");
		}

		string json = File.ReadAllText(filePath);
		var options = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
			IncludeFields = true,
		};

		// best solutione
		Config loaded = JsonSerializer.Deserialize<Config>(json, options);
		if (loaded == null) {
			throw new Exception("failed to read jsone or like idk what happened");
		}

		CollisionIgnoreAreas.AddRange((loaded.CollisionIgnoreAreas ?? []).Where(ptn => !ptn.IsEmptyPattern()));
		VerboseDebugAreas.AddRange((loaded.VerboseDebugAreas ?? []).Where(ptn => !ptn.IsEmptyPattern()));
		Fast3DUVPatchBlacklist.AddRange((loaded.Fast3DUVPatchBlacklist ?? []).Where(ptn => !ptn.IsEmptyPattern()));
		TextureDumpList.AddRange((loaded.TextureDumpList ?? []).Where(ptn => !ptn.IsEmptyPattern()));
		TextureCutoutHashBlacklist.AddRange(loaded.TextureCutoutHashBlacklist ?? []);
		/*
		Console.WriteLine("confige:");
		Console.WriteLine("(before trim)");
		Console.WriteLine($"\tCollisionIgnorePatterns: [{string.Join(',', loaded.CollisionIgnorePatterns)}]");
		Console.WriteLine($"\tFast3DVerboseDebugPatterns: [{string.Join(',', loaded.Fast3DVerboseDebugPatterns)}]");
		Console.WriteLine($"\tFast3DUVPatchBlacklist: [{string.Join(',', loaded.Fast3DUVPatchBlacklist)}]");
		Console.WriteLine($"\tTextureDumpList: [{string.Join(',', loaded.TextureDumpList)}]");
		Console.WriteLine($"\tTextureCutoutHashBlacklist: [{string.Join(',', loaded.TextureCutoutHashBlacklist)}]");
		Console.WriteLine("(after trim)");
		Console.WriteLine($"\tCollisionIgnorePatterns: [{string.Join(',', CollisionIgnorePatterns)}]");
		Console.WriteLine($"\tFast3DVerboseDebugPatterns: [{string.Join(',', Fast3DVerboseDebugPatterns)}]");
		Console.WriteLine($"\tFast3DUVPatchBlacklist: [{string.Join(',', Fast3DUVPatchBlacklist)}]");
		Console.WriteLine($"\tTextureDumpList: [{string.Join(',', TextureDumpList)}]");
		Console.WriteLine($"\tTextureCutoutHashBlacklist: [{string.Join(',', TextureCutoutHashBlacklist)}]");
		*/
	}
}
