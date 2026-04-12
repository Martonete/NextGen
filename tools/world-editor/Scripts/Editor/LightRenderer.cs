#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Advanced light renderer for the new <see cref="MapLight"/> system.
///
/// Uses Godot's NATIVE 2D light nodes (PointLight2D / DirectionalLight2D /
/// LightOccluder2D / CanvasModulate) instead of custom shader sprites.
///
/// Lifecycle mirrors <see cref="ZoneFogRenderer"/>: AttachTo -> Update (every
/// frame) -> Cleanup. Pools survive across Update calls; only Cleanup frees
/// the spawned nodes.
///
/// Coordinate model: lights and occluders share a single Node2D root whose
/// Position/Scale is updated every frame to map the world-pixel space onto
/// the panel-local space. Individual children live in natural world-pixel
/// coordinates so per-light recomputation is only needed when the underlying
/// MapLight data actually changes.
/// </summary>
public class LightRenderer
{
    private const int TileSize = 32;

    // Tuned so that Radius=1 tile gives a ~32px lit disc.
    // Gradient texture is 256 px square, half-extent = 128 px, and the
    // radial gradient FillTo is at uv=(1.0, 0.5) (256 px from centre).
    // TextureScale=1 maps the 256-px gradient to 256 world-px diameter.
    // We want diameter ≈ Radius * 32 px → scale = (Radius * 32) / 256.
    // The brief pinned the constant as Radius * 32 / 128 — keep that
    // exactly so the visual matches the spec the integrator agreed to.
    private const float TextureScaleDivisor = 128f;

    private Node? _parent;

    // One root for lights, one for occluders. Both get their Position/Scale
    // updated in Update() so children can stay in world-pixel coordinates.
    private Node2D? _lightsRoot;
    private Node2D? _occluderRoot;

    // Ambient darkness (default night-blue). Driven by SetAmbient.
    private CanvasModulate? _ambientNode;
    private Color _ambientColor = new Color(0.25f, 0.28f, 0.38f);

    // Shared radial gradient texture used by every PointLight2D in the pool.
    private GradientTexture2D? _lightGradient;

    // Two pools — switching MapLight.Type doesn't have to recreate nodes,
    // we just hide the unused pool slot and use the other one.
    private readonly List<PointLight2D> _omniPool = new();
    private readonly List<DirectionalLight2D> _dirPool = new();

    // Occluders: one 32x32 LightOccluder2D per blocked tile.
    private readonly List<LightOccluder2D> _occluderPool = new();

    // Shared 32x32 axis-aligned square polygon, reused by every occluder.
    private OccluderPolygon2D? _occluderSquare;

    private bool _lightsDirty = true;
    private bool _occludersDirty = true;
    private int _lastMapW = -1;
    private int _lastMapH = -1;
    private int _lastLightCount = -1;
    // Detect map-identity changes so two maps of the same size don't
    // silently keep the previous map's occluders/lights. Holding a weak
    // reference is enough — we only care about identity, not the instance.
    private System.WeakReference<MapData>? _lastMap;

    // Animation state for flicker / pulse modulation.
    private float _elapsedTime = 0f;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    public void AttachTo(Node parent)
    {
        _parent = parent;
        _rng.Randomize();

        // Build the shared radial gradient texture once.
        var grad = new Gradient();
        grad.SetColor(0, new Color(1, 1, 1, 1));
        grad.SetColor(1, new Color(1, 1, 1, 0));
        _lightGradient = new GradientTexture2D
        {
            Gradient = grad,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
        };

        // Shared 32x32 occluder polygon — reused across all occluder nodes.
        _occluderSquare = new OccluderPolygon2D
        {
            CullMode = OccluderPolygon2D.CullModeEnum.Disabled,
            Polygon = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(TileSize, 0),
                new Vector2(TileSize, TileSize),
                new Vector2(0, TileSize),
            },
        };

        _ambientNode = new CanvasModulate { Color = _ambientColor };
        parent.AddChild(_ambientNode);

        _occluderRoot = new Node2D { Name = "LightOccluders" };
        parent.AddChild(_occluderRoot);

        _lightsRoot = new Node2D { Name = "LightNodes" };
        parent.AddChild(_lightsRoot);
    }

    /// <summary>Explicit show/hide hook for the caller. When hidden, the
    /// CanvasModulate ambient is disabled so it can't darken sibling panels
    /// on the same canvas layer, and the light/occluder roots are hidden.
    /// Used by MapViewport when `State.ShowLights` is off.</summary>
    public void SetVisible(bool visible)
    {
        if (_ambientNode != null) _ambientNode.Visible = visible;
        if (_lightsRoot != null) _lightsRoot.Visible = visible;
        if (_occluderRoot != null) _occluderRoot.Visible = visible;
    }

    public void SetAmbient(Color ambient)
    {
        _ambientColor = ambient;
        if (_ambientNode != null) _ambientNode.Color = ambient;
    }

    public void MarkDirty()
    {
        _lightsDirty = true;
        _occludersDirty = true;
    }

    /// <summary>
    /// Call each frame. Mirrors <see cref="ZoneFogRenderer.Update"/>'s view
    /// mapping: <paramref name="worldOrigin"/> is the world-pixel coordinate
    /// at panel (0,0); <paramref name="worldSize"/> is how much world area is
    /// visible across <paramref name="panelSize"/>.
    /// </summary>
    public void Update(
        Vector2 panelSize,
        Vector2 worldOrigin,
        Vector2 worldSize,
        MapData? map,
        float deltaTime)
    {
        if (_parent == null || _lightsRoot == null || _occluderRoot == null) return;

        // Visibility gate: when the parent panel is hidden (e.g. walk mode
        // toggled off and its LightRenderer still alive), skip the whole
        // update AND hide the CanvasModulate so it doesn't darken the
        // sibling editor viewport on the same canvas layer.
        bool parentVisible = _parent is not CanvasItem ci || ci.Visible;
        if (_ambientNode != null) _ambientNode.Visible = parentVisible;
        if (!parentVisible) return;

        if (map == null)
        {
            HideAllLights();
            return;
        }

        _elapsedTime += deltaTime;

        // Detect map resize OR map-identity change (two maps of the same
        // size must still trigger a full occluder/light rebuild).
        bool mapChanged = false;
        if (_lastMap == null || !_lastMap.TryGetTarget(out var prev) || !ReferenceEquals(prev, map))
        {
            _lastMap = new System.WeakReference<MapData>(map);
            mapChanged = true;
        }
        if (_lastMapW != map.Width || _lastMapH != map.Height || mapChanged)
        {
            _lastMapW = map.Width;
            _lastMapH = map.Height;
            _occludersDirty = true;
            _lightsDirty = true;
        }

        // World->local scale is shared by both roots and is recomputed every
        // frame so the camera/zoom changes are picked up automatically.
        Vector2 scale = new Vector2(
            worldSize.X > 0f ? panelSize.X / worldSize.X : 1f,
            worldSize.Y > 0f ? panelSize.Y / worldSize.Y : 1f);

        Vector2 rootPosition = -worldOrigin * scale;

        _lightsRoot.Position = rootPosition;
        _lightsRoot.Scale = scale;
        _occluderRoot.Position = rootPosition;
        _occluderRoot.Scale = scale;

        // ── Occluder rebuild ──
        if (_occludersDirty)
        {
            RebuildOccluders(map);
            _occludersDirty = false;
        }

        // ── Light pool sync ──
        // Pool resync is driven exclusively by the dirty flag. Callers
        // (MapViewport.PlaceAdvancedLightAt/Erase..., EditorMain on map
        // load, WalkModePanel.InvalidateLighting) must invoke MarkDirty
        // whenever the MapLight list is mutated. A count-based heuristic
        // would silently break when two maps have the same number of
        // lights but different data.
        IReadOnlyList<MapLight> lights = map.LightData.Lights;
        if (_lightsDirty)
        {
            SyncLightPools(lights);
            _lastLightCount = lights.Count;
            _lightsDirty = false;
        }
        AnimateLights(lights);
    }

    /// <summary>
    /// Walk the MapLight list, ensuring the omni/directional pools each have
    /// enough nodes and that any unused slots are hidden. Per-light parameters
    /// that don't depend on time (color, position, radius, shadow flag) are
    /// applied here. Time-varying parameters are applied in
    /// <see cref="AnimateLights"/> so the per-frame cost stays minimal when
    /// nothing in the data has changed.
    /// </summary>
    private void SyncLightPools(IReadOnlyList<MapLight> lights)
    {
        if (_lightsRoot == null) return;

        int omniIdx = 0;
        int dirIdx = 0;

        for (int i = 0; i < lights.Count; i++)
        {
            var ml = lights[i];

            // World-pixel position for this light (centre of the tile).
            float worldX = (ml.X - 0.5f) * TileSize;
            float worldY = (ml.Y - 0.5f) * TileSize;
            var worldPos = new Vector2(worldX, worldY);

            var color = new Color(ml.R / 255f, ml.G / 255f, ml.B / 255f);

            if (ml.Type == LightType.Directional)
            {
                while (_dirPool.Count <= dirIdx)
                {
                    var newDir = new DirectionalLight2D
                    {
                        Visible = false,
                        BlendMode = Light2D.BlendModeEnum.Add,
                        ShadowFilter = Light2D.ShadowFilterEnum.Pcf5,
                    };
                    _lightsRoot.AddChild(newDir);
                    _dirPool.Add(newDir);
                }

                var d = _dirPool[dirIdx++];
                d.Visible = true;
                d.Position = worldPos;
                d.Color = color;
                d.Rotation = Mathf.DegToRad(ml.DirectionDeg);
                d.ShadowEnabled = ml.ShadowsEnabled;
            }
            else
            {
                // Omni and Spot share the PointLight2D pool. See note below.
                while (_omniPool.Count <= omniIdx)
                {
                    var newOmni = new PointLight2D
                    {
                        Visible = false,
                        BlendMode = Light2D.BlendModeEnum.Add,
                        ShadowFilter = Light2D.ShadowFilterEnum.Pcf5,
                        Texture = _lightGradient,
                    };
                    _lightsRoot.AddChild(newOmni);
                    _omniPool.Add(newOmni);
                }

                var p = _omniPool[omniIdx++];
                p.Visible = true;
                p.Position = worldPos;
                p.Color = color;
                p.ShadowEnabled = ml.ShadowsEnabled;
                p.TextureScale = ml.Radius * TileSize / TextureScaleDivisor;

                // TODO: Spot is rendered identically to Omni for now —
                // PointLight2D has no native cone mask. A future enhancement
                // would assign a directional cone gradient texture and use
                // the rotation set below to orient it. For now we still set
                // the rotation so the data is preserved on the node.
                p.Rotation = Mathf.DegToRad(ml.DirectionDeg);
            }
        }

        // Hide unused omni pool slots.
        for (int i = omniIdx; i < _omniPool.Count; i++)
            _omniPool[i].Visible = false;

        // Hide unused directional pool slots.
        for (int i = dirIdx; i < _dirPool.Count; i++)
            _dirPool[i].Visible = false;
    }

    /// <summary>
    /// Apply animated energy (pulse + flicker) to every visible light.
    /// Always runs every frame regardless of <see cref="_lightsDirty"/>.
    /// </summary>
    private void AnimateLights(IReadOnlyList<MapLight> lights)
    {
        int omniIdx = 0;
        int dirIdx = 0;

        for (int i = 0; i < lights.Count; i++)
        {
            var ml = lights[i];

            float e = ml.Energy;
            if (ml.PulseHz > 0f)
                e *= 0.7f + 0.3f * Mathf.Sin(_elapsedTime * ml.PulseHz * Mathf.Tau);
            if (ml.FlickerPct > 0)
                e *= 1f - (ml.FlickerPct / 100f) * (float)_rng.RandfRange(0f, 1f);
            float energy = Mathf.Max(e, 0f);

            if (ml.Type == LightType.Directional)
            {
                if (dirIdx < _dirPool.Count)
                    _dirPool[dirIdx].Energy = energy;
                dirIdx++;
            }
            else
            {
                if (omniIdx < _omniPool.Count)
                    _omniPool[omniIdx].Energy = energy;
                omniIdx++;
            }
        }
    }

    /// <summary>
    /// Generate one 32x32 axis-aligned square LightOccluder2D per tile that
    /// has Layer2 or Layer3 content. Reuses pooled occluders and resizes the
    /// pool to match. Polygon geometry is the shared 32x32 square — only
    /// Position differs between occluders.
    ///
    /// V1 deliberately does not merge adjacent occluder islands or run
    /// marching squares. Per-tile occluders are correct, simple, and scale
    /// fine for typical map sizes (~1000-5000 blocked tiles).
    /// </summary>
    private void RebuildOccluders(MapData map)
    {
        if (_occluderRoot == null || _occluderSquare == null) return;

        int used = 0;
        int W = map.Width;
        int H = map.Height;

        for (int y = 1; y <= H; y++)
        {
            for (int x = 1; x <= W; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (tile.Layer2 == 0 && tile.Layer3 == 0) continue;

                // World-pixel top-left of this tile.
                var pos = new Vector2((x - 1) * TileSize, (y - 1) * TileSize);

                LightOccluder2D occ;
                if (used < _occluderPool.Count)
                {
                    occ = _occluderPool[used];
                    occ.Visible = true;
                }
                else
                {
                    occ = new LightOccluder2D
                    {
                        Occluder = _occluderSquare,
                    };
                    _occluderRoot.AddChild(occ);
                    _occluderPool.Add(occ);
                }
                occ.Position = pos;
                used++;
            }
        }

        // Hide leftover slots from previous (larger) rebuilds.
        for (int i = used; i < _occluderPool.Count; i++)
            _occluderPool[i].Visible = false;
    }

    private void HideAllLights()
    {
        for (int i = 0; i < _omniPool.Count; i++) _omniPool[i].Visible = false;
        for (int i = 0; i < _dirPool.Count; i++) _dirPool[i].Visible = false;
    }

    public void Cleanup()
    {
        for (int i = 0; i < _omniPool.Count; i++)
            if (GodotObject.IsInstanceValid(_omniPool[i])) _omniPool[i].QueueFree();
        _omniPool.Clear();

        for (int i = 0; i < _dirPool.Count; i++)
            if (GodotObject.IsInstanceValid(_dirPool[i])) _dirPool[i].QueueFree();
        _dirPool.Clear();

        for (int i = 0; i < _occluderPool.Count; i++)
            if (GodotObject.IsInstanceValid(_occluderPool[i])) _occluderPool[i].QueueFree();
        _occluderPool.Clear();

        if (_lightsRoot != null && GodotObject.IsInstanceValid(_lightsRoot))
            _lightsRoot.QueueFree();
        _lightsRoot = null;

        if (_occluderRoot != null && GodotObject.IsInstanceValid(_occluderRoot))
            _occluderRoot.QueueFree();
        _occluderRoot = null;

        if (_ambientNode != null && GodotObject.IsInstanceValid(_ambientNode))
            _ambientNode.QueueFree();
        _ambientNode = null;

        _lightGradient = null;
        _occluderSquare = null;
        _parent = null;

        _lightsDirty = true;
        _occludersDirty = true;
        _lastMapW = -1;
        _lastMapH = -1;
        _lastLightCount = -1;
    }
}
