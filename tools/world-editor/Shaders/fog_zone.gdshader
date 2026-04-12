shader_type canvas_item;

// Zone niebla — animated world-anchored noise fog over tiles inside
// zones with Niebla=true. Drift is controlled by `speed` (set 0,0 for
// a completely static pattern).
//
// Kept deliberately simple — no player break, no free_smoke branch —
// those are disabled for now while we stabilize the rendering.
// They'll be reintroduced once this base is confirmed working.

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D zone_mask : filter_linear;
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
    float m = texture(zone_mask, mask_uv).r;
    if (m <= 0.001) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    vec2 n_uv = world_px / 512.0 + speed * TIME;
    float n = texture(noise_texture, n_uv).r;
    float fog = clamp(n * 2.0 - 1.0, 0.0, 1.0);

    COLOR = vec4(fog_color.rgb, fog * density * m);
}
