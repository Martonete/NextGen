shader_type canvas_item;

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float density : hint_range(0.0, 1.0) = 0.35;
uniform vec2 speed = vec2(0.02, 0.01);
uniform vec4 fog_color : source_color = vec4(0.5, 0.55, 0.63, 1.0);
uniform float noise_scale : hint_range(0.1, 8.0) = 1.5;

void fragment() {
    vec2 uv = UV * noise_scale + speed * TIME;
    float noise = texture(noise_texture, uv).r;
    float fog = clamp(noise * 2.0 - 1.0, 0.0, 1.0);
    COLOR = vec4(fog_color.rgb, fog * density);
}
