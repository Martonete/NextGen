#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Native Godot 2D lighting system for the world editor and walk mode.
/// Uses CanvasModulate (ambient) + PointLight2D nodes (per tile light)
/// rendered inside a SubViewport so the editor UI is unaffected.
///
/// Pipeline:
/// - SubViewportContainer hosts a SubViewport sized to the visible area.
/// - Inside the SubViewport: a Node2D root, a CanvasModulate child (ambient color),
///   and one PointLight2D per tile with HasLight (sharing one radial gradient texture).
/// - The actual map drawing still happens in the parent Control via _Draw.
///   The lights are rendered ON TOP using the parent's CanvasItems.
///
/// For simplicity in V1, we attach PointLight2D nodes directly as children of
/// the MapViewport. Godot allows Node2D children of Control. PointLight2D affects
/// any CanvasItem in its layer/range — including the parent Control's drawn tiles
/// — when there is a CanvasModulate ancestor.
/// </summary>
public class LightingSystem
{
    private const int TileSize = 32;

    private readonly Node _parent;          // CanvasItem that owns the lights (Control or Node2D)
    private Node2D? _container;             // Node2D root that holds all lights and applies world transform
    private CanvasModulate? _modulate;      // Ambient darkening
    private readonly List<PointLight2D> _pool = new();   // Reusable light nodes
    private int _activeCount;               // How many lights from the pool are in use this frame
    private Texture2D? _lightTexture;       // Shared radial gradient texture
    private bool _enabled = true;
    private Color _ambient = new(1, 1, 1, 1);

    // Shadow occluder pool
    private readonly List<LightOccluder2D> _occluderPool = new();
    private int _activeOccluderCount;
    private OccluderPolygon2D? _sharedOccluderPolygon; // 32×32 square, shared across this instance's occluders

    /// <summary>Create the lighting system attached to the given parent CanvasItem.</summary>
    public LightingSystem(Node parent)
    {
        _parent = parent;
        _lightTexture = BuildRadialGradient(256);
    }

    /// <summary>Set the world transform applied to all lights (camera offset + zoom).
    /// PointLight2D positions are in world coordinates; we use a Node2D container
    /// that applies the same offset/scale as the parent's DrawSetTransform.</summary>
    public void SetWorldTransform(Vector2 offset, float zoom)
    {
        EnsureContainer();
        if (_container != null)
        {
            _container.Position = offset;
            _container.Scale = new Vector2(zoom, zoom);
        }
    }

    private void EnsureContainer()
    {
        if (_container == null)
        {
            _container = new Node2D { Name = "LightContainer", ZIndex = 0 };
            _parent.AddChild(_container);
        }
    }

    /// <summary>Set the ambient color (the shade in unlit areas).
    /// Use Color(1,1,1) for full brightness or Color(0.2,0.2,0.3) for night.</summary>
    public void SetAmbient(Color color)
    {
        _ambient = color;
        EnsureModulate();
        if (_modulate != null) _modulate.Color = _ambient;
    }

    /// <summary>Toggle the entire lighting system on or off.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (_modulate != null) _modulate.Visible = enabled;
        if (!enabled)
        {
            foreach (var light in _pool) light.Visible = false;
            _activeCount = 0;
        }
    }

    private void EnsureModulate()
    {
        EnsureContainer();
        if (_modulate == null)
        {
            _modulate = new CanvasModulate { Color = _ambient };
            _container?.AddChild(_modulate);
        }
    }

    /// <summary>
    /// Sync the light pool with the current map tile data.
    /// Each tile with HasLight becomes one PointLight2D positioned at the tile center,
    /// with color and texture scale derived from the tile's RGB and Range.
    /// Idempotent — call whenever lights change or the map is loaded.
    /// </summary>
    public void Rebuild(MapData? map)
    {
        if (map == null || !_enabled)
        {
            // Hide all lights
            foreach (var light in _pool) light.Visible = false;
            _activeCount = 0;
            return;
        }

        EnsureModulate();
        if (_modulate != null) _modulate.Color = _ambient;

        int idx = 0;
        for (int y = 1; y <= map.Height; y++)
        {
            for (int x = 1; x <= map.Width; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (!tile.HasLight) continue;

                var light = AcquireLight(idx++);
                // Tile center in world pixels (1-based tile coords → 0-based pixels)
                light.Position = new Vector2(
                    (x - 0.5f) * TileSize,
                    (y - 0.5f) * TileSize);
                light.Color = new Color(
                    tile.LightR / 255f,
                    tile.LightG / 255f,
                    tile.LightB / 255f,
                    1f);
                // Scale the 256px gradient so its diameter equals 2*range tiles.
                // Range=6 → diameter 12 tiles = 384px → scale ≈ 1.5
                float diameterPx = tile.LightRange * 2f * TileSize;
                light.TextureScale = diameterPx / 256f;
                light.Energy = 1.0f;
                light.Visible = true;
            }
        }

        _activeCount = idx;
        // Hide unused pool lights
        for (int i = _activeCount; i < _pool.Count; i++)
            _pool[i].Visible = false;
    }

    private PointLight2D AcquireLight(int index)
    {
        EnsureContainer();
        while (_pool.Count <= index)
        {
            var l = new PointLight2D
            {
                Texture = _lightTexture,
                BlendMode = Light2D.BlendModeEnum.Add,
                ShadowEnabled = true,
                ShadowFilter = Light2D.ShadowFilterEnum.Pcf5,
                ShadowFilterSmooth = 1.5f,
                ShadowItemCullMask = 1,
                Visible = false,
                ZIndex = 0,
            };
            _container?.AddChild(l);
            _pool.Add(l);
        }
        return _pool[index];
    }

    /// <summary>
    /// Build or rebuild LightOccluder2D nodes for every Blocked tile in the map.
    /// One occluder per blocked tile, using a shared 32×32 square polygon.
    /// Must be called whenever the blocked layer changes.
    /// </summary>
    public void RebuildOccluders(MapData? map)
    {
        if (map == null)
        {
            for (int i = 0; i < _occluderPool.Count; i++)
                _occluderPool[i].Visible = false;
            _activeOccluderCount = 0;
            return;
        }

        EnsureContainer();

        // Lazily create the shared occluder polygon (32×32 square at tile origin)
        if (_sharedOccluderPolygon == null)
        {
            _sharedOccluderPolygon = new OccluderPolygon2D
            {
                Polygon = new[]
                {
                    new Vector2(0, 0),
                    new Vector2(TileSize, 0),
                    new Vector2(TileSize, TileSize),
                    new Vector2(0, TileSize),
                }
            };
        }

        int idx = 0;
        for (int y = 1; y <= map.Height; y++)
        {
            for (int x = 1; x <= map.Width; x++)
            {
                if (!map.Tiles[x, y].Blocked) continue;

                var occ = AcquireOccluder(idx++);
                // Position at tile top-left in world pixels (1-based coords → 0-based pixels)
                occ.Position = new Vector2((x - 1) * TileSize, (y - 1) * TileSize);
                occ.Visible = true;
            }
        }

        _activeOccluderCount = idx;
        for (int i = _activeOccluderCount; i < _occluderPool.Count; i++)
            _occluderPool[i].Visible = false;
    }

    private LightOccluder2D AcquireOccluder(int index)
    {
        EnsureContainer();
        while (_occluderPool.Count <= index)
        {
            var o = new LightOccluder2D
            {
                Occluder = _sharedOccluderPolygon,
                Visible = false,
            };
            _container?.AddChild(o);
            _occluderPool.Add(o);
        }
        return _occluderPool[index];
    }

    /// <summary>Build a radial white gradient texture (256x256) — center white, edge transparent.</summary>
    private static Texture2D BuildRadialGradient(int size)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float center = size / 2f;
        float maxDist = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float t = Math.Clamp(dist / maxDist, 0f, 1f);
                // Smooth quadratic falloff: 1 at center, 0 at edge
                float a = (1f - t) * (1f - t);
                img.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Cleanup all created nodes (call from _ExitTree).</summary>
    public void Cleanup()
    {
        foreach (var l in _pool)
            if (GodotObject.IsInstanceValid(l)) l.QueueFree();
        _pool.Clear();
        foreach (var o in _occluderPool)
            if (GodotObject.IsInstanceValid(o)) o.QueueFree();
        _occluderPool.Clear();
        if (_sharedOccluderPolygon != null && GodotObject.IsInstanceValid(_sharedOccluderPolygon))
            _sharedOccluderPolygon.Dispose();
        _sharedOccluderPolygon = null;
        if (_modulate != null && GodotObject.IsInstanceValid(_modulate))
            _modulate.QueueFree();
        _modulate = null;
        if (_container != null && GodotObject.IsInstanceValid(_container))
            _container.QueueFree();
        _container = null;
    }
}
