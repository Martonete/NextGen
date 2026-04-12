shader_type canvas_item;
render_mode blend_mul, unshaded;

// Deferred-style 2D lightmap composited via MULTIPLY blending.
//
// Output = ambient + sum(light contributions).
// Because blend_mul multiplies the output with the scene:
//   - vec3(1.0) = no change (fully lit)
//   - vec3(0.0) = full darkness
//   - ambient (e.g. 0.05, 0.03, 0.08) = dark night with a hint of blue
//
// Lit pixels get ambient + warm light color → close to 1.0 → scene preserved.
// Unlit pixels get only ambient → close to 0.0 → scene darkened.
//
// This is the same technique used by Terraria, Starbound, and the 2.5D
// Argentum engine (deferred lighting, lights as textured quads, multiply
// composite). The difference is we do per-pixel raycast shadows through
// an occlusion mask built from L3 GRH sprite alphas.
//
// Reference: https://github.com/mattdesl/lwjgl-basics/wiki/2D-Pixel-Perfect-Shadows

uniform sampler2D occlusion_mask : filter_linear;
uniform vec2 map_pixel_size = vec2(3200.0, 3200.0);
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size  = vec2(3200.0, 3200.0);

// Ambient color — the "darkness" that unlit areas fall to.
// With multiply blending, scene × ambient = final unlit color.
// (0.3, 0.3, 0.4) = visible night (can still see terrain shapes).
// (0.08, 0.06, 0.12) = near-pitch-black night.
// (1.0, 1.0, 1.0) = full daylight (no darkening).
uniform vec3 ambient = vec3(0.3, 0.3, 0.4);

// Up to 32 lights. Each vec4 = (worldX, worldY, radiusWorldPx, energy).
uniform vec4 lights[32];
uniform vec4 light_colors[32];
uniform int  num_lights = 0;
uniform float light_energy_mults[32];

// Dynamic character occluder.
uniform vec2  character_pos = vec2(-1.0, -1.0);
uniform float character_half = 10.0;

const int RAY_STEPS = 16;

float sample_occlusion(vec2 world_px) {
    vec2 uv = world_px / map_pixel_size;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0;
    float mask = texture(occlusion_mask, uv).r;

    if (character_pos.x >= 0.0) {
        vec2 d = abs(world_px - character_pos);
        if (d.x < character_half && d.y < character_half * 2.0) {
            mask = max(mask, 1.0);
        }
    }
    return mask;
}

bool is_occluded(vec2 from_px, vec2 light_px) {
    vec2 dir = light_px - from_px;
    float len = length(dir);
    if (len < 1.0) return false;

    float start_t = clamp(8.0 / len, 0.0, 0.4);
    float end_t   = clamp(1.0 - 8.0 / len, 0.6, 1.0);

    for (int i = 0; i < RAY_STEPS; i++) {
        float t = start_t + (end_t - start_t) * (float(i) / float(RAY_STEPS - 1));
        vec2 p = from_px + dir * t;
        if (sample_occlusion(p) > 0.4) return true;
    }
    return false;
}

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    float self_occ = sample_occlusion(world_px);

    // Start with ambient — this is the base darkness for unlit areas.
    vec3 light_accum = vec3(0.0);

    for (int i = 0; i < 32; i++) {
        if (i >= num_lights) break;

        vec4  L    = lights[i];
        vec3  col  = light_colors[i].rgb;
        float dist = distance(world_px, L.xy);
        float r    = L.z;
        if (dist > r) continue;

        // Smooth falloff: linear fade with soft edge via smoothstep.
        // NOT squared — squaring made the light too dim and concentrated.
        // smoothstep alone gives a natural cubic S-curve:
        //   center (t=0) → 1.0, edge (t=1) → 0.0, with gentle transitions.
        float t = dist / r;
        float falloff = smoothstep(1.0, 0.0, t);

        // Energy directly scales the light contribution. With multiply
        // blending, the output is clamped to [0,1] so high energy just
        // pushes the lit area closer to fully preserving the scene color.
        // Typical values: 0.5=subtle candle, 1.0=torch, 2.0=campfire,
        // 5.0=bright beacon.
        float energy = L.w * light_energy_mults[i];

        // Self-occluded pixels (inside an L3 sprite) don't receive light
        if (self_occ > 0.4) continue;

        // Shadow raycast
        if (is_occluded(world_px, L.xy)) continue;

        light_accum += col * falloff * energy;
    }

    // Final output: ambient + accumulated light, clamped to [0, 1].
    // Because we use blend_mul, this is MULTIPLIED with the scene:
    //   lit pixel:   scene × (ambient + light ≈ 1.0) = scene (preserved)
    //   unlit pixel: scene × ambient ≈ scene × 0.08 = very dark
    //   shadow:      scene × ambient = dark (light blocked by occluder)
    vec3 final_color = clamp(ambient + light_accum, 0.0, 1.0);
    COLOR = vec4(final_color, 1.0);
}
