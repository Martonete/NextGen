shader_type canvas_item;

// Screen-space fog outside the lit core viewport. Unlike fog_overlay.gdshader
// (world-space, zone-driven weather fog), this one only needs a fixed
// core/transition/outer mask baked once (fog_mask) plus an animated noise
// layer so the fog reads as drifting mist instead of a flat color tint.

uniform sampler2D fog_mask : filter_linear;
uniform sampler2D noise_texture : repeat_enable, filter_linear;

uniform vec4 fog_color : source_color = vec4(0.13, 0.15, 0.19, 1.0);
uniform float max_alpha = 0.55;
uniform vec2 speed = vec2(0.015, 0.01);
uniform float noise_scale = 2.5;

void fragment() {
    float mask = texture(fog_mask, UV).r;
    if (mask <= 0.001) {
        // Inside the lit core: fully transparent, no fog.
        // NOTE: Godot canvas_item shaders forbid 'return' in fragment(); use else.
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
    } else {
        // Two overlapping noise samples at different scales/speeds so the fog
        // doesn't read as a single scrolling texture — closer to real drifting mist.
        vec2 uv1 = UV * noise_scale + speed * TIME;
        vec2 uv2 = UV * noise_scale * 1.7 - speed * 1.3 * TIME;
        float n1 = texture(noise_texture, uv1).r;
        float n2 = texture(noise_texture, uv2).r;
        float n = (n1 * 0.6 + n2 * 0.4);

        // Keep the fog always at least mostly opaque where the mask says so (so it
        // reliably hides the terrain at high intensity), with the noise only
        // modulating it +/-25% for texture/movement instead of flickering it away.
        float density = clamp(0.75 + (n - 0.5) * 0.5, 0.0, 1.0);

        COLOR = vec4(fog_color.rgb, mask * max_alpha * density);
    }
}
