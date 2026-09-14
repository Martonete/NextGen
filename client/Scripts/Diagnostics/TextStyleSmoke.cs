using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.UI;

namespace ArgentumNextgen.Diagnostics;

/// <summary>
/// Draws every piece of game text at the sizes taken from ArgentumOnlineGodot and
/// saves a capture, so a missing face or a silently empty outline shows up.
/// Run: godot --path client res://test/render/TextStyleSmoke.tscn
/// </summary>
public partial class TextStyleSmoke : Node2D
{
	public override void _Ready()
	{
		RpgTheme.ResourceProvider = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
		QueueRedraw();
		CallDeferred(nameof(Capture));
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(0, 0, 640, 300), new Color(0.18f, 0.22f, 0.15f));

		var world = GameFonts.InWorld;
		var body = GameFonts.AlegreyaRegular;
		var bold = GameFonts.AlegreyaBold;
		var italic = GameFonts.AlegreyaItalic;

		Row(world, 14, 40, "Nieri <Tierras Sagradas>  — nick, 14, contorno 5", true);
		// The real dialogue path, so the missing panel and the halo are what ship.
		Rendering.CharRenderer.DrawDialogLine(this, world, 12, 24, 80,
			"Hola viajero, esto es un diálogo — 12", Colors.White);
		Row(body, 12, 120, "Consola: Has ganado 884 puntos de experiencia — Alegreya 12", false);
		Row(bold, 12, 150, "Consola en negrita — Alegreya Bold 12", false);
		Row(italic, 12, 180, "Consola en cursiva — Alegreya Italic 12", false);
		Row(body, 13, 215, "Entrada de chat — Alegreya 13", false);

		// Names carry the whole point of the outline: over pale terrain a plain
		// white glyph disappears. Draw one on sand to check it holds up.
		DrawRect(new Rect2(20, 240, 600, 44), new Color(0.90f, 0.83f, 0.66f));
		Row(world, 14, 262, "Nick sobre arena clara", true);
		Rendering.CharRenderer.DrawDialogLine(this, world, 12, 24, 280,
			"Diálogo sobre arena, sin recuadro", new Color(1f, 0.95f, 0.6f));
	}

	private void Row(Font font, int size, float y, string text, bool outlined)
	{
		var pos = new Vector2(24, y);
		if (outlined)
			DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, size, 5,
				new Color(0f, 0f, 0f, 0.5f));
		DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, Colors.White);
	}

	private async void Capture()
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		string dir = ProjectSettings.GlobalizePath("user://text-style");
		Directory.CreateDirectory(dir);
		using var image = GetViewport().GetTexture().GetImage();
		string path = Path.Combine(dir, "text-style.png");
		GD.Print(image.SavePng(path) == Error.Ok ? $"[TEXT-SMOKE] {path}" : "[TEXT-SMOKE] save failed");
		GetTree().Quit();
	}
}
