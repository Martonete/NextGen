shader_type canvas_item;

// Screen-space fog outside the classic 17x13 viewport. The C# layer supplies
// a soft mask; this shader adds only a very slow texture break so the dark
// area reads as atmosphere without drawing attention during movement.

uniform sampler2D fog_mask : filter_linear;
uniform sampler2D noise_texture : repeat_enable, filter_linear;

uniform vec4 fog_color : source_color = vec4(0.045, 0.05, 0.06, 1.0);
uniform vec4 edge_color : source_color = vec4(0.075, 0.085, 0.10, 1.0);
uniform float max_alpha = 0.95;
uniform vec2 speed = vec2(0.0025, 0.0015);
uniform float noise_scale = 1.35;

void fragment() {
    float mask = texture(fog_mask, UV).r;
    if (mask <= 0.001) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
    } else {
        // Broad shadow bed with tiny, slow variation. Avoid strong wisps: they
        // become too noticeable against the moving world.
        vec2 uv1 = UV * noise_scale + speed * TIME;
        vec2 uv2 = UV * noise_scale * 1.65 - speed.yx * 0.85 * TIME;

        float n1 = texture(noise_texture, uv1).r;
        float n2 = texture(noise_texture, uv2).r;
        float broad = n1 * 0.65 + n2 * 0.35;
        float wisp = smoothstep(0.62, 0.94, n2);

        float edge = smoothstep(0.08, 0.88, mask);
        float outer = smoothstep(0.45, 1.0, mask);
        float soft_mask = clamp(mask + (broad - 0.5) * 0.045 * (1.0 - outer), 0.0, 1.0);

        float density = clamp(0.93 + (broad - 0.5) * 0.08 + wisp * 0.025, 0.86, 1.0);
        float alpha = soft_mask * max_alpha * density;

        vec3 color = mix(fog_color.rgb, edge_color.rgb, wisp * (1.0 - outer) * edge * 0.16);
        color *= 0.98 + (broad - 0.5) * 0.04;

        COLOR = vec4(color, alpha);
    }
}
