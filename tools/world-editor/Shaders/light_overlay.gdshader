shader_type canvas_item;
render_mode blend_mul, unshaded;

// Deferred-style 2D lightmap with 1D shadow maps.
//
// Each light has a pre-computed 1D shadow map stored as a row in a
// texture atlas. The shadow map encodes the nearest occluder distance
// at each angle (0-359°) from the light's position. To determine if a
// pixel is in shadow: compute the angle from pixel to light, sample the
// shadow map at that angle, compare the pixel's distance to the stored
// occluder distance. Farther → shadow. Closer → lit.
//
// This replaces per-pixel raycasting (48 steps per pixel per light)
// with a single texture lookup per light — dramatically cleaner shadows
// with no stepping artifacts.
//
// Reference: https://github.com/mattdesl/lwjgl-basics/wiki/2D-Pixel-Perfect-Shadows

uniform sampler2D occlusion_mask : filter_linear;
uniform vec2 map_pixel_size = vec2(3200.0, 3200.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size  = vec2(3200.0, 3200.0);

// 1D shadow map atlas: each ROW is one light's shadow map.
// Width = 720 (2 samples per degree × 360°), Height = 32 (max lights).
// Value = normalized distance to nearest occluder (0.0 = at light, 1.0 = at max radius).
// filter_nearest to prevent row bleeding between lights in the atlas.
uniform sampler2D shadow_atlas : filter_nearest;
uniform float shadow_atlas_width = 720.0;

uniform vec3 ambient = vec3(0.4, 0.4, 0.5);

// Up to 32 lights. vec4 = (worldX, worldY, radiusWorldPx, energy).
uniform vec4 lights[32];
uniform vec4 light_colors[32];
uniform int  num_lights = 0;
uniform float light_energy_mults[32];

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
        // Gaussian tail extends 30% past nominal radius.
        if (dist > r * 1.3) continue;

        // Gaussian falloff — no hard edge.
        float t = dist / r;
        float falloff = exp(-t * t * 3.0);

        float energy = L.w * light_energy_mults[i];

        // ── 1D Shadow map lookup ──
        vec2 delta = world_px - L.xy;
        float angle = atan(delta.y, delta.x);
        float angle_norm = (angle + PI) / TAU;

        float shadow_u = angle_norm;
        float shadow_v = (float(i) + 0.5) / 32.0;
        float occluder_dist_norm = texture(shadow_atlas, vec2(shadow_u, shadow_v)).r;

        float pixel_dist_norm = dist / r;

        // Soft shadow: lit if pixel is closer than occluder, shadow if farther.
        // occluder_dist_norm = 1.0 means no occluder → fully lit.
        // If atlas isn't loaded yet, default is 1.0 → fully lit (no shadow).
        float shadow_soft = 1.0;
        if (occluder_dist_norm < 0.99) {
            shadow_soft = 1.0 - smoothstep(
                occluder_dist_norm - 0.02,
                occluder_dist_norm + 0.04,
                pixel_dist_norm);
        }

        // Dynamic character occluder (not in shadow map — moves each frame).
        if (character_pos.x >= 0.0) {
            vec2 to_light = L.xy - world_px;
            vec2 to_char = character_pos - world_px;
            float tl = length(to_light);
            if (tl > 1.0) {
                float proj = dot(to_char, to_light / tl);
                if (proj > 0.0 && proj < tl) {
                    float perp = length(to_char - (to_light / tl) * proj);
                    if (perp < character_half) {
                        shadow_soft *= smoothstep(0.0, character_half, perp);
                    }
                }
            }
        }

        light_accum += col * falloff * energy * shadow_soft;
    }

    vec3 final_color = clamp(ambient + light_accum, 0.0, 1.0);
    COLOR = vec4(final_color, 1.0);
}
