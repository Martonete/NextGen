shader_type canvas_item;

// World-anchored animated fog with per-tile mask + player disruption.
//
// Two movement modes (free_smoke uniform):
//   0 → unidirectional wind drift (speed * TIME shifts noise)
//   1 → domain warping for swirly multi-directional "real smoke"

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D fog_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size  = vec2(3200.0, 3200.0);

uniform float density : hint_range(0.0, 1.0) = 0.35;
uniform vec2 speed = vec2(0.02, 0.01);
uniform vec4 fog_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);
uniform float noise_world_scale : hint_range(64.0, 2048.0) = 512.0;

// Free-smoke mode: int for maximum driver compatibility (some Godot
// builds have issues with bool uniforms). 0 = off, non-zero = on.
uniform int free_smoke = 0;
uniform float free_smoke_intensity : hint_range(0.0, 1.5) = 0.55;

uniform vec2 player_world_pos = vec2(-100000.0, -100000.0);
uniform float player_break_radius : hint_range(0.0, 512.0) = 144.0;

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    // --- Fog mask (per-tile) ---
    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;
    float mask = 0.0;
    if (mask_uv.x >= 0.0 && mask_uv.x <= 1.0 && mask_uv.y >= 0.0 && mask_uv.y <= 1.0) {
        mask = texture(fog_mask, mask_uv).r;
    }
    if (mask <= 0.001) {
        COLOR = vec4(0.0);
        return;
    }

    // --- Noise pattern, world-anchored ---
    vec2 base_uv = world_px / noise_world_scale;
    vec2 n_uv = base_uv + speed * TIME;

    if (free_smoke > 0) {
        // Domain warp: sample noise at two offset positions to build a
        // 2D displacement field, then use it to warp the main sample.
        // Each lookup drifts at a slightly different speed so the
        // displacement evolves over time → organic swirls.
        float t = TIME * 0.12;
        vec2 w1 = base_uv + vec2(t, 0.0);
        vec2 w2 = base_uv + vec2(0.0, t);
        float wx = texture(noise_texture, w1).r * 2.0 - 1.0;
        float wy = texture(noise_texture, w2).r * 2.0 - 1.0;
        vec2 q = vec2(wx, wy) * free_smoke_intensity;
        n_uv = base_uv + q + vec2(0.0, t * 0.3);
    }

    float n = texture(noise_texture, n_uv).r;
    float fog = clamp(n * 2.0 - 1.0, 0.0, 1.0);

    // --- Player break: disperse fog near the character ---
    float pd = length(world_px - player_world_pos);
    float player_fade = smoothstep(0.0, player_break_radius, pd);

    COLOR = vec4(fog_color.rgb, fog * density * mask * player_fade);
}
