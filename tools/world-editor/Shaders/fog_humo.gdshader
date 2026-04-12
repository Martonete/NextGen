shader_type canvas_item;

// Painted smoke ("Humo") — clean solid-fill mode that the user loved
// in step 1. No noise, no animation, no player break; just the mask
// tinted by humo_color at humo_density. Layer 3/4 tiles are already
// subtracted in the C# mask builder so trees/roofs poke through.

uniform sampler2D humo_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size = vec2(3200.0, 3200.0);

uniform float humo_density = 0.6;
uniform vec4 humo_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;
    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;

    float m = texture(humo_mask, mask_uv).r;
    COLOR = vec4(humo_color.rgb, m * humo_density);
}
