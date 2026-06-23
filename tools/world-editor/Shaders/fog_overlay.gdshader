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

// Size of one noise "cell" in world pixels. Larger = bigger / softer
// smoke clouds with fewer features visible on screen. Smaller = denser,
// more granular pattern. 512 matches the old hardcoded value.
uniform float noise_scale = 512.0;

// Per-layer randomness — set by the renderer from PaintedFogLayer.RandomSeed.
// noise_seed offsets where this layer samples the noise texture, so two
// layers with identical settings still LOOK different.
// wind_angle_offset rotates the drift direction slightly so layers don't
// all glide in the same direction at the same time. Zero = no offset
// (backward compat with old .aofog files that don't have a seed).
uniform vec2 noise_seed = vec2(0.0, 0.0);
uniform float wind_angle_offset = 0.0;

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;
    float m = texture(fog_mask, mask_uv).r;

    // Rotate the wind direction by the per-layer angle offset so layers
    // don't drift in unison. Each layer picks a small ±0.5 rad rotation
    // from its RandomSeed.
    float ca = cos(wind_angle_offset);
    float sa = sin(wind_angle_offset);
    vec2 rotated_speed = vec2(speed.x * ca - speed.y * sa,
                              speed.x * sa + speed.y * ca);
    vec2 wind = rotated_speed * TIME;
    vec2 hover = vec2(sin(TIME * 0.25 + noise_seed.x),
                      cos(TIME * 0.18 + noise_seed.y)) * 0.12;
    vec2 offset = mix(wind, hover, clamp(free_smoke, 0.0, 1.0));

    // Guard against divide-by-zero if someone sets noise_scale to 0.
    float ns = max(noise_scale, 1.0);
    // Add per-layer noise_seed so identical-style layers sample different
    // regions of the noise texture and look unique.
    vec2 n_uv = world_px / ns + offset + noise_seed;
    float n = texture(noise_texture, n_uv).r;
    float fog = clamp(n * 2.0 - 1.0, 0.0, 1.0);

    COLOR = vec4(fog_color.rgb, fog * density * m);
}
