shader_type canvas_item;
render_mode blend_add;

// VB6 Argentum particle sprites are fully opaque brown textures (no alpha).
// The texture provides shape/intensity via brightness; the vertex color
// (COLOR) provides the particle tint from Particles.ini colorsets.
//
// - Texture brightness → intensity multiplier for the particle color
// - smoothstep threshold → dark edges become fully transparent (soft glow)
// - Bright center → visible, correctly colored particle

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	float intensity = max(tex.r, max(tex.g, tex.b));
	float alpha = smoothstep(0.15, 0.55, intensity);
	COLOR = vec4(COLOR.rgb * intensity, COLOR.a * alpha);
}
