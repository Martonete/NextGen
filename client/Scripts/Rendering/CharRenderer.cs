using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Pre-computed byte-to-float lookup table. Avoids repeated byte/255f divisions
/// in particle and aura color conversions during hot render loops.
/// </summary>
internal static class ByteToFloat
{
    internal static readonly float[] Table = new float[256];
    static ByteToFloat()
    {
        for (int i = 0; i < 256; i++) Table[i] = i / 255f;
    }
}

/// <summary>
/// Renders a character with heading-dependent layer order matching VB6 exactly.
/// VB6 dibujarPersonaje() changes draw order per heading:
///   Heading 1 (N):  Arma → Escudo → Body → Head
///   Heading 2 (E):  Escudo → Body → Head → Arma
///   Heading 3 (S):  Body → Head → Arma → Escudo
///   Heading 4 (W):  Arma → Body → Head → Escudo
///
/// All character components use Center=1 in VB6 (bodies are multi-tile sprites).
/// </summary>
public static partial class CharRenderer
{
	private const int TileSize = 32;
	// Helmets use the same head anchor as Cabezas.ind. Older code subtracted 34px here,
	// which made TS AO helmets float above the character.
	private const int HELMET_Y_OFFSET = 1;

	// Static buffers for DrawShadowProjected — reused every call to avoid per-frame allocations
	private static readonly Vector2[] _shadowVerts = new Vector2[4];
	private static readonly Color[] _shadowColors = new Color[4];

	private readonly struct AuraDrawData
	{
		public AuraDrawData(int grhIndex, int frame, Vector2 position, Color color, float angle, bool rotating)
		{
			GrhIndex = grhIndex;
			Frame = frame;
			Position = position;
			Color = color;
			Angle = angle;
			Rotating = rotating;
		}

		public int GrhIndex { get; }
		public int Frame { get; }
		public Vector2 Position { get; }
		public Color Color { get; }
		public float Angle { get; }
		public bool Rotating { get; }
	}

	/// <summary>
	/// Fade speed: characters transition from visible to invisible over ~250ms.
	/// Rate = 1.0 / 250ms = 4.0 per second.
	/// </summary>
	private const float FovFadeRate = 4.0f;

	/// <summary>
	/// Check if a character is inside the core 17x13 viewport (the original 800x600 area).
	/// Characters outside this area should fade out smoothly.
	/// </summary>
	private static bool IsInsideCoreViewport(int charPosX, int charPosY, int userX, int userY)
	{
		return Math.Abs(charPosX - userX) <= ResolutionManager.CoreHalfX
			&& Math.Abs(charPosY - userY) <= ResolutionManager.CoreHalfY;
	}

	public static void DrawCharacter(
		Node2D canvas,
		Character ch,
		Vector2 screenPos,
		GameData data,
		GrhAnimator animator,
		float deltaMs = 0f,
		GameState? state = null,
		WorldRenderer? worldRenderer = null,
		int charTileX = 0,
		int charTileY = 0,
		int charIdx = -1)
	{
		// FOV fade — timers are advanced by UpdateCharacterTimers in _Process.
		// For UI previews (state == null), always fully visible.
		if (state != null && ch.FovAlpha <= 0.01f) return; // fully faded out, skip drawing

		int heading = ch.Heading;
		if (heading < 1 || heading > 4) heading = 3;

		// Pre-resolve body data for head offset
		BodyData? body = null;
		if (ch.Body > 0 && ch.Body < data.Bodies.Length)
			body = data.Bodies[ch.Body];

		Vector2 headOffset = body != null
			? new Vector2(body.HeadOffsetX, body.HeadOffsetY)
			: new Vector2(0, -30);

		// TransparenciaBody pulsing — advanced by UpdateCharacterTimers in _Process.

		// Water reflections are now drawn by WorldRenderer (PASS 1.5) between
		// Layer 1 and Layer 2, so they clip naturally to water tiles.

		float fovAlpha = ch.FovAlpha;

		// Shadow: diagonal projection (light from lower-left → shadow upper-right)
		bool drawShadow = state != null;
		if (state?.Config != null)
		{
			bool isNpc = ch.CharIndex != state.UserCharIndex && ch.NpcNumber > 0;
			drawShadow = isNpc ? state.Config.ShowNpcShadows : state.Config.ShowShadows;
		}
		if (drawShadow && !ch.Invisible && fovAlpha > 0.3f)
			DrawShadow(canvas, ch, screenPos, heading, data, animator);

		// VB6: invisible self = pulsing transparency (TransparenciaBody 0-100)
		// Combined with FOV fade alpha for smooth boundary transitions
		Color? invisOverride = null;
		if (ch.Invisible)
			invisOverride = new Color(1, 1, 1, ch.TransparenciaBody / 100f * fovAlpha);
		else if (fovAlpha < 1f)
			invisOverride = new Color(1, 1, 1, fovAlpha);

		// Apply walk bob — purely visual offset, no effect on tile position or gameplay
		Vector2 bobbedPos = ch.BobY != 0f ? new Vector2(screenPos.X, screenPos.Y + ch.BobY) : screenPos;

		// Heading-dependent draw order (VB6: dibujarPersonaje)
		DrawCharParts(canvas, ch, bobbedPos, headOffset, heading, data, animator, state,
					  colorOverride: invisOverride);

		// FX overlays — not drawn when invisible (VB6: entire char skipped in invisible branch)
		if (!ch.Invisible)
			DrawFx(canvas, ch, bobbedPos, data, animator, deltaMs);

		// Character-attached particles — not drawn when invisible
		if (!ch.Invisible && state != null && (state.Config?.ShowParticles ?? true))
			DrawCharParticles(canvas, ch, screenPos, state, data, animator?.GlobalTimeMs ?? 0, worldRenderer, charIdx);

		// Name + clan above head (VB6: uses font1 bitmap font, toggled by N key)
		// VB6: name IS drawn for invisible self (visible to self/GMs)
		if (state == null || state.ShowNames)
			DrawName(canvas, ch, screenPos, data, state);

		// Dialog bubble — queued to overlay layer (above all characters/NPCs)
		DrawDialog(canvas, ch, screenPos, headOffset, data, deltaMs, worldRenderer);
	}


	/// <summary>
	/// Character shadow: body + head projected from a shared anchor point (body feet).
	/// Light from lower-left → shadow falls toward upper-right.
	/// Both sprites are projected through the same transform so the head
	/// shadow sits directly on top of the body shadow, forming a complete silhouette.
	/// </summary>
	private static void DrawShadow(
		Node2D canvas, Character ch, Vector2 screenPos, int heading,
		GameData data, GrhAnimator animator)
	{
		if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;

		// Shadow projection constants
		const float ShearRatio = 0.3f;  // per pixel above feet, shift right by 0.3px
		const float FlatRatio = 0.85f;  // per pixel above feet, compress to 0.85px
		Color shadowColor = new(0, 0, 0, 0.35f);

		// Resolve body to compute the shared anchor point (feetY)
		int bodyGrh = data.Bodies[ch.Body].Walk[heading];
		if (bodyGrh <= 0) return;
		int bodyFrame = ch.Moving ? (int)ch.WalkFrame : 0;
		var bodyRes = data.ResolveGrh(bodyGrh, bodyFrame);
		if (bodyRes == null || bodyRes.FileNum <= 0) return;

		// Body draw position (with centering)
		float bodyDrawX = screenPos.X;
		float bodyDrawY = screenPos.Y;
		if (bodyRes.TileWidth != 1f && bodyRes.TileWidth > 0)
			bodyDrawX -= (int)(bodyRes.TileWidth * (TileSize / 2)) - TileSize / 2;
		if (bodyRes.TileHeight != 1f && bodyRes.TileHeight > 0)
			bodyDrawY -= (int)(bodyRes.TileHeight * TileSize) - TileSize;

		// Shared anchor: body feet (bottom of body sprite)
		float feetY = bodyDrawY + bodyRes.PixelHeight;

		// Draw body shadow
		DrawShadowProjected(canvas, bodyRes, bodyDrawX, bodyDrawY, feetY,
			ShearRatio, FlatRatio, shadowColor, data);

		// Draw head shadow (projected from same feetY)
		if (ch.Head > 0 && ch.Head < data.Heads.Length)
		{
			int headGrh = data.Heads[ch.Head].Head[heading];
			if (headGrh <= 0) return;
			var headRes = data.ResolveGrh(headGrh, 0);
			if (headRes == null || headRes.FileNum <= 0) return;

			// Head normal position = body position + headOffset
			float headDrawX = screenPos.X + data.Bodies[ch.Body].HeadOffsetX;
			float headDrawY = screenPos.Y + data.Bodies[ch.Body].HeadOffsetY;
			// Head centering (same logic as DrawGrh)
			if (headRes.TileWidth != 1f && headRes.TileWidth > 0)
				headDrawX -= (int)(headRes.TileWidth * (TileSize / 2)) - TileSize / 2;
			if (headRes.TileHeight != 1f && headRes.TileHeight > 0)
				headDrawY -= (int)(headRes.TileHeight * TileSize) - TileSize;

			DrawShadowProjected(canvas, headRes, headDrawX, headDrawY, feetY,
				ShearRatio, FlatRatio, shadowColor, data);
		}

		// Draw helmet shadow (projected from same feetY)
		if (ch.CascoAnim > 0 && ch.CascoAnim < data.Cascos.Length)
		{
			var casco = data.Cascos[ch.CascoAnim];
			if (casco.Head != null && casco.Head[heading] > 0)
			{
				var cascoRes = data.ResolveGrh(casco.Head[heading], 0);
				if (cascoRes != null && cascoRes.FileNum > 0)
				{
					float cascoDrawX = screenPos.X + data.Bodies[ch.Body].HeadOffsetX;
					float cascoDrawY = screenPos.Y + data.Bodies[ch.Body].HeadOffsetY + HELMET_Y_OFFSET;
					if (cascoRes.TileWidth != 1f && cascoRes.TileWidth > 0)
						cascoDrawX -= (int)(cascoRes.TileWidth * (TileSize / 2)) - TileSize / 2;
					if (cascoRes.TileHeight != 1f && cascoRes.TileHeight > 0)
						cascoDrawY -= (int)(cascoRes.TileHeight * TileSize) - TileSize;

					DrawShadowProjected(canvas, cascoRes, cascoDrawX, cascoDrawY, feetY,
						ShearRatio, FlatRatio, shadowColor, data);
				}
			}
		}

		// Draw weapon shadow (projected from same feetY)
		if (ch.WeaponAnim > 0 && ch.WeaponAnim < data.Weapons.Length)
		{
			int weapGrh = data.Weapons[ch.WeaponAnim].Walk[heading];
			if (weapGrh > 0)
			{
				int weapFrame = ch.Moving ? (int)ch.WalkFrame : 0;
				var weapRes = data.ResolveGrh(weapGrh, weapFrame);
				if (weapRes != null && weapRes.FileNum > 0)
				{
					float weapDrawX = screenPos.X;
					float weapDrawY = screenPos.Y;
					if (weapRes.TileWidth != 1f && weapRes.TileWidth > 0)
						weapDrawX -= (int)(weapRes.TileWidth * (TileSize / 2)) - TileSize / 2;
					if (weapRes.TileHeight != 1f && weapRes.TileHeight > 0)
						weapDrawY -= (int)(weapRes.TileHeight * TileSize) - TileSize;

					DrawShadowProjected(canvas, weapRes, weapDrawX, weapDrawY, feetY,
						ShearRatio, FlatRatio, shadowColor, data);
				}
			}
		}

		// Draw shield shadow (projected from same feetY)
		if (ch.ShieldAnim > 0 && ch.ShieldAnim < data.Shields.Length)
		{
			int shieldGrh = data.Shields[ch.ShieldAnim].Walk[heading];
			if (shieldGrh > 0)
			{
				int shieldFrame = ch.Moving ? (int)ch.WalkFrame : 0;
				var shieldRes = data.ResolveGrh(shieldGrh, shieldFrame);
				if (shieldRes != null && shieldRes.FileNum > 0)
				{
					float shieldDrawX = screenPos.X;
					float shieldDrawY = screenPos.Y;
					if (shieldRes.TileWidth != 1f && shieldRes.TileWidth > 0)
						shieldDrawX -= (int)(shieldRes.TileWidth * (TileSize / 2)) - TileSize / 2;
					if (shieldRes.TileHeight != 1f && shieldRes.TileHeight > 0)
						shieldDrawY -= (int)(shieldRes.TileHeight * TileSize) - TileSize;

					DrawShadowProjected(canvas, shieldRes, shieldDrawX, shieldDrawY, feetY,
						ShearRatio, FlatRatio, shadowColor, data);
				}
			}
		}
	}

	/// <summary>
	/// Project a sprite as a shadow parallelogram from a shared anchor (feetY).
	/// Each corner (px, py) is transformed:
	///   dy = feetY - py (distance above feet)
	///   shadowX = px + dy * shearRatio
	///   shadowY = feetY - dy * flatRatio
	/// This ensures body + head shadows form a coherent silhouette.
	/// </summary>
	private static void DrawShadowProjected(
		Node2D canvas, GrhData resolved, float drawX, float drawY, float feetY,
		float shearRatio, float flatRatio, Color shadowColor, GameData data)
	{
		var texture = data.Textures?.GetTexture(resolved.FileNum);
		if (texture == null) return;

		int texW = texture.GetWidth(), texH = texture.GetHeight();
		int sx = resolved.SX, sy = resolved.SY;
		int pw = resolved.PixelWidth, ph = resolved.PixelHeight;
		if (texW > 0) sx %= texW;
		if (texH > 0) sy %= texH;
		if (sx + pw > texW) pw = texW - sx;
		if (sy + ph > texH) ph = texH - sy;
		if (pw <= 0 || ph <= 0) return;

		// Distance from feet for top and bottom edges of this sprite
		float topDy = feetY - drawY;             // top of sprite
		float botDy = feetY - (drawY + ph);      // bottom of sprite

		// Project 4 corners through the shadow transform
		Vector2 tl = new(drawX + topDy * shearRatio, feetY - topDy * flatRatio);
		Vector2 tr = new(drawX + pw + topDy * shearRatio, feetY - topDy * flatRatio);
		Vector2 bl = new(drawX + botDy * shearRatio, feetY - botDy * flatRatio);
		Vector2 br = new(drawX + pw + botDy * shearRatio, feetY - botDy * flatRatio);

		float u0 = (float)sx / texW;
		float u1 = (float)(sx + pw) / texW;
		float vTop = (float)sy / texH;
		float vBot = (float)(sy + ph) / texH;

		// CCW: BL → BR → TR → TL
		_shadowVerts[0] = bl; _shadowVerts[1] = br; _shadowVerts[2] = tr; _shadowVerts[3] = tl;
		_shadowColors[0] = shadowColor; _shadowColors[1] = shadowColor;
		_shadowColors[2] = shadowColor; _shadowColors[3] = shadowColor;
		canvas.DrawPolygon(
			_shadowVerts,
			_shadowColors,
			new[] { new Vector2(u0, vBot), new Vector2(u1, vBot),
					new Vector2(u1, vTop), new Vector2(u0, vTop) },
			texture);
	}

	/// <summary>
	/// Draw character parts (body, head, helmet, weapon, shield) in VB6 heading order.
	/// Used by both normal rendering and reflection (via DrawSetTransform flip).
	/// </summary>
	private static void DrawCharParts(
		Node2D canvas, Character ch, Vector2 pos, Vector2 headOffset,
		int heading, GameData data, GrhAnimator animator,
		GameState? state = null, Color? colorOverride = null,
		Color? equipColorOverride = null, Color? helmetColorOverride = null)
	{
		Color? ec = equipColorOverride ?? colorOverride;
		Color? hc = helmetColorOverride ?? ec;
		switch (heading)
		{
			case 1: // North
				DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
				DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
				DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
				DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
				DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
				break;
			case 2: // East
				DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
				DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
				DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
				DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
				DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
				break;
			case 3: // South
				DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
				DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
				DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
				DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
				DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
				break;
			case 4: // West
				DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
				DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
				DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
				DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
				DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
				break;
		}
	}

	private static void DrawBody(
		Node2D canvas, Character ch, Vector2 pos, int heading,
		GameData data, GrhAnimator animator, GameState? state = null,
		Color? colorOverride = null)
	{
		if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;
		var body = data.Bodies[ch.Body];
		if (body.Walk[heading] == 0) return;

		int bodyGrh = body.Walk[heading];
		int frame = ch.Moving ? (int)ch.WalkFrame : 0;

		if (colorOverride.HasValue)
		{
			DrawGrh(canvas, data, bodyGrh, frame, pos, true, colorOverride.Value);
			return;
		}
		// Dead alpha: config slider (20-100%) → fixed alpha, alive = 255
		byte alpha = (ch.Dead && (state?.Config?.DeadCharTransparency ?? true))
			? (byte)((state?.Config?.DeadTransparencyAlpha ?? 47) * 255 / 100) : (byte)255;
		DrawGrh(canvas, data, bodyGrh, frame, pos, true,
				alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
	}

	private static void DrawHead(
		Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
		int heading, GameData data, GameState? state = null,
		Color? colorOverride = null)
	{
		if (ch.Head <= 0 || ch.Head >= data.Heads.Length) return;
		var head = data.Heads[ch.Head];
		if (head.Head[heading] == 0) return;

		// VB6: no per-heading X adjustment; mounted gets X+1
		float xAdj = ch.Mounted ? 1f : 0f;
		Vector2 headPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + 1);

		if (colorOverride.HasValue)
		{
			DrawGrh(canvas, data, head.Head[heading], 0, headPos, true, colorOverride.Value);
			return;
		}
		// Dead alpha: config slider (20-100%) → fixed alpha, alive = 255
		byte alpha = (ch.Dead && (state?.Config?.DeadCharTransparency ?? true))
			? (byte)((state?.Config?.DeadTransparencyAlpha ?? 47) * 255 / 100) : (byte)255;
		DrawGrh(canvas, data, head.Head[heading], 0, headPos, true,
				alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
	}

	private static void DrawHelmet(
		Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
		int heading, GameData data, Color? colorOverride = null)
	{
		if (ch.CascoAnim <= 0 || ch.CascoAnim >= data.Cascos.Length) return;
		var casco = data.Cascos[ch.CascoAnim];
		if (casco.Head == null || casco.Head[heading] == 0) return;

		int grhIdx = casco.Head[heading];

		// VB6: no per-heading X adjustment; mounted gets X+1
		float xAdj = ch.Mounted ? 1f : 0f;
		Vector2 helmetPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + HELMET_Y_OFFSET);

		DrawGrh(canvas, data, grhIdx, 0, helmetPos, true, colorOverride);
	}

	private static void DrawWeapon(
		Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
		int heading, GameData data, GrhAnimator animator,
		Color? colorOverride = null)
	{
		if (ch.WeaponAnim <= 0 || ch.WeaponAnim >= data.Weapons.Length) return;
		var weapon = data.Weapons[ch.WeaponAnim];
		int grhIndex = weapon.Walk[heading];
		if (grhIndex <= 0) return;

		// VB6: Arma.WeaponWalk drawn at PixelOffsetX, PixelOffsetY (same as body), center=1
		int frame = ch.Moving ? (int)ch.WalkFrame : 0;
		DrawGrh(canvas, data, grhIndex, frame, bodyPos, true, colorOverride);
	}

	private static void DrawShield(
		Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
		int heading, GameData data, GrhAnimator animator,
		Color? colorOverride = null)
	{
		if (ch.ShieldAnim <= 0 || ch.ShieldAnim >= data.Shields.Length) return;
		var shield = data.Shields[ch.ShieldAnim];
		int grhIndex = shield.Walk[heading];
		if (grhIndex <= 0) return;

		// VB6: Escudo.ShieldWalk drawn at PixelOffsetX, PixelOffsetY (same as body), center=1
		int frame = ch.Moving ? (int)ch.WalkFrame : 0;
		DrawGrh(canvas, data, grhIndex, frame, bodyPos, true, colorOverride);
	}

	/// <summary>
	/// Collect aura draw data for a character and queue to WorldRenderer's aura layer.
	/// The aura layer (z=1) draws ABOVE the content layer (z=0), so auras appear
	/// on top of layer 3 tiles and characters, with additive blend.
	/// Position: PixelOffsetX + HeadOffset.X, HeadOffset.Y + PixelOffsetY + 72 - offset
	/// Rotation: angle += 0.004 per frame if Giratoria, wraps at 180
	/// </summary>
	public static void CollectAuraDraws(
		WorldRenderer worldRenderer, Character ch, Vector2 pos, Vector2 headOffset,
		GameData data, double globalTimeMs, float alphaOverride = 1f)
	{
		if (data.Auras == null || data.Auras.Length <= 1) return;
		if (ch.Navigating) return; // No auras while on a boat

		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexA, ref ch.AuraAngleA, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexW, ref ch.AuraAngleW, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexE, ref ch.AuraAngleE, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexR, ref ch.AuraAngleR, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexC, ref ch.AuraAngleC, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.NpcAura, ref ch.NpcAuraAngle, globalTimeMs, alphaOverride);
	}

	private static void CollectSingleAura(
		WorldRenderer worldRenderer, Vector2 pos, Vector2 headOffset,
		GameData data, int auraIndex, ref float angle, double globalTimeMs, float alphaOverride = 1f)
	{
		if (!TryBuildAuraDraw(data, auraIndex, pos, headOffset, globalTimeMs, alphaOverride, out var draw))
			return;

		if (draw.Rotating)
			angle = draw.Angle;

		worldRenderer.QueueAuraDraw(draw.GrhIndex, draw.Frame, draw.Position, draw.Color, draw.Angle);
	}

	private static bool TryBuildAuraDraw(
		GameData data, int auraIndex, Vector2 pos, Vector2 headOffset,
		double globalTimeMs, float alpha, out AuraDrawData draw)
	{
		draw = default;
		if (auraIndex <= 0 || auraIndex >= data.Auras.Length) return false;

		var aura = data.Auras[auraIndex];
		if (aura.GrhIndex <= 0) return false;

		float drawAngle = aura.Giratoria ? CalculateAuraAngle(globalTimeMs) : 0f;
		int frame = GetTimedGrhFrame(data, aura.GrhIndex, globalTimeMs);
		var position = new Vector2(pos.X + headOffset.X, pos.Y + headOffset.Y + 72 - aura.Offset);
		var color = new Color(ByteToFloat.Table[aura.R], ByteToFloat.Table[aura.G], ByteToFloat.Table[aura.B], alpha);
		draw = new AuraDrawData(aura.GrhIndex, frame, position, color, drawAngle, aura.Giratoria);
		return true;
	}

	private static float CalculateAuraAngle(double globalTimeMs)
	{
		// VB6: Giratoria, 0.004 rad/frame at ~24 FPS = ~0.096 rad/sec.
		return (float)(globalTimeMs * 0.000096 % 180.0);
	}

	private static int GetTimedGrhFrame(GameData data, int grhIndex, double globalTimeMs)
	{
		if (grhIndex <= 0 || grhIndex >= data.Grhs.Length) return 0;
		var grh = data.Grhs[grhIndex];
		if (grh.NumFrames <= 1) return 0;

		float speed = grh.Speed > 0 ? grh.Speed : 100f;
		return (int)(globalTimeMs / speed % grh.NumFrames);
	}

	private static void DrawFx(
		Node2D canvas, Character ch, Vector2 pos,
		GameData data, GrhAnimator animator, float deltaMs)
	{
		// Timer advancement (FxFrameCounter, FxLoops) is done by UpdateCharacterTimers in _Process.
		// This method only reads the current frame state for rendering.
		for (int i = 0; i < 3; i++)
		{
			int fxIdx = ch.ActiveFxSlots[i];
			if (fxIdx <= 0 || fxIdx >= data.Fxs.Length) continue;

			var fx = data.Fxs[fxIdx];
			if (fx.Animacion <= 0) continue;

			int grhIndex = fx.Animacion;
			if (grhIndex <= 0 || grhIndex >= data.Grhs.Length) continue;
			var grh = data.Grhs[grhIndex];
			int numFrames = grh.NumFrames;

			int frame = numFrames <= 1 ? 0 : (int)Math.Min(ch.FxFrameCounter[i], numFrames - 1);
			Vector2 fxDrawPos = pos + new Vector2(fx.OffsetX, fx.OffsetY);
			DrawGrh(canvas, data, grhIndex, frame, fxDrawPos, true,
					new Color(1, 1, 1, 150f / 255f));
		}
	}

	/// <summary>
	/// Render character-attached particle streams centered on the character sprite.
	/// Particles are queued onto the additive blend layer (WorldRenderer) for proper VB6 glow.
	/// </summary>
	private static void DrawCharParticles(Node2D canvas, Character ch, Vector2 pos,
										   GameState state, GameData data,
										   double globalTimeMs,
										   WorldRenderer? worldRenderer = null,
										   int charIdx = -1)
	{
		if (charIdx < 0) return;

		foreach (var stream in state.MapParticles)
		{
			if (!stream.Active || stream.CharIndex != charIdx) continue;
			if (stream.DefIndex < 1 || stream.DefIndex >= state.ParticleDefs.Length) continue;
			var def = state.ParticleDefs[stream.DefIndex];

			// VB6: particles render at screenPos + particle offset (no centering)
			// Particle X1/Y1/X2/Y2 in Particles.ini already define the spawn offset

			foreach (var p in stream.Particles)
			{
				if (!p.Alive || p.GrhIndex <= 0) continue;
				float alpha = def.FadeAlpha && p.MaxLife > 0
					? p.Alpha * Math.Clamp(p.Life / p.MaxLife, 0f, 1f)
					: p.Alpha;
				var color = new Color(ByteToFloat.Table[p.ColR], ByteToFloat.Table[p.ColG], ByteToFloat.Table[p.ColB], alpha);
				Vector2 pPos = pos + new Vector2(p.X, p.Y);
				float angle = def.RotateVisual ? Mathf.DegToRad(p.Angle) : 0f;
				float scale = (!def.ScaleOverLife || p.MaxLife <= 0) ? 1f
					: def.ResizeX + (def.ResizeY - def.ResizeX) * Math.Clamp(1f - p.Life / p.MaxLife, 0f, 1f);

				// Use animated GRH frame (VB6: particles animate)
				int frame = 0;
				if (data.Grhs != null && p.GrhIndex > 0 && p.GrhIndex < data.Grhs.Length)
				{
					var grh = data.Grhs[p.GrhIndex];
					if (grh.NumFrames > 1)
					{
						float speed = grh.Speed > 0 ? grh.Speed : 100f;
						frame = (int)(globalTimeMs / speed % grh.NumFrames);
					}
				}

				if (worldRenderer != null)
				{
					// Queue onto additive blend layer for proper glow
					worldRenderer.QueueCharParticleDraw(p.GrhIndex, frame, pPos, color, angle, scale);
				}
				else
				{
					// Fallback: draw directly (no additive blend)
					DrawGrh(canvas, data, p.GrhIndex, frame, pPos, true, color);
				}
			}
		}
	}

	/// <summary>
	/// Advance all per-character timers once per frame. Must be called from _Process
	/// (or equivalent), NOT from _Draw, to prevent double-advancing when _Draw is
	/// called multiple times per frame.
	/// Covers: FovAlpha, TransparenciaBody, FxFrameCounter/FxLoops, dialog timers.
	/// </summary>
	/// <summary>
	/// Lightweight timer update for off-viewport characters — only FOV fade.
	/// Skips FX counters, transparency pulse, and other visual-only timers.
	/// </summary>
	public static void UpdateCharacterFovOnly(Character ch, float deltaMs, GameState state)
	{
		bool insideCore = IsInsideCoreViewport(ch.PosX, ch.PosY, state.UserPosX, state.UserPosY);
		float fovTarget = insideCore ? 1f : 0f;
		if (Math.Abs(ch.FovAlpha - fovTarget) > 0.001f)
		{
			float fovStep = FovFadeRate * deltaMs / 1000f;
			ch.FovAlpha = ch.FovAlpha < fovTarget
				? Math.Min(ch.FovAlpha + fovStep, fovTarget)
				: Math.Max(ch.FovAlpha - fovStep, fovTarget);
		}
	}

	public static void UpdateCharacterTimers(Character ch, float deltaMs, GameState state, GameData data)
	{
		// ── FOV fade ──
		int userX = state.UserPosX;
		int userY = state.UserPosY;
		bool insideCore = IsInsideCoreViewport(ch.PosX, ch.PosY, userX, userY);
		float fovTarget = insideCore ? 1f : 0f;
		float fovStep = FovFadeRate * deltaMs / 1000f;
		if (ch.FovAlpha < fovTarget)
			ch.FovAlpha = Math.Min(ch.FovAlpha + fovStep, fovTarget);
		else if (ch.FovAlpha > fovTarget)
			ch.FovAlpha = Math.Max(ch.FovAlpha - fovStep, fovTarget);

		// ── Transparency pulse (dead / invisible) ──
		if (ch.Dead || ch.Invisible)
		{
			float speed = deltaMs * 0.035f;
			if (!ch.Llegoalatransp)
			{
				ch.TransparenciaBody = Math.Min(ch.TransparenciaBody + speed, 53f);
				if (ch.TransparenciaBody >= 53f) ch.Llegoalatransp = true;
			}
			else
			{
				ch.TransparenciaBody = Math.Max(ch.TransparenciaBody - speed, 18f);
				if (ch.TransparenciaBody <= 18f) ch.Llegoalatransp = false;
			}
		}

		// ── FX frame counters ──
		if (data.Fxs != null && data.Grhs != null)
		{
			for (int i = 0; i < 3; i++)
			{
				int fxIdx = ch.ActiveFxSlots[i];
				if (fxIdx <= 0 || fxIdx >= data.Fxs.Length) continue;

				var fx = data.Fxs[fxIdx];
				if (fx.Animacion <= 0) continue;

				int grhIndex = fx.Animacion;
				if (grhIndex <= 0 || grhIndex >= data.Grhs.Length) continue;
				var grh = data.Grhs[grhIndex];
				int numFrames = grh.NumFrames;
				float speed = grh.Speed > 0 ? grh.Speed : 100f;

				if (numFrames <= 1)
				{
					// Static FX — decrement loop counter once per frame
					if (ch.FxLoops[i] != -1)
					{
						ch.FxLoops[i]--;
						if (ch.FxLoops[i] <= 0)
						{
							ch.ActiveFxSlots[i] = 0;
							ch.FxLoops[i] = 0;
							ch.FxFrameCounter[i] = 0;
						}
					}
				}
				else
				{
					// Animated FX — advance frame counter
					ch.FxFrameCounter[i] += deltaMs * numFrames / speed;

					if (ch.FxFrameCounter[i] >= numFrames)
					{
						if (ch.FxLoops[i] == -1)
						{
							ch.FxFrameCounter[i] %= numFrames;
						}
						else
						{
							ch.FxLoops[i]--;
							if (ch.FxLoops[i] <= 0)
							{
								ch.ActiveFxSlots[i] = 0;
								ch.FxLoops[i] = 0;
								ch.FxFrameCounter[i] = 0;
							}
							else
							{
								ch.FxFrameCounter[i] %= numFrames;
							}
						}
					}
				}
			}
		}

		// ── Dialog timers ──
		if (!string.IsNullOrEmpty(ch.DialogText))
		{
			float dtFactor = deltaMs / 16.667f;
			long now = System.Environment.TickCount64;
			long elapsed = now - ch.DialogStartMs;

			if (ch.DialogDurationMs >= 292)
			{
				if (ch.DialogRiseCounter > 0)
					ch.DialogRiseCounter = Math.Max(0, ch.DialogRiseCounter - dtFactor);
				if (ch.DialogRiseCounter > 0)
					ch.DialogAlpha = Math.Min(255f, ch.DialogAlpha + 12f * dtFactor);
			}

			if (elapsed >= ch.DialogDurationMs && !ch.DialogFading)
				ch.DialogFading = true;

			if (ch.DialogFading)
			{
				ch.DialogAlpha = Math.Max(0, ch.DialogAlpha - 10f * dtFactor);
				if (ch.DialogAlpha <= 9f)
					ch.DialogText = "";
			}
		}
	}

}
