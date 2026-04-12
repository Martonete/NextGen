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

    // Occluders: pool grows as needed. Each shadow-casting tile contributes
    // 1+ occluders depending on how many polygons the GRH's alpha mask
    // produced. A Blocked-only tile (no Layer3 graphic) uses the shared
    // 32x32 square fallback.
    private readonly List<LightOccluder2D> _occluderPool = new();

    // Shared 32x32 axis-aligned square polygon, reused for Blocked-only tiles.
    private OccluderPolygon2D? _occluderSquare;

    // Graphics resources — injected so we can read each GRH's source image
    // and extract its alpha mask for pixel-accurate occluder shapes.
    private GrhData[]? _grhs;
    private TextureManager? _textures;

    /// <summary>Polygon cache keyed by GRH index. Each entry is the array of
    /// <see cref="OccluderPolygon2D"/> produced from the sprite's alpha
    /// contour — reused across every tile that draws the same GRH. An empty
    /// array means "no opaque pixels", which should never happen but is
    /// cached so we don't re-scan an all-transparent sprite.</summary>
    private readonly Dictionary<int, OccluderPolygon2D[]> _polygonCache = new();

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

    // Dynamic character occluder — a single persistent LightOccluder2D that
    // follows the player/hover position each frame so shadows update in
    // real time as you walk around. Lives OUTSIDE the tile occluder pool
    // so RebuildOccluders doesn't free it when L3 tiles change.
    private LightOccluder2D? _characterOccluder;
    private OccluderPolygon2D? _characterPolygon;

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

        // NOTE: we deliberately do NOT add a CanvasModulate to the parent.
        // CanvasModulate affects every CanvasItem on its CanvasLayer — in
        // the editor the MapViewport shares the default canvas layer with
        // the toolbar, sidebars, tabs, and status bar, so a CanvasModulate
        // here would darken the ENTIRE editor UI, not just the map. Proper
        // day/night would require either (a) wrapping the UI in a separate
        // CanvasLayer, or (b) rendering the map inside a SubViewport.
        // Until then the editor shows a bright map with additive light
        // glows on top; shadow areas appear via the lights' ShadowColor.
        _ambientNode = null;

        _occluderRoot = new Node2D { Name = "LightOccluders" };
        parent.AddChild(_occluderRoot);

        _lightsRoot = new Node2D { Name = "LightNodes" };
        parent.AddChild(_lightsRoot);

        // Character occluder: ~20 wide × 40 tall rectangle anchored at
        // the FEET (bottom edge), mirroring how an AO character sprite is
        // drawn — horizontally centered on the tile, extending upward
        // from the bottom. Caller passes the feet world position; the
        // polygon extends 40 px upward from there.
        //
        // TODO: swap this placeholder rectangle for the actual player
        // body GRH's alpha-extracted polygon once the light system is
        // rewritten with a custom shader. Today Light2D's shadow atlas
        // limitations make fine-grained character silhouettes pointless.
        _characterPolygon = new OccluderPolygon2D
        {
            CullMode = OccluderPolygon2D.CullModeEnum.Disabled,
            Polygon = new Vector2[]
            {
                new Vector2(-10, -40),
                new Vector2(10, -40),
                new Vector2(10, 0),
                new Vector2(-10, 0),
            },
        };
        _characterOccluder = new LightOccluder2D
        {
            Occluder = _characterPolygon,
            Visible = false,
        };
        _occluderRoot.AddChild(_characterOccluder);
    }

    /// <summary>Inject the graphics resources needed to compute pixel-
    /// accurate occluder polygons from each Layer3 GRH's alpha mask.
    /// Call after <see cref="AttachTo"/> and whenever the map's texture
    /// pack is swapped. Passing null clears the cache and falls back to
    /// 32x32 square occluders per tile.</summary>
    public void SetGraphicsResources(GrhData[]? grhs, TextureManager? textures)
    {
        _grhs = grhs;
        _textures = textures;
        _polygonCache.Clear();
        _occludersDirty = true;
    }

    /// <summary>Explicit show/hide hook for the caller. Hides both the
    /// light and occluder roots. Used by MapViewport when
    /// <c>State.ShowLights</c> is off.</summary>
    public void SetVisible(bool visible)
    {
        if (_lightsRoot != null) _lightsRoot.Visible = visible;
        if (_occluderRoot != null) _occluderRoot.Visible = visible;
    }

    /// <summary>Placeholder for a future global ambient-darkness feature.
    /// Currently a no-op because we don't use CanvasModulate in the editor
    /// (it would darken the whole UI, not just the map). Kept on the API
    /// so zone/map ambient wiring can be added without churn later.</summary>
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
    /// <paramref name="characterWorldPx"/> is the world-pixel position of
    /// the player / hover cursor — pass null to disable. The character is
    /// treated as a small dynamic occluder so lights respond in real time
    /// as it moves.
    /// </summary>
    public void Update(
        Vector2 panelSize,
        Vector2 worldOrigin,
        Vector2 worldSize,
        MapData? map,
        float deltaTime,
        Vector2? characterWorldPx = null)
    {
        if (_parent == null || _lightsRoot == null || _occluderRoot == null) return;

        // Visibility gate: when the parent panel is hidden (e.g. walk mode
        // toggled off and its LightRenderer still alive), skip the whole
        // update so we don't waste work rebuilding occluders or stepping
        // animations on unseen nodes.
        bool parentVisible = _parent is not CanvasItem ci || ci.Visible;
        _lightsRoot.Visible = parentVisible;
        _occluderRoot.Visible = parentVisible;
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

        // ── Dynamic character occluder ──
        // Moves every frame to track the player / hover. No MarkDirty
        // needed — Godot's Light2D picks up Position changes live.
        if (_characterOccluder != null)
        {
            if (characterWorldPx.HasValue)
            {
                _characterOccluder.Visible = true;
                _characterOccluder.Position = characterWorldPx.Value;
            }
            else
            {
                _characterOccluder.Visible = false;
            }
        }
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
                        // Opaque shadow color — without a CanvasModulate we
                        // can't rely on global darkness to show shadows,
                        // so the shadow pass itself must be visible.
                        // TRANSPARENT SHADOW COLOR — Godot's Light2D shadow atlas paints
// ShadowColor across the light's full square AABB regardless of the
// gradient texture's circular alpha mask, producing hard rectangular
// shadow blobs when many occluders are in range. Transparent
// shadow color neuters the visual artifact. Proper pixel-perfect
// shadows will require abandoning Light2D for a custom shader pass
// (see the fog system for the pattern).
ShadowColor = new Color(0f, 0f, 0f, 0f),
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
                        // TRANSPARENT SHADOW COLOR — Godot's Light2D shadow atlas paints
// ShadowColor across the light's full square AABB regardless of the
// gradient texture's circular alpha mask, producing hard rectangular
// shadow blobs when many occluders are in range. Transparent
// shadow color neuters the visual artifact. Proper pixel-perfect
// shadows will require abandoning Light2D for a custom shader pass
// (see the fog system for the pattern).
ShadowColor = new Color(0f, 0f, 0f, 0f),
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
    /// Generate LightOccluder2D nodes for every shadow-casting tile.
    ///
    /// Shadow casters are tiles with Layer3 content (trees, walls,
    /// furniture, buildings) OR the <c>Blocked</c> movement flag
    /// (invisible walls, water, cliffs). Layer2 is decoration (tile
    /// transitions, overlays) and must NOT cast shadows — it would
    /// darken every grass seam on the map. Layer4 (roofs) is also
    /// skipped because indoor lights would be completely blocked from
    /// their own building.
    ///
    /// Occluder shape for Layer3 tiles is pixel-accurate: the polygon
    /// is extracted from the GRH's alpha mask via
    /// <see cref="BitMap.OpaqueToPolygons"/> and cached per GRH index.
    /// The same polygon is reused for every tile that draws the same
    /// graphic, so even maps with thousands of trees only pay the
    /// mask-tracing cost once per unique sprite.
    ///
    /// Blocked-only tiles (no Layer3 graphic) fall back to the shared
    /// 32x32 axis-aligned square polygon.
    /// </summary>
    private void RebuildOccluders(MapData map)
    {
        if (_occluderRoot == null || _occluderSquare == null) return;

        // Non-pooling rebuild: QueueFree every existing occluder first, then
        // add fresh ones. Pooling with setter-based mutation was sometimes
        // leaving Godot's Light2D shadow atlas with stale geometry when
        // nearby tiles changed — this is slower but always correct.
        for (int i = 0; i < _occluderPool.Count; i++)
            if (GodotObject.IsInstanceValid(_occluderPool[i])) _occluderPool[i].QueueFree();
        _occluderPool.Clear();

        int W = map.Width;
        int H = map.Height;

        for (int y = 1; y <= H; y++)
        {
            for (int x = 1; x <= W; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                // Only Layer3 (objects) and Blocked flag cast shadows.
                // Layer2 is decorative overlays, Layer4 is roofs.
                if (tile.Layer3 == 0 && !tile.Blocked) continue;

                if (tile.Layer3 != 0)
                {
                    // Pixel-accurate occluder: resolve GRH → polygons → spawn
                    // one LightOccluder2D per polygon at the graphic's actual
                    // draw position (horizontally centered, bottom-anchored
                    // on the tile — matches MapViewport.DrawTileGrh's
                    // center:true branch).
                    var polys = GetPolygonsForGrh(tile.Layer3);
                    var grh = ResolveGrhFrame(tile.Layer3);
                    if (polys.Length > 0 && grh != null)
                    {
                        float drawX = (x - 1) * TileSize + (TileSize - grh.PixelWidth) / 2f;
                        float drawY = (y - 1) * TileSize + (TileSize - grh.PixelHeight);
                        var drawPos = new Vector2(drawX, drawY);

                        for (int p = 0; p < polys.Length; p++)
                            SpawnOccluder(polys[p], drawPos);
                        continue;
                    }
                    // Fall through to the 32x32 square fallback when the
                    // mask couldn't be traced (missing texture, etc).
                }

                // 32x32 square fallback for Blocked-only tiles or GRHs
                // whose alpha mask couldn't be extracted.
                SpawnOccluder(_occluderSquare, new Vector2((x - 1) * TileSize, (y - 1) * TileSize));
            }
        }
    }

    /// <summary>Create a fresh LightOccluder2D with the given polygon and
    /// position, parent it under <see cref="_occluderRoot"/>, and record it
    /// in <see cref="_occluderPool"/>. Used by <see cref="RebuildOccluders"/>
    /// — callers must have already cleared the pool.</summary>
    private void SpawnOccluder(OccluderPolygon2D polygon, Vector2 position)
    {
        var occ = new LightOccluder2D
        {
            Occluder = polygon,
            Position = position,
        };
        _occluderRoot!.AddChild(occ);
        _occluderPool.Add(occ);
    }

    /// <summary>Resolve an animated GRH down to its first frame so we can
    /// read a stable PixelWidth/Height/SX/SY. Static GRHs are returned
    /// as-is. Returns <c>null</c> if the index is out of range or
    /// <see cref="_grhs"/> hasn't been injected yet.</summary>
    private GrhData? ResolveGrhFrame(int grhIdx)
    {
        if (_grhs == null || grhIdx <= 0 || grhIdx >= _grhs.Length)
            return null;
        var g = _grhs[grhIdx];
        if (g.NumFrames > 1 && g.Frames != null && g.Frames.Length > 0)
        {
            int frame0 = g.Frames[0];
            if (frame0 > 0 && frame0 < _grhs.Length)
                return _grhs[frame0];
        }
        return g;
    }

    /// <summary>Extract one or more occluder polygons from a GRH's alpha
    /// mask via <see cref="BitMap.OpaqueToPolygons"/>. Results are cached
    /// per GRH index so every tile drawing the same sprite shares one
    /// <see cref="OccluderPolygon2D"/> instance. Returns an empty array
    /// when resources are unavailable or the sprite is fully transparent.
    ///
    /// Polygon coordinates are in the graphic's LOCAL space (0..PixelWidth,
    /// 0..PixelHeight). The caller positions the occluder node at the
    /// graphic's draw origin so the polygons end up in world space.
    /// </summary>
    private OccluderPolygon2D[] GetPolygonsForGrh(int grhIdx)
    {
        if (_polygonCache.TryGetValue(grhIdx, out var cached))
            return cached;

        var empty = System.Array.Empty<OccluderPolygon2D>();
        if (_grhs == null || _textures == null) { _polygonCache[grhIdx] = empty; return empty; }
        if (grhIdx <= 0 || grhIdx >= _grhs.Length) { _polygonCache[grhIdx] = empty; return empty; }

        var grh = ResolveGrhFrame(grhIdx);
        if (grh == null || grh.PixelWidth <= 0 || grh.PixelHeight <= 0 || grh.FileNum <= 0)
        { _polygonCache[grhIdx] = empty; return empty; }

        var srcImg = _textures.GetImageCached(grh.FileNum);
        if (srcImg == null) { _polygonCache[grhIdx] = empty; return empty; }

        // Bound the region to the loaded image so a bad SX/SY + width/height
        // doesn't throw at Image.GetRegion().
        int imgW = srcImg.GetWidth();
        int imgH = srcImg.GetHeight();
        int sx = Mathf.Clamp(grh.SX, 0, imgW);
        int sy = Mathf.Clamp(grh.SY, 0, imgH);
        int w = Mathf.Clamp(grh.PixelWidth, 1, imgW - sx);
        int h = Mathf.Clamp(grh.PixelHeight, 1, imgH - sy);
        if (w <= 0 || h <= 0) { _polygonCache[grhIdx] = empty; return empty; }

        Image region;
        try
        {
            region = srcImg.GetRegion(new Rect2I(sx, sy, w, h));
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[LightRenderer] GetRegion failed for grh {grhIdx}: {e.Message}");
            _polygonCache[grhIdx] = empty;
            return empty;
        }

        // Alpha > 0.25 is the cutoff for "solid" pixels — lower than the
        // typical 0.5 so we catch faint edges on semi-transparent sprites
        // (e.g. tree canopies with soft alpha).
        var bitmap = new Bitmap();
        bitmap.CreateFromImageAlpha(region, 0.25f);

        // NOTE: deliberately NOT calling GrowMask — growing the mask by
        // 1 pixel tends to merge distinct sub-shapes into a single blobby
        // outline, which makes the shadow look rectangular for sprites
        // with multiple disconnected parts (sword piles, fence posts).

        // Lower epsilon = finer polygon contours. 1.0 gives a near-pixel
        // match for typical 32–96 px sprites without exploding vertex
        // counts on large sprites like buildings.
        var rawPolys = bitmap.OpaqueToPolygons(new Rect2I(0, 0, w, h), 1.0f);
        if (rawPolys == null || rawPolys.Count == 0)
        { _polygonCache[grhIdx] = empty; return empty; }

        var result = new OccluderPolygon2D[rawPolys.Count];
        for (int i = 0; i < rawPolys.Count; i++)
        {
            result[i] = new OccluderPolygon2D
            {
                CullMode = OccluderPolygon2D.CullModeEnum.Disabled,
                Polygon = rawPolys[i],
            };
        }
        _polygonCache[grhIdx] = result;
        return result;
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

        if (_characterOccluder != null && GodotObject.IsInstanceValid(_characterOccluder))
            _characterOccluder.QueueFree();
        _characterOccluder = null;
        _characterPolygon = null;

        _lightGradient = null;
        _occluderSquare = null;
        _parent = null;
        _polygonCache.Clear();
        _grhs = null;
        _textures = null;

        _lightsDirty = true;
        _occludersDirty = true;
        _lastMapW = -1;
        _lastMapH = -1;
        _lastLightCount = -1;
    }
}
