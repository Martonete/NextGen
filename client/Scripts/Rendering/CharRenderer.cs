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
	private const int GmTeleportAuraIndex = 103;
	private const float GmTeleportAuraDuration = 0.9f;
	// Helmets use the same head anchor as Cabezas.ind. Older code subtracted 34px here,
	// which made TS AO helmets float above the character.
	private const int HELMET_Y_OFFSET = 1;

	// Static buffers for DrawShadowProjected — reused every call to avoid per-frame allocations
	private static readonly Vector2[] _shadowVerts = new Vector2[4];
	private static readonly Color[] _shadowColors = new Color[4];
	private static readonly List<Ao20ShadowRenderer.SpritePart> _ao20ShadowParts = new(5);

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
	/// Fade speed from AO Libre's MapContainer (FADE_DURATION = 0.4s).
	/// A slightly longer transition prevents creatures from popping at the
	/// peripheral vision boundary.
	/// </summary>
	private const float FovFadeRate = 2.5f;

	/// <summary>
	/// Check whether a character is in the expanded creature range.  AO Libre
	/// keeps creatures visible slightly beyond the opaque visual core, avoiding
	/// pop-in while the peripheral darkness is fading them out.
	/// </summary>
	private static bool IsInsideCoreViewport(int charPosX, int charPosY, int userX, int userY)
	{
		return VisionRange.IsInsideCreatureView(charPosX, charPosY, userX, userY);
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
			DrawAo20Shadow(canvas, ch, screenPos, heading, data, state!, charTileX, charTileY, charIdx);

		// VB6: invisible self = pulsing transparency (TransparenciaBody 0-100)
		// Combined with FOV fade alpha for smooth boundary transitions
		Color? invisOverride = null;
		if (ch.Invisible)
			invisOverride = new Color(1, 1, 1, ch.TransparenciaBody / 100f * fovAlpha);
		else if (fovAlpha < 1f)
			invisOverride = new Color(1, 1, 1, fovAlpha);

		// Combat hit flash — brief tint that overrides the normal modulate (skipped
		// while invisible). Fades out as the timer decays. Received = red (hurt),
		// dealt = brighten (the struck victim). Alpha stays at the FOV fade value.
		if (ch.HitFlashTimer > 0f && !ch.Invisible)
		{
			float k = Math.Clamp(ch.HitFlashTimer / Character.HitFlashDuration, 0f, 1f);
			invisOverride = ch.HitFlashReceived
				? new Color(1f, 1f - 0.65f * k, 1f - 0.65f * k, fovAlpha)   // red tint
				: new Color(1f + 0.7f * k, 1f + 0.7f * k, 1f + 0.7f * k, fovAlpha); // bright flash
		}

		// Heading-dependent draw order (VB6: dibujarPersonaje)
		// No walk bob: AO2020 has no vertical bounce, the body sprites carry
		// whatever motion the stride needs.
		DrawCharParts(canvas, ch, screenPos, headOffset, heading, data, animator, state,
					  colorOverride: invisOverride);

		// FX overlays — not drawn when invisible (VB6: entire char skipped in invisible branch)
		if (!ch.Invisible)
			DrawFx(canvas, ch, screenPos, data, animator, deltaMs);

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
		int bodyFrame = ch.WalkPoseActive ? (int)ch.WalkFrame : 0;
		Vector2 bodyRegistration = data.WalkOffset(bodyGrh, bodyFrame);
		var bodyRes = data.ResolveGrh(bodyGrh, bodyFrame);
		if (bodyRes == null || bodyRes.FileNum <= 0) return;

		// Body draw position (with centering)
		float bodyDrawX = screenPos.X + bodyRegistration.X;
		float bodyDrawY = screenPos.Y + bodyRegistration.Y;
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
				int weapFrame = EquipmentFrame(ch, data, weapGrh, heading);
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
				int shieldFrame = EquipmentFrame(ch, data, shieldGrh, heading);
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
	/// Builds AO20's 256x256 composed character silhouette and projects that one
	/// texture through clsBatch.DrawShadow's geometry. The normal character still
	/// uses DrawCharParts below; only its shadow is composited.
	/// </summary>
	private static void DrawAo20Shadow(Node2D canvas, Character ch, Vector2 screenPos, int heading,
		GameData data, GameState state, int charTileX, int charTileY, int charIdx)
	{
		if (ch.Body <= 0 || ch.Body >= data.Bodies.Length || charTileX <= 0 || charTileY <= 0) return;
		_ao20ShadowParts.Clear();
		Vector2 bodyPos = Ao20ShadowRenderer.CompositeAnchor;
		Vector2 headOffset = new(data.Bodies[ch.Body].HeadOffsetX, data.Bodies[ch.Body].HeadOffsetY);
		headOffset += HeadAttachmentAdjustment(ch, data, heading);
		int walkingFrame = ch.WalkPoseActive ? (int)ch.WalkFrame : 0;
		int bodyGrh = data.Bodies[ch.Body].Walk[heading];
		int headGrh = ch.Head > 0 && ch.Head < data.Heads.Length ? data.Heads[ch.Head].Head[heading] : 0;
		int helmetGrh = ch.CascoAnim > 0 && ch.CascoAnim < data.Cascos.Length ? data.Cascos[ch.CascoAnim].Head[heading] : 0;
		int weaponGrh = ch.WeaponAnim > 0 && ch.WeaponAnim < data.Weapons.Length ? data.Weapons[ch.WeaponAnim].Walk[heading] : 0;
		int shieldGrh = ch.ShieldAnim > 0 && ch.ShieldAnim < data.Shields.Length ? data.Shields[ch.ShieldAnim].Walk[heading] : 0;

		void AddPart(int grhIndex, int frame, Vector2 anchor)
		{
			if (grhIndex <= 0) return;
			if (grhIndex == bodyGrh) anchor += data.WalkOffset(grhIndex, frame);
			else if (grhIndex == weaponGrh || grhIndex == shieldGrh)
				frame = EquipmentFrame(ch, data, grhIndex, heading);
			var resolved = data.ResolveGrh(grhIndex, frame);
			if (resolved == null || resolved.FileNum <= 0) return;
			var texture = data.Textures?.GetTexture(resolved.FileNum);
			if (texture == null) return;
			float x = anchor.X, y = anchor.Y;
			if (resolved.TileWidth != 1f && resolved.TileWidth > 0) x -= (int)(resolved.TileWidth * 16f) - 16;
			if (resolved.TileHeight != 1f && resolved.TileHeight > 0) y -= (int)(resolved.TileHeight * 32f) - 32;
			_ao20ShadowParts.Add(new Ao20ShadowRenderer.SpritePart(resolved, texture, new Vector2(x, y)));
		}

		Vector2 headPos = bodyPos + new Vector2(headOffset.X + (ch.Mounted ? 1f : 0f), headOffset.Y + 1f);
		Vector2 helmetPos = bodyPos + new Vector2(headOffset.X + (ch.Mounted ? 1f : 0f), headOffset.Y + HELMET_Y_OFFSET);

		switch (heading)
		{
			case 1: AddPart(weaponGrh, walkingFrame, bodyPos); AddPart(shieldGrh, walkingFrame, bodyPos); AddPart(bodyGrh, walkingFrame, bodyPos); AddPart(headGrh, 0, headPos); AddPart(helmetGrh, 0, helmetPos); break;
			case 2: AddPart(shieldGrh, walkingFrame, bodyPos); AddPart(bodyGrh, walkingFrame, bodyPos); AddPart(headGrh, 0, headPos); AddPart(helmetGrh, 0, helmetPos); AddPart(weaponGrh, walkingFrame, bodyPos); break;
			case 3: AddPart(bodyGrh, walkingFrame, bodyPos); AddPart(headGrh, 0, headPos); AddPart(helmetGrh, 0, helmetPos); AddPart(weaponGrh, walkingFrame, bodyPos); AddPart(shieldGrh, walkingFrame, bodyPos); break;
			case 4: AddPart(weaponGrh, walkingFrame, bodyPos); AddPart(bodyGrh, walkingFrame, bodyPos); AddPart(headGrh, 0, headPos); AddPart(helmetGrh, 0, helmetPos); AddPart(shieldGrh, walkingFrame, bodyPos); break;
		}

		int cacheId = charIdx >= 0 ? charIdx : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ch);
		Ao20ShadowRenderer.DrawCharacterShadow(canvas, cacheId, _ao20ShadowParts, screenPos,
			Ao20ShadowRenderer.GetLightCorners(state, charTileX, charTileY));
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
		headOffset += HeadAttachmentAdjustment(ch, data, heading);
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
		int frame = ch.WalkPoseActive ? (int)ch.WalkFrame : 0;
		pos += data.WalkOffset(bodyGrh, frame);

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
		int frame = EquipmentFrame(ch, data, grhIndex, heading);
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
		int frame = EquipmentFrame(ch, data, grhIndex, heading);
		DrawGrh(canvas, data, grhIndex, frame, bodyPos, true, colorOverride);
	}

	// These imported collars need the head seated slightly deeper. Keep this
	// separate from body registration: moving the whole sprite would undo the
	// stable walk anchor. Head and helmet share it in normal/reflection draws.
	private static Vector2 HeadAttachmentAdjustment(Character ch, GameData data, int heading)
	{
		if (ch.Mounted || ch.Navigating || ch.Dead || ch.Body >= data.Bodies.Length
			|| (ch.Body != 512 && ch.Body != 513)) return Vector2.Zero;
		int grh = data.Bodies[ch.Body].Walk[heading];
		return data.WalkRegistration.ContainsKey(grh) ? new Vector2(0, 2) : Vector2.Zero;
	}

	private static int EquipmentFrame(Character ch, GameData data, int grhIndex, int heading)
	{
		if (!ch.WalkPoseActive || grhIndex <= 0 || grhIndex >= data.Grhs.Length
			|| ch.Body <= 0 || ch.Body >= data.Bodies.Length) return 0;
		int bodyGrh = data.Bodies[ch.Body].Walk[heading];
		if (bodyGrh <= 0 || bodyGrh >= data.Grhs.Length) return 0;
		return WalkSpriteLayout.Frame(ch.WalkFrame, data.Grhs[bodyGrh].NumFrames, data.Grhs[grhIndex].NumFrames);
	}

	/// <summary>
	/// Collect legacy GRH-sprite aura draws for a character and queue them to the
	/// additive aura layer, which is added before ContentLayer — so they render after
	/// L2 but behind characters AND behind trees, which is the look these have always had.
	/// Their source art is a black-backed sprite that only keys out under additive blend,
	/// so they must stay on that layer. Procedural auras take a different path:
	/// DrawProceduralAurasInline, drawn with the character so trees occlude them too.
	/// Position: PixelOffsetX + HeadOffset.X, HeadOffset.Y + PixelOffsetY + 72 - offset
	/// Rotation: angle += 0.004 per frame if Giratoria, wraps at 180
	/// </summary>
	public static void CollectAuraDraws(
		WorldRenderer worldRenderer, Character ch, Vector2 pos, Vector2 headOffset,
		GameData data, double globalTimeMs, float alphaOverride = 1f)
	{
		if (data.Auras == null || data.Auras.Length <= 1) return;
		if (ch.Navigating) return; // No auras while on a boat
		if (ch.PreviewAuraIndex > 0)
		{
			float previewAngle = 0;
			CollectSingleAura(worldRenderer, pos, headOffset, data, ch.PreviewAuraIndex, ref previewAngle, globalTimeMs, alphaOverride);
			return;
		}

		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexA, ref ch.AuraAngleA, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexW, ref ch.AuraAngleW, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexE, ref ch.AuraAngleE, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexR, ref ch.AuraAngleR, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexC, ref ch.AuraAngleC, globalTimeMs, alphaOverride);
		CollectSingleAura(worldRenderer, pos, headOffset, data, ch.NpcAura, ref ch.NpcAuraAngle, globalTimeMs, alphaOverride);
	}

	/// <summary>
	/// Queue an aura draw for an item lying on the ground (obj.dat CreaAura).
	/// Unlike character auras, ground items have no head/body — the glow is
	/// centered on the tile itself, offset upward by the aura's own Offset field.
	/// </summary>
	public static void CollectGroundAuraDraw(
		WorldRenderer worldRenderer, GameData data, int auraIndex, Vector2 tilePos, double globalTimeMs)
	{
		if (data.Auras == null || auraIndex <= 0 || auraIndex >= data.Auras.Length) return;

		var aura = data.Auras[auraIndex];
		if (aura.ProceduralStyle > 0)
		{
			worldRenderer.QueueAuraDraw(-auraIndex, 0, tilePos + new Vector2(16, 16 - aura.Offset), Colors.White, 0);
			return;
		}
		if (aura.GrhIndex <= 0) return;

		float drawAngle = aura.Giratoria ? CalculateAuraAngle(globalTimeMs) : 0f;
		int frame = GetTimedGrhFrame(data, aura.GrhIndex, globalTimeMs);

		// DrawGrh's "center" mode bottom-anchors sprites taller than 1 tile (so a
		// standing character's aura grows upward from their feet). Ground items lie
		// flat on a single tile, so counteract that bottom-anchor and instead center
		// the aura vertically on the tile, same as the item sprite itself.
		float tileHeight = aura.GrhIndex < data.Grhs.Length ? data.Grhs[aura.GrhIndex].TileHeight : 1f;
		float verticalCenterFix = tileHeight > 1f ? (tileHeight - 1f) * (TileSize / 2f) : 0f;

		var position = new Vector2(tilePos.X, tilePos.Y - aura.Offset + verticalCenterFix);
		var color = new Color(ByteToFloat.Table[aura.R], ByteToFloat.Table[aura.G], ByteToFloat.Table[aura.B], 1f);
		worldRenderer.QueueAuraDraw(aura.GrhIndex, frame, position, color, drawAngle);
	}

	/// <summary>
	/// Procedural auras (ProceduralStyle > 0), drawn inline in the per-tile character pass
	/// — the same spot DrawBindingEffect uses — instead of being queued onto the additive
	/// aura layers. The front half used to live on AuraFrontLayer, which is added AFTER
	/// ContentLayer and therefore painted straight over trees. Drawing here means a tree
	/// emitted later in the tile loop covers the aura exactly like it covers the character.
	///
	/// Safe to leave the additive layer because these are vector strokes with real alpha,
	/// unlike the legacy GRH sprite auras (black-backed art that needs additive to key out;
	/// those stay on AuraAdditiveLayer, which already draws behind trees anyway).
	/// </summary>
	public static void DrawProceduralAurasInline(
		CanvasItem canvas, Character ch, Vector2 pos,
		GameData data, double globalTimeMs, float alphaOverride, bool front)
	{
		if (data.Auras == null || data.Auras.Length <= 1) return;
		if (ch.Navigating) return; // No auras while on a boat

		DrawGmTeleportAura(canvas, ch, pos, data, alphaOverride, globalTimeMs, front);

		if (ch.PreviewAuraIndex > 0)
		{
			DrawProceduralAura(canvas, pos, data, ch.PreviewAuraIndex, globalTimeMs, alphaOverride, front);
			return;
		}

		DrawProceduralAura(canvas, pos, data, ch.AuraIndexA, globalTimeMs, alphaOverride, front);
		DrawProceduralAura(canvas, pos, data, ch.AuraIndexW, globalTimeMs, alphaOverride, front);
		DrawProceduralAura(canvas, pos, data, ch.AuraIndexE, globalTimeMs, alphaOverride, front);
		DrawProceduralAura(canvas, pos, data, ch.AuraIndexR, globalTimeMs, alphaOverride, front);
		DrawProceduralAura(canvas, pos, data, ch.AuraIndexC, globalTimeMs, alphaOverride, front);
		DrawProceduralAura(canvas, pos, data, ch.NpcAura, globalTimeMs, alphaOverride, front);
	}

	private static void DrawProceduralAura(
		CanvasItem canvas, Vector2 pos, GameData data, int auraIndex,
		double globalTimeMs, float alphaOverride, bool front)
	{
		if (auraIndex <= 0 || auraIndex >= data.Auras.Length) return;
		var aura = data.Auras[auraIndex];
		if (aura.ProceduralStyle <= 0) return; // legacy GRH sprite — additive layer handles it
		RunicAuraRenderer.Draw(canvas, aura, pos + new Vector2(16, 27 - aura.Offset),
			globalTimeMs, alphaOverride, front);
	}

	/// <summary>
	/// One-shot halo the server asks for with CreateFX 207 after a GM warp.
	/// It is not one of the equipped aura slots: it runs on its own timer, so the
	/// alpha is computed here (fade in, hold, fade out) instead of coming from
	/// TryBuildAuraDraw. Procedural only, so it draws inline like the rest of them.
	/// </summary>
	private static void DrawGmTeleportAura(
		CanvasItem canvas, Character ch, Vector2 pos, GameData data, float alphaOverride,
		double globalTimeMs, bool front)
	{
		if (ch.GmTeleportAuraTime < 0f || GmTeleportAuraIndex >= data.Auras.Length) return;

		var aura = data.Auras[GmTeleportAuraIndex];
		if (aura.ProceduralStyle <= 0) return;

		float age = ch.GmTeleportAuraTime;
		float alpha = alphaOverride
			* Math.Min(1f, age / 0.12f)
			* Math.Min(1f, (GmTeleportAuraDuration - age) / 0.35f);
		if (alpha <= 0.01f) return;

		RunicAuraRenderer.Draw(canvas, aura, pos + new Vector2(16, 27 - aura.Offset),
			globalTimeMs, alpha, front);
	}

	private static void CollectSingleAura(
		WorldRenderer worldRenderer, Vector2 pos, Vector2 headOffset,
		GameData data, int auraIndex, ref float angle, double globalTimeMs, float alphaOverride = 1f)
	{
		if (!TryBuildAuraDraw(data, auraIndex, pos, headOffset, globalTimeMs, alphaOverride, out var draw))
			return;

		// Procedural auras (negative index) draw inline with the character instead —
		// see DrawProceduralAurasInline. Only GRH sprite auras belong on the additive layer.
		if (draw.GrhIndex < 0) return;

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
		if (aura.ProceduralStyle > 0)
		{
			draw = new AuraDrawData(-auraIndex, 0, pos + new Vector2(16, 27 - aura.Offset),
				new Color(1, 1, 1, alpha), 0, false);
			return true;
		}
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
		// A one-shot warp halo far off-screen would otherwise sit frozen at age 0
		// and replay the moment the character walks back into the viewport.
		ch.GmTeleportAuraTime = -1f;

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
		// ── Combat hit flash decay ──
		if (ch.HitFlashTimer > 0f)
			ch.HitFlashTimer = Math.Max(0f, ch.HitFlashTimer - deltaMs / 1000f);

		// ── GM teleport aura (FX 207): plays once and switches itself off ──
		if (ch.GmTeleportAuraTime >= 0f)
		{
			ch.GmTeleportAuraTime += Math.Max(0f, deltaMs) / 1000f;
			if (ch.GmTeleportAuraTime >= GmTeleportAuraDuration)
				ch.GmTeleportAuraTime = -1f;
		}

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
