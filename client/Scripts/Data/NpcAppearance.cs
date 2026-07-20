#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Minimal NPC appearance table (Body/Head/Heading per NPC number), parsed from
/// NPCs.dat. Used only by the login backdrop; the gameplay client gets NPC
/// appearance from the server at runtime.
/// </summary>
public readonly struct NpcAppearance
{
	public readonly int Body;
	public readonly int Head;
	public readonly int Heading;

	public NpcAppearance(int body, int head, int heading)
	{
		Body = body;
		Head = head;
		Heading = heading;
	}

	/// <summary>
	/// Load NPCs.dat and return a map of NPC number to appearance. Returns an
	/// empty map if the file is missing or unreadable.
	/// </summary>
	public static Dictionary<int, NpcAppearance> LoadTable(IResourceProvider resources)
	{
		var result = new Dictionary<int, NpcAppearance>();

		if (!TryReadNpcDat(resources, out var bytes, out var source))
		{
			GD.PrintErr("[NPC-APPEARANCE] NPCs.dat not found in resources, client/Data/INIT, resources/data/INIT or server/dat; no backdrop NPCs.");
			return result;
		}

		string text = DecodeDat(bytes);

		int currentNpc = 0;
		int body = 0, head = 0, heading = 3;
		bool inSection = false;

		void Flush()
		{
			if (inSection && currentNpc > 0)
				result[currentNpc] = new NpcAppearance(body, head, heading);
		}

		foreach (var rawLine in text.Split('\n'))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line[0] == '\'') continue;

			if (line[0] == '[')
			{
				Flush();
				int end = line.IndexOf(']');
				string tag = end > 1 ? line.Substring(1, end - 1) : "";
				currentNpc = tag.StartsWith("NPC", StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(tag.Substring(3), out var n)
						? n
						: 0;
				inSection = currentNpc > 0;
				body = 0;
				head = 0;
				heading = 3;
				continue;
			}

			if (!inSection) continue;

			int eq = line.IndexOf('=');
			if (eq <= 0) continue;

			string key = line.Substring(0, eq).Trim();
			string val = line.Substring(eq + 1).Trim();
			int comment = val.IndexOf('\'');
			if (comment >= 0) val = val.Substring(0, comment).Trim();

			if (key.Equals("Body", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out body);
			else if (key.Equals("Head", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out head);
			else if (key.Equals("Heading", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out heading);
		}
		Flush();

		GD.Print($"[NPC-APPEARANCE] Loaded {result.Count} NPC appearances from {source}.");
		return result;
	}

	private static bool TryReadNpcDat(IResourceProvider resources, out byte[] bytes, out string source)
	{
		const string relativePath = "INIT/NPCs.dat";
		if (resources.Exists(relativePath))
		{
			bytes = resources.ReadBytes(relativePath);
			source = relativePath;
			return true;
		}

		foreach (var path in LooseNpcDatCandidates(resources.BasePath))
		{
			if (!File.Exists(path)) continue;
			bytes = File.ReadAllBytes(path);
			source = path;
			return true;
		}

		bytes = Array.Empty<byte>();
		source = "";
		return false;
	}

	private static IEnumerable<string> LooseNpcDatCandidates(string basePath)
	{
		foreach (var candidate in DataRelativeCandidates(basePath))
			yield return candidate;

		string projectPath = ProjectSettings.GlobalizePath("res://");
		foreach (var candidate in DataRelativeCandidates(projectPath))
			yield return candidate;

		var dir = new DirectoryInfo(Path.GetFullPath(projectPath));
		for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
		{
			yield return Path.Combine(dir.FullName, "client", "Data", "INIT", "NPCs.dat");
			yield return Path.Combine(dir.FullName, "resources", "data", "INIT", "NPCs.dat");
			yield return Path.Combine(dir.FullName, "server", "dat", "NPCs.dat");
		}
	}

	private static IEnumerable<string> DataRelativeCandidates(string basePath)
	{
		if (string.IsNullOrWhiteSpace(basePath)) yield break;

		string full = Path.GetFullPath(basePath);
		yield return Path.Combine(full, "INIT", "NPCs.dat");
		yield return Path.Combine(full, "Data", "INIT", "NPCs.dat");
	}

	private static string DecodeDat(byte[] bytes)
	{
		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

		return Encoding.Latin1.GetString(bytes);
	}
}
