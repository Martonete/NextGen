shader_type canvas_item;

// See the world-editor copy for documentation.

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D fog_mask : filter_linear;
uniform vec2 map_tile_size = vec2(100.0, 100.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size  = vec2(3200.0, 3200.0);

uniform float density = 0.35;
uniform vec2 speed = vec2(0.02, 0.01);
uniform vec4 fog_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);

uniform float free_smoke = 0.0;

uniform vec2 player_world_pos = vec2(-100000.0, -100000.0);
uniform float player_break_radius = 144.0;

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    vec2 tile_xy = world_px / 32.0;
    vec2 mask_uv = tile_xy / map_tile_size;
    float mask = 0.0;
    if (mask_uv.x >= 0.0 && mask_uv.x <= 1.0 && mask_uv.y >= 0.0 && mask_uv.y <= 1.0) {
        mask = texture(fog_mask, mask_uv).r;
    }
    if (mask <= 0.001) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    vec2 base_uv = world_px / 512.0;
    float n;
    if (free_smoke > 0.5) {
        vec2 d1 = vec2(0.030, 0.012);
        vec2 d2 = vec2(-0.018, 0.026);
        float n1 = texture(noise_texture, base_uv + d1 * TIME).r;
        float n2 = texture(noise_texture, base_uv + d2 * TIME).r;
        n = max(n1, n2);
    } else {
        n = texture(noise_texture, base_uv + speed * TIME).r;
    }
    float fog = clamp(n * 2.0 - 1.0, 0.0, 1.0);

    float pd = length(world_px - player_world_pos);
    float player_fade = smoothstep(0.0, player_break_radius, pd);

    COLOR = vec4(fog_color.rgb, fog * density * mask * player_fade);
}
