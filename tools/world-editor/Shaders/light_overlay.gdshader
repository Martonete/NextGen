shader_type canvas_item;
render_mode blend_mul, unshaded;

// 2D lightmap with 1D shadow maps, composited via multiply blending.
// Reference: github.com/mattdesl/lwjgl-basics/wiki/2D-Pixel-Perfect-Shadows

uniform sampler2D occlusion_mask : filter_linear;
uniform vec2 map_pixel_size = vec2(3200.0, 3200.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size  = vec2(3200.0, 3200.0);

// 1D shadow atlas: row per light, 720 columns = 2 per degree.
uniform sampler2D shadow_atlas : filter_nearest;

uniform vec3 ambient = vec3(0.4, 0.4, 0.5);

// lights[i] = (worldX, worldY, radiusWorldPx, energy × animMult).
// Energy is pre-multiplied by flicker/pulse on CPU each frame.
uniform vec4 lights[32];
// light_colors[i].rgb = color. Alpha unused.
uniform vec4 light_colors[32];
uniform int  num_lights = 0;

// Dynamic character occluder.
uniform vec2  character_pos = vec2(-1.0, -1.0);
uniform float character_half = 10.0;

const float PI = 3.14159265;
const float TAU = 6.28318530;

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    vec3 light_accum = vec3(0.0);

    for (int i = 0; i < 32; i++) {
        if (i >= num_lights) break;

        vec4  L    = lights[i];
        vec3  col  = light_colors[i].rgb;
        float dist = distance(world_px, L.xy);
        float r    = L.z;
        if (dist > r * 1.3) continue;

        // Gaussian falloff — no hard edge.
        float t = dist / r;
        float falloff = exp(-t * t * 3.0);

        // Energy already includes flicker/pulse (pre-multiplied on CPU).
        float energy = L.w;

        // 1D shadow map lookup.
        vec2 delta = world_px - L.xy;
        float angle = atan(delta.y, delta.x);
        float angle_norm = (angle + PI) / TAU;
        float shadow_v = (float(i) + 0.5) / 32.0;
        float occluder_dist = texture(shadow_atlas, vec2(angle_norm, shadow_v)).r;

        float pixel_dist = dist / r;

        // If no occluder (atlas ≈ 1.0), fully lit. Otherwise soft shadow.
        float shadow = 1.0;
        if (occluder_dist < 0.99) {
            shadow = 1.0 - smoothstep(
                occluder_dist - 0.02,
                occluder_dist + 0.04,
                pixel_dist);
        }

        // Dynamic character check (not in atlas — moves each frame).
        if (character_pos.x >= 0.0) {
            vec2 to_light = L.xy - world_px;
            vec2 to_char = character_pos - world_px;
            float tl = length(to_light);
            if (tl > 1.0) {
                float proj = dot(to_char, to_light / tl);
                if (proj > 0.0 && proj < tl) {
                    float perp = length(to_char - (to_light / tl) * proj);
                    if (perp < character_half) {
                        shadow *= smoothstep(0.0, character_half, perp);
                    }
                }
            }
        }

        light_accum += col * falloff * energy * shadow;
    }

    vec3 final_color = clamp(ambient + light_accum, 0.0, 1.0);
    COLOR = vec4(final_color, 1.0);
}
