shader_type canvas_item;

// Unified fog/smoke shader used by BOTH painted humo and zone niebla.
// Two separate ShaderMaterial instances share this file with independent
// uniform values.
//
// World-anchored noise — pattern stays glued to the tiles regardless of
// camera pan. Set `speed` to (0, 0) for a completely static look.
//
// IMPORTANT: no early `return` — Godot 4.6+ rejects that in fragment()
// as a compile error. Instead we multiply by the mask at the end so
// m=0 tiles naturally become fully transparent.

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D fog_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size = vec2(3200.0, 3200.0);

uniform float density = 0.35;
uniform vec2 speed = vec2(0.02, 0.01);
uniform vec4 fog_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;
    float m = texture(fog_mask, mask_uv).r;

    vec2 n_uv = world_px / 512.0 + speed * TIME;
    float n = texture(noise_texture, n_uv).r;
    float fog = clamp(n * 2.0 - 1.0, 0.0, 1.0);

    COLOR = vec4(fog_color.rgb, fog * density * m);
}
