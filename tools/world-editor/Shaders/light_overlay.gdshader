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

// More steps = finer mask sampling, fewer 'rays' (visible stripes from
// gaps in the occluder mask getting different shadow results per pixel).
// 96 is a good balance of quality vs perf.
const int RAY_STEPS = 96;

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

// Per-pixel hash for ray jitter — breaks up banding artifacts.
float pixel_hash(vec2 co) {
    return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

// Returns shadow factor 0..1. 1.0 = fully lit, 0.0 = fully shadowed.
// Shadows fade with distance from the occluder — nearby occluders cast
// sharp dark shadows, distant ones cast faint ones (matches atmospheric
// scatter + ambient fill in real lighting).
float shadow_factor(vec2 from_px, vec2 light_px) {
    vec2 dir = light_px - from_px;
    float len = length(dir);
    if (len < 1.0) return 1.0;

    float skip_px = 16.0;
    float start_t = clamp(skip_px / len, 0.0, 0.3);
    float end_t   = clamp(1.0 - 8.0 / len, 0.55, 1.0);

    // Reduced jitter (was 0.5 → 0.15) so adjacent pixels take similar
    // ray paths. Higher jitter creates 'rays' radiating from light
    // sources because two neighboring pixels disagree on whether a tiny
    // mask gap is occluded. Lower jitter trades subtle banding for
    // continuous shadow regions.
    float jitter = pixel_hash(from_px * 0.1) * 0.15;
    float step_size = (end_t - start_t) / float(RAY_STEPS);

    // Perpendicular to the ray direction, for PCF-style shadow softening.
    vec2 perp = vec2(-dir.y, dir.x) / len;

    for (int i = 0; i < RAY_STEPS; i++) {
        float t = start_t + (float(i) + jitter) * step_size;
        vec2 p = from_px + dir * t;

        // 5-tap PCF: center + 4 perpendicular offsets at small distances.
        // Tight spacing (±1, ±2 px) so the PCF doesn't reach across thin
        // walls — the mask is already thickened in CPU stamping so wider
        // GPU taps aren't needed to bridge gaps.
        float s0 = sample_occlusion(p);
        float s1 = sample_occlusion(p + perp * 1.0);
        float s2 = sample_occlusion(p - perp * 1.0);
        float s3 = sample_occlusion(p + perp * 2.0);
        float s4 = sample_occlusion(p - perp * 2.0);
        float avg = (s0 + s1 + s2 + s3 + s4) * 0.2;

        if (avg > 0.5) {
            // FULL shadow — light is blocked by a wall/sprite. Walls
            // should opaquely block light regardless of distance from the
            // shadowed pixel. The previous distance-falloff design caused
            // light to leak through walls into adjacent rooms.
            // The PCF coverage still softens the shadow edge.
            float coverage = smoothstep(0.4, 0.9, avg);
            return 1.0 - coverage;
        }
    }
    return 1.0;
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
        // Shadow only falls on ground behind them. Smooth blend at the
        // mask edge so the transition isn't a hard visible line.
        float ground_factor = 1.0 - smoothstep(0.3, 0.6, self_mask);
        float shadow = 1.0;
        if (ground_factor > 0.5) {
            shadow = shadow_factor(world_px, L.xy);
        }

        light_accum += col * falloff * energy * shadow;
    }

    // Atmospheric ambient — slight warm tint (mix of the cool night color
    // with a faint amber wash) breaks up the monolithic blue-purple.
    vec3 warm_ambient = ambient + vec3(0.03, 0.01, -0.01);

    vec3 final_color = clamp(warm_ambient + light_accum, 0.0, 1.0);
    COLOR = vec4(final_color, 1.0);
}
