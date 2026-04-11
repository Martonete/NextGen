shader_type canvas_item;

// World-space animated fog. The ColorRect is positioned at the zone's
// world rectangle PLUS padding, so the fog bleeds past the zone edge.
// Shader applies a smoothstep alpha fade near the rect edges (in UV
// space) so the fog fades out naturally at its borders — no hard line.
// Speed * TIME drifts the noise so the pattern animates subtly.

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float density : hint_range(0.0, 1.0) = 0.35;
uniform vec2 speed = vec2(0.02, 0.01);
uniform vec4 fog_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);
uniform float noise_scale : hint_range(0.1, 8.0) = 1.5;
// Width of the soft edge fade in UV units. Pushed per-rect so the
// absolute fade width stays constant regardless of zone size.
uniform float edge_fade_x : hint_range(0.0, 0.5) = 0.15;
uniform float edge_fade_y : hint_range(0.0, 0.5) = 0.15;

void fragment() {
    vec2 uv = UV * noise_scale + speed * TIME;
    float noise = texture(noise_texture, uv).r;
    float fog = clamp(noise * 2.0 - 1.0, 0.0, 1.0);

    // Soft fade near the rect edges so the fog doesn't end with a
    // visible rectangle border. The smoothstep is symmetric on
    // both sides of each axis.
    float fl = smoothstep(0.0, edge_fade_x, UV.x);
    float fr = smoothstep(0.0, edge_fade_x, 1.0 - UV.x);
    float ft = smoothstep(0.0, edge_fade_y, UV.y);
    float fb = smoothstep(0.0, edge_fade_y, 1.0 - UV.y);
    float edge = fl * fr * ft * fb;

    COLOR = vec4(fog_color.rgb, fog * density * edge);
}
