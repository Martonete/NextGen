shader_type canvas_item;

// DIAGNOSTIC / MINIMAL VERSION.
// Outputs semi-transparent red wherever the fog mask is set. If you see
// red over your zone/painted fog tiles, the shader pipeline works and
// the issue is in the more complex variant. If you STILL see white,
// the shader is not loading or the ColorRect is falling back to its
// default (white) color.

uniform sampler2D fog_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size = vec2(3200.0, 3200.0);

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;
    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;

    // Outside map bounds → fully transparent
    if (mask_uv.x < 0.0 || mask_uv.x > 1.0 || mask_uv.y < 0.0 || mask_uv.y > 1.0) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float m = texture(fog_mask, mask_uv).r;
    // Diagnostic: red wherever mask is set, transparent elsewhere
    COLOR = vec4(1.0, 0.0, 0.0, m * 0.5);
}
