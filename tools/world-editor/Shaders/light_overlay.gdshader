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
// (0.4, 0.4, 0.5) = visible night (terrain shapes readable).
// (0.15, 0.12, 0.2) = dark night.
// (1.0, 1.0, 1.0) = full daylight (no darkening).
uniform vec3 ambient = vec3(0.4, 0.4, 0.5);

// Up to 32 lights. Each vec4 = (worldX, worldY, radiusWorldPx, energy).
uniform vec4 lights[32];
uniform vec4 light_colors[32];
uniform int  num_lights = 0;
uniform float light_energy_mults[32];

// Normal map derived from the occlusion mask. When enabled, the
// lighting calculation uses a per-pixel dot(normal, lightDir) factor
// so sprites show shading based on their "surface direction" — one
// side brighter, other side darker, giving a 2.5D relief effect.
// The normal is estimated from the mask's luminance gradient (Sobel).
uniform float normal_strength = 0.6; // 0 = flat (no normal effect), 1 = full

// Dynamic character occluder.
uniform vec2  character_pos = vec2(-1.0, -1.0);
uniform float character_half = 10.0;

const int RAY_STEPS = 16;

// Estimate surface normal from the occlusion mask gradient (Sobel filter).
// Returns a vec3 normal where (0, 0, 1) = flat facing camera.
// Edges of sprites get angled normals so light wraps around them.
vec3 estimate_normal(vec2 world_px) {
    vec2 texel = 1.0 / map_pixel_size;
    float px = 2.0; // sample offset in world pixels
    vec2 off = texel * px;

    // 3x3 Sobel kernel sampling the occlusion mask height.
    // We treat mask opacity as "height" — opaque = elevated surface.
    float tl = texture(occlusion_mask, world_px / map_pixel_size + vec2(-off.x, -off.y)).r;
    float t  = texture(occlusion_mask, world_px / map_pixel_size + vec2(   0.0, -off.y)).r;
    float tr = texture(occlusion_mask, world_px / map_pixel_size + vec2( off.x, -off.y)).r;
    float l  = texture(occlusion_mask, world_px / map_pixel_size + vec2(-off.x,    0.0)).r;
    float r  = texture(occlusion_mask, world_px / map_pixel_size + vec2( off.x,    0.0)).r;
    float bl = texture(occlusion_mask, world_px / map_pixel_size + vec2(-off.x,  off.y)).r;
    float b  = texture(occlusion_mask, world_px / map_pixel_size + vec2(   0.0,  off.y)).r;
    float br = texture(occlusion_mask, world_px / map_pixel_size + vec2( off.x,  off.y)).r;

    // Sobel gradients
    float dx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float dy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);

    // Normal from gradient (z = 1.0 makes the surface mostly face the camera)
    return normalize(vec3(-dx * normal_strength, -dy * normal_strength, 1.0));
}

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

    // Skip the first ~40 world-px of the ray to prevent self-occlusion.
    // A pixel sitting ON a 32-64px wide sprite needs to ignore its own
    // sprite's mask when raycasting toward the light — otherwise the
    // entire sprite surface stays black even on the light-facing side.
    // 40px covers a typical 1-tile sprite; 2-tile sprites (64px) may
    // still self-shadow slightly at their center, which looks natural.
    float skip_px = 40.0;
    float start_t = clamp(skip_px / len, 0.0, 0.45);
    float end_t   = clamp(1.0 - 8.0 / len, 0.55, 1.0);

    for (int i = 0; i < RAY_STEPS; i++) {
        float t = start_t + (end_t - start_t) * (float(i) / float(RAY_STEPS - 1));
        vec2 p = from_px + dir * t;
        if (sample_occlusion(p) > 0.4) return true;
    }
    return false;
}

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    // NOTE: no self-occlusion check here. Pixels ON a sprite's surface
    // (rock, tree, etc.) CAN receive light — the light-facing side of
    // the sprite should be illuminated. The raycast's start_t skip
    // (40px) handles self-shadowing by ignoring the sprite's own mask
    // near the start of the ray. Only DISTANT occluders between the
    // pixel and the light actually block illumination.

    vec3 light_accum = vec3(0.0);

    for (int i = 0; i < 32; i++) {
        if (i >= num_lights) break;

        vec4  L    = lights[i];
        vec3  col  = light_colors[i].rgb;
        float dist = distance(world_px, L.xy);
        float r    = L.z;
        if (dist > r) continue;

        // Smooth Hermite falloff: bright center, gradual soft edge.
        float t = dist / r;
        float falloff = smoothstep(1.0, 0.0, t);

        // With multiply blending, output is clamped to [0,1] naturally —
        // over-energized lights just push the lit area closer to full
        // scene preservation, never wash-out. No pre-scaling needed.
        // Slider: 0.3=candle, 0.8=torch, 1.5=campfire, 3=beacon.
        float energy = L.w * light_energy_mults[i];

        // Shadow raycast — checks for occluders BETWEEN this pixel
        // and the light (skipping the first 40px to avoid self-shadow).
        if (is_occluded(world_px, L.xy)) continue;

        // Normal-based shading for 2.5D relief. Edges of sprites get
        // angled normals from the Sobel filter, creating directional
        // lighting (bright on light-facing side, dim on the back).
        // Flat ground pixels have normal ≈ (0,0,1) → ndotl ≈ 1.0.
        vec3 normal = estimate_normal(world_px);
        vec3 light_dir = normalize(vec3(L.xy - world_px, 80.0));
        float ndotl = max(dot(normal, light_dir), 0.25);

        light_accum += col * falloff * energy * ndotl;
    }

    // Final output: ambient + accumulated light, clamped to [0, 1].
    // Because we use blend_mul, this is MULTIPLIED with the scene:
    //   lit pixel:   scene × (ambient + light ≈ 1.0) = scene (preserved)
    //   unlit pixel: scene × ambient ≈ scene × 0.08 = very dark
    //   shadow:      scene × ambient = dark (light blocked by occluder)
    vec3 final_color = clamp(ambient + light_accum, 0.0, 1.0);
    COLOR = vec4(final_color, 1.0);
}
