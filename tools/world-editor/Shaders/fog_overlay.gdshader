shader_type canvas_item;

// STEP 1 — mask-based visibility. Red wherever the mask is set.
// If you see red ONLY on tiles you painted (or inside a zone with
// niebla), the mask + UV math works and we can add the real fog
// rendering (noise, color, density, player break, free smoke) next.

uniform sampler2D fog_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size = vec2(3200.0, 3200.0);

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;
    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;

    float m = texture(fog_mask, mask_uv).r;
    COLOR = vec4(1.0, 0.0, 0.0, m * 0.6);
}
