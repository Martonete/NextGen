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
// 1D Shadow Atlas: width = 720 angles (0.5° each), height = light slot.
// Each pixel = normalized distance to nearest occluder from that light
// at that angle. Built on CPU once per mask change or light move —
// shader just reads it instead of doing per-pixel raycasts. Killing
// per-pixel raycast eliminates 'rays radiating from light sources'.
uniform sampler2D shadow_atlas : filter_linear;
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
// NOTE: flicker/pulse energy multiplier is pre-baked into lights[i].w
// on CPU each frame. Godot 4 silently rejects uniform float[] arrays
// (only vec4[] works), so we can't have a separate energy_mults array.

// Normal map derived from the occlusion mask. When enabled, the
// lighting calculation uses a per-pixel dot(normal, lightDir) factor
// so sprites show shading based on their "surface direction" — one
// side brighter, other side darker, giving a 2.5D relief effect.
// The normal is estimated from the mask's luminance gradient (Sobel).
uniform float normal_strength = 0.6; // 0 = flat (no normal effect), 1 = full

// Dynamic character occluder.
uniform vec2  character_pos = vec2(-1.0, -1.0);
uniform float character_half = 10.0;

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

    // Character occluder as an ellipse (approximates human silhouette
    // far better than a rectangle). Anchor at the character's torso
    // center (offset up from the feet position by character_half).
    if (character_pos.x >= 0.0) {
        vec2 torso = character_pos - vec2(0.0, character_half);
        vec2 d = world_px - torso;
        float a = character_half;           // half-width of ellipse
        float b = character_half * 1.2;     // half-height, slightly taller
        float e = (d.x * d.x) / (a * a) + (d.y * d.y) / (b * b);
        if (e < 1.0) {
            // Soft edge via smoothstep — fades at the rim instead of a
            // hard ellipse boundary, matching the soft alpha of a sprite.
            mask = max(mask, 1.0 - smoothstep(0.7, 1.0, e));
        }
    }
    return mask;
}

void fragment() {
    vec2 world_px = rect_world_origin + UV * rect_world_size;

    // Check if THIS pixel is on an occluder surface (wall, tree, rock).
    float self_mask = sample_occlusion(world_px);

    vec3 light_accum = vec3(0.0);

    for (int i = 0; i < 32; i++) {
        if (i >= num_lights) break;

        vec4  L    = lights[i];
        vec3  col  = light_colors[i].rgb;
        float dist = distance(world_px, L.xy);
        float r    = L.z;
        if (dist > r * 1.3) continue;

        // Two-layer falloff: wide halo + subtle hot core for a 'glow'
        // feel without the high contrast that makes raycast 'rays'
        // through mask gaps super-visible. Core reduced from ×0.4 to
        // ×0.15 so the hot spot is much less intense.
        float t = dist / r;
        float halo = exp(-t * t * 3.0);
        float core = exp(-t * t * 15.0);
        float falloff = halo + core * 0.15;

        float energy = L.w;

        // Occluder surfaces (walls/trees) always receive light.
        // Shadow only falls on ground behind them.
        float ground_factor = 1.0 - smoothstep(0.3, 0.6, self_mask);
        float shadow = 1.0;
        if (ground_factor > 0.5) {
            // ── 1D Shadow Atlas lookup ──
            // Compute the angle from light to this pixel, normalize to
            // [0..1], and read the precomputed occluder distance from
            // the atlas. Compare to the pixel's normalized distance:
            // farther than the occluder = shadow, closer = lit.
            // No per-pixel raycast → no per-pixel divergence → no rays.
            vec2 to_pixel = world_px - L.xy;
            float angle = atan(to_pixel.y, to_pixel.x); // -PI..PI
            float angle_norm = (angle + 3.14159265) / 6.28318531;

            // Light's row in the atlas (32 rows total).
            float light_row = (float(i) + 0.5) / 32.0;
            float occluder_dist = texture(shadow_atlas,
                vec2(angle_norm, light_row)).r;

            float pixel_dist = dist / r;
            // Tiny bias to avoid self-shadow at the occluder surface.
            // Smoothstep gives a soft penumbra at the shadow edge.
            shadow = 1.0 - smoothstep(occluder_dist - 0.01,
                                       occluder_dist + 0.04,
                                       pixel_dist);

            // Dynamic character occluder — checked separately since the
            // atlas only has static map occluders (mask).
            if (character_pos.x >= 0.0) {
                vec2 to_char = character_pos - world_px;
                vec2 to_lit = L.xy - world_px;
                float lit_len = length(to_lit);
                if (lit_len > 1.0) {
                    vec2 lit_dir = to_lit / lit_len;
                    float proj = dot(to_char, lit_dir);
                    if (proj > 0.0 && proj < lit_len) {
                        float perp = length(to_char - lit_dir * proj);
                        if (perp < character_half) {
                            shadow *= smoothstep(0.0, character_half, perp);
                        }
                    }
                }
            }
        }

        light_accum += col * falloff * energy * shadow;
    }

    // Atmospheric ambient — slight warm tint (mix of the cool night color
    // with a faint amber wash) breaks up the monolithic blue-purple.
    vec3 warm_ambient = ambient + vec3(0.03, 0.01, -0.01);

    vec3 final_color = clamp(warm_ambient + light_accum, 0.0, 1.0);
    COLOR = vec4(final_color, 1.0);
}
