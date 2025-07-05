using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SM64Lib;
using SM64Lib.Levels;

// 🟥
// I would name the class that but C# doesn't support that
// shame.
static class Simplicity {
	// OS simplicity
	// Ensures a file is gone. If file doesn't exist then we don't throw a tantrum about it.
	public static void rm_f(string path) {
		if (File.Exists(path)) {
			File.Delete(path);
		}
	}

	public static void RunProcess(string process) {
		string[] split = process.Split(' ');
		string name = split[0];
		if (name.StartsWith("\"")) {
			int i = 1;
			while (i < split.Length && !name.EndsWith("\"")) {
				name += $" {split[i++]}";
			}
		}
		string args = process[(name.Length + 1)..];
		if (name.StartsWith("\"")) {
			name = name.Trim('"');
		}

		Process p = Process.Start(new ProcessStartInfo(name, args) {
			CreateNoWindow = true,
			UseShellExecute = false,
		});

		if (p == null) {
			Console.WriteLine($"Failed to run {process}");
			return;
		}
		p.WaitForExit();
		p.Close();
	}


	// rom mangler simplicity
	public static string GetLevelName(RomManager manger, Level level) {
		string name = ((RMLevel)level).Config.LevelName;

		if (string.IsNullOrWhiteSpace(name)) {
			name = manger.LevelInfoData.First(_level => _level.ID == level.LevelID).Name;
		}

		return name;
	}

	public static string GetAreaName(RomManager manger, Level level, byte areaID) {
		string name = ((RMLevel)level).Config.GetLevelAreaConfig(areaID).AreaName;

		return name;
	}


	// globma simplicity
	public static bool IsWildcardMatch(string wildcardPattern, string subject) {
		if (string.IsNullOrWhiteSpace(wildcardPattern))
			return false;

		string regexPattern = string.Concat("^", Regex.Escape(wildcardPattern).Replace("\\*", ".*"), "$");

		int wildcardCount = wildcardPattern.Count(x => x.Equals('*'));
		if (wildcardCount <= 0)
			return subject.Equals(wildcardPattern, StringComparison.CurrentCultureIgnoreCase);
		else if (wildcardCount == 1) {
			string newWildcardPattern = wildcardPattern.Replace("*", "");

			if (wildcardPattern.StartsWith("*")) {
				return subject.EndsWith(newWildcardPattern, StringComparison.CurrentCultureIgnoreCase);
			}
			else if (wildcardPattern.EndsWith("*")) {
				return subject.StartsWith(newWildcardPattern, StringComparison.CurrentCultureIgnoreCase);
			}
			else {
				try {
					return Regex.IsMatch(subject, regexPattern);
				}
				catch {
					return false;
				}
			}
		}
		else {
			try {
				return Regex.IsMatch(subject, regexPattern);
			}
			catch {
				return false;
			}
		}
	}



	// math simplicity
	public static bool IsTriDegenerate(Vtx_tn v1, Vtx_tn v2, Vtx_tn v3) {
		long ax = v2.x - v1.x, ay = v2.y - v1.y, az = v2.z - v1.z;
		long bx = v3.x - v1.x, by = v3.y - v1.y, bz = v3.z - v1.z;

		// Cross product
		long cx = ay * bz - az * by;
		long cy = az * bx - ax * bz;
		long cz = ax * by - ay * bx;

		// Degenerate if the cross product is zero
		return cx == 0 && cy == 0 && cz == 0;
	}

	public static double TriArea(Vtx_tn v1, Vtx_tn v2, Vtx_tn v3) {
		// Edge vectors
		long ax = v2.x - v1.x, ay = v2.y - v1.y, az = v2.z - v1.z;
		long bx = v3.x - v1.x, by = v3.y - v1.y, bz = v3.z - v1.z;

		// Cross product
		long cx = ay * bz - az * by;
		long cy = az * bx - ax * bz;
		long cz = ax * by - ay * bx;

		// Magnitude of the cross product
		double crossMagnitude = Math.Sqrt(cx * cx + cy * cy + cz * cz);

		// Area of the triangle
		return crossMagnitude / 2.0;
	}

	public static double Lerp(double a, double b, double t) {
		return a * (1.0 - t) + b * t;
	}
	public static double InverseLerp(double a, double b, double value) {
		return (value - a) / (b - a);
	}

	public static double Remap(double value, double oldMin, double oldMax, double newMin, double newMax) {
		double t = InverseLerp(oldMin, oldMax, value);
		return Lerp(newMin, newMax, t);
	}

	public static double uvmod(double a, double b) {
		return (a + 65536) % b;
	}

	public static bool TryParseUIntma(string input, out uint value) {
		if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
			return uint.TryParse(input[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
		}

		return uint.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}
}
