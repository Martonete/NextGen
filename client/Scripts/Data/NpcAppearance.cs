#nullable enable
using System.Collections.Generic;
using System.Text;
using Godot;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.Data;

/// <summary>
/// Minimal NPC appearance table (Body/Head/Heading per NPC number), parsed from the
/// server's NPCs.dat INI. Used only by the login backdrop to draw a map's NPCs — the
/// gameplay client gets NPC appearance from the server at runtime, not from this file.
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
	/// Load NPCs.dat and return a map of NPC number → appearance. Returns an empty
	/// map (never throws) if the file is missing or unreadable — callers treat that
	/// as "no NPCs to draw".
	///
	/// NPCs.dat is not part of the .aopak archives, so it's read as a loose file from
	/// the data directory (BasePath/INIT/NPCs.dat), which is where it's shipped.
	/// </summary>
	public static Dictionary<int, NpcAppearance> LoadTable(IResourceProvider resources)
	{
		var result = new Dictionary<int, NpcAppearance>();

		string path = resources.BasePath + "/INIT/NPCs.dat";
		if (!Godot.FileAccess.FileExists(path))
		{
			GD.PrintErr($"[NPC-APPEARANCE] NPCs.dat not found at {path} — no backdrop NPCs.");
			return result;
		}

		byte[] bytes;
		using (var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
		{
			if (f == null) return result;
			bytes = f.GetBuffer((long)f.GetLength());
		}

		// NPCs.dat is Latin-1 (VB6 era), matching the rest of the .dat files.
		string text = Encoding.Latin1.GetString(bytes);

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
			if (line.Length == 0 || line[0] == '\'') continue; // blank / VB6 comment

			if (line[0] == '[')
			{
				Flush();
				int end = line.IndexOf(']');
				string tag = end > 1 ? line.Substring(1, end - 1) : "";
				// Sections look like [NPC123]; ignore anything else.
				currentNpc = tag.StartsWith("NPC") && int.TryParse(tag.Substring(3), out var n) ? n : 0;
				inSection = currentNpc > 0;
				body = 0; head = 0; heading = 3;
				continue;
			}

			if (!inSection) continue;

			int eq = line.IndexOf('=');
			if (eq <= 0) continue;
			string key = line.Substring(0, eq).Trim();
			string val = line.Substring(eq + 1).Trim();
			// Strip trailing inline comments (VB6: value 'comment).
			int q = val.IndexOf('\'');
			if (q >= 0) val = val.Substring(0, q).Trim();

			if (key.Equals("Body", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out body);
			else if (key.Equals("Head", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out head);
			else if (key.Equals("Heading", System.StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out heading);
		}
		Flush();

		return result;
	}
}
