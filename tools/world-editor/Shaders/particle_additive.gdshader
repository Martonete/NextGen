shader_type canvas_item;
render_mode blend_add;

// Particle additive glow shader.
// VB6 Argentum particle sprites are fully opaque brown textures with no alpha.
// This shader derives alpha from pixel brightness so dark edges become transparent
// and bright centers create a soft glow effect — matching the original visual.

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	float brightness = max(tex.r, max(tex.g, tex.b));
	COLOR = vec4(tex.rgb * COLOR.rgb, brightness * COLOR.a);
}
