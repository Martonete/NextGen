shader_type canvas_item;

// Painted smoke ("Humo") — two modes controlled by humo_animated:
//   0.0 → solid saturated fill (clean click-painted look the user loved)
//   1.0 → animated noise pattern using humo_speed drift
//
// Both modes use the mask for per-tile visibility and humo_color for tint.
// Layer 2/3/4-occluded tiles are already subtracted in the C# mask builder.

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D humo_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size = vec2(3200.0, 3200.0);

uniform float humo_density = 0.6;
uniform vec4 humo_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);
uniform float humo_animated = 0.0;     // 0 = solid, 1 = animated noise
uniform vec2 humo_speed = vec2(0.02, 0.01);

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;
    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;

    float m = texture(humo_mask, mask_uv).r;
    if (m <= 0.001) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float alpha = humo_density;
    if (humo_animated > 0.5) {
        vec2 n_uv = world_px / 512.0 + humo_speed * TIME;
        float n = texture(noise_texture, n_uv).r;
        float f = clamp(n * 2.0 - 1.0, 0.0, 1.0);
        alpha *= f;
    }
    COLOR = vec4(humo_color.rgb, alpha * m);
}
