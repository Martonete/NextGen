shader_type canvas_item;

// Unified fog/smoke shader used by BOTH painted humo and zone niebla.
// Two separate ShaderMaterial instances share this file with independent
// uniform values (each has its own fog_mask, fog_color, density, speed).
//
// free_smoke modes:
//   < 0.5 → linear wind drift (speed * TIME — pattern drifts in a
//           fixed direction)
//   > 0.5 → "float in place": noise offset oscillates with sin/cos of
//           TIME so the pattern morphs/animates but has NO net drift.
//           Like real smoke hanging in still air.
//
// IMPORTANT: no early `return` — Godot 4.6+ rejects early return in
// fragment(). We multiply by the mask at the end so m=0 tiles become
// fully transparent without branching.

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D fog_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size = vec2(3200.0, 3200.0);

uniform float density = 0.35;
uniform vec2 speed = vec2(0.02, 0.01);
uniform vec4 fog_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);

uniform float free_smoke = 0.0;

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;
    float m = texture(fog_mask, mask_uv).r;

    // Two possible noise-sample offsets: linear wind drift, or
    // bounded oscillation for "floating in place". mix() blends
    // between them based on free_smoke ∈ [0, 1].
    vec2 wind = speed * TIME;
    vec2 hover = vec2(sin(TIME * 0.25), cos(TIME * 0.18)) * 0.12;
    vec2 offset = mix(wind, hover, clamp(free_smoke, 0.0, 1.0));

    vec2 n_uv = world_px / 512.0 + offset;
    float n = texture(noise_texture, n_uv).r;
    float fog = clamp(n * 2.0 - 1.0, 0.0, 1.0);

    COLOR = vec4(fog_color.rgb, fog * density * m);
}
