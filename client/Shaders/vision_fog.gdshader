shader_type canvas_item;

// Screen-space peripheral darkness. The C# layer supplies the same soft mask
// (central transparent rectangle + rounded smoothstep edge) used by AO Libre.

uniform sampler2D fog_mask : filter_linear;
uniform vec4 fog_color : source_color = vec4(0.02, 0.03, 0.05, 1.0);
uniform float max_alpha = 0.7;

void fragment() {
    float mask = texture(fog_mask, UV).r;
    if (mask <= 0.001) {
        COLOR = vec4(0.0, 0.0, 0.0, 0.0);
    } else {
        COLOR = vec4(fog_color.rgb, mask * max_alpha);
    }
}
