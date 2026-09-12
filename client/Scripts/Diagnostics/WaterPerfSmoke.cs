using System;
using System.IO;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.Diagnostics;

/// <summary>
/// Renders map 27 at (50,75) — the beach where the water surface fills most of
/// the screen — at 1920x1080 with vsync off, and reports the average frame time
/// plus a screenshot. Used to measure the water pass and to prove the batched
/// version still draws the same picture.
/// Run: godot --path client res://test/render/WaterPerfSmoke.tscn [-- --tag <name>]
/// </summary>
public partial class WaterPerfSmoke : Node
{
	private const int Map = 27;
	private const int UserX = 50;
	private const int UserY = 75;
	private const int Warmup = 60;
	private const int Measured = 240;

	private readonly GameData _data = new();
	private readonly GrhAnimator _animator = new();
	private readonly GameState _state = new();
	private WorldRenderer? _renderer;

	private int _frame;
	private double _accumUs;
	private ulong _lastTick;
	private bool _saved;
	private string _tag = "actual";

	public override void _Ready()
	{
		foreach (var arg in OS.GetCmdlineUserArgs())
			if (arg.StartsWith("--tag="))
				_tag = arg["--tag=".Length..];

		var resources = ResourceProviderFactory.Create(ProjectSettings.GlobalizePath("res://Data"));
		_data.LoadAll(resources);

		_state.MapData = MapLoader.Load(resources, Map);
		_state.CurrentMap = Map;
		_state.UserPosX = UserX;
		_state.UserPosY = UserY;
		_state.UserCharIndex = 1;
		_state.Characters[1] = new Character { CharIndex = 1, Body = 1, Head = 1, Heading = 3, PosX = UserX, PosY = UserY };

		ResolutionManager.ApplyResolution(1920, 1080, true);
		DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;

		_renderer = new WorldRenderer();
		_renderer.Init(_state, _data, _animator, resources);
		AddChild(_renderer);
		_renderer.RebuildWaterMap();
		_renderer.BuildRoofRegions();

		_lastTick = Time.GetTicksUsec();
	}

	public override async void _Process(double delta)
	{
		if (_data.IsLoaded) _animator.Update((float)delta, _data);

		ulong now = Time.GetTicksUsec();
		double frameUs = now - _lastTick;
		_lastTick = now;
		_frame++;
		if (_frame > Warmup) _accumUs += frameUs;

		if (_frame == Warmup + Measured && !_saved)
		{
			_saved = true;
			double ms = _accumUs / Measured / 1000.0;
			GD.Print($"=== WaterPerfSmoke [{_tag}] mapa {Map} ({UserX},{UserY}) 1920x1080 vsync off ===");
			GD.Print($"  frame: {ms:F3} ms  ({1000.0 / ms:F1} FPS)");
			GD.Print($"  draw calls/frame: {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}");
			GD.Print($"  lotes de agua: {_renderer?.WaterBatchCount}");
			GD.Print($"  primitivas/frame: {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)}");

			string output = ProjectSettings.GlobalizePath("user://water-perf");
			Directory.CreateDirectory(output);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			using var image = GetViewport().GetTexture().GetImage();
			string path = Path.Combine(output, $"map27-{_tag}.png");
			if (image.SavePng(path) != Error.Ok)
				GD.PrintErr("No se pudo guardar la captura");
			else
				GD.Print($"  captura: {path}");
			GetTree().Quit();
		}
	}
}
