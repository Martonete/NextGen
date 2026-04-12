shader_type canvas_item;

// Custom pixel-based light compositing that respects L3 sprite alpha.
//
// Instead of Godot's built-in Light2D (whose shadow atlas paints a hard
// rectangular dark block around lights), this shader raycasts each pixel
// toward each light source through an occlusion mask built from the
// actual L3 GRH sprite alphas. If ANY sample along the ray is opaque,
// the light is blocked — giving pixel-accurate shadows that match the
// visible graphic silhouettes exactly.
//
// Architecture mirrors fog_overlay.gdshader: one panel-sized Sprite2D
// with this material, updated per-frame via uniforms for the camera
// view mapping and light positions.

uniform sampler2D occlusion_mask : filter_linear;
uniform vec2 map_pixel_size = vec2(3200.0, 3200.0); // map width/height in WORLD PIXELS
uniform vec2 rect_world_origin = vec2(0.0, 0.0);
uniform vec2 rect_world_size  = vec2(3200.0, 3200.0);

// Up to 32 lights. Each vec4 = (worldX, worldY, radiusWorldPx, energy).
uniform vec4 lights[32];
// Per-light color (rgb). Alpha unused.
uniform vec4 light_colors[32];
uniform int  num_lights = 0;

// Flicker / pulse packed per light: x = current energy multiplier
// (pre-computed on CPU each frame so the shader stays simple).
uniform float light_energy_mults[32];

// Dynamic character occluder — square at this world-pixel position.
// x < 0 disables. half_size controls the square's half-width.
uniform vec2  character_pos = vec2(-1.0, -1.0);
uniform float character_half = 10.0;

// Number of steps for the shadow raycast. More = finer detail, slower.
// 16 is a good balance for typical editor zoom levels.
const int RAY_STEPS = 16;

// Sample the occlusion mask at a world-pixel position. Returns > 0
// when the pixel is blocked by an L3 sprite or character.
float sample_occlusion(vec2 world_px) {
    // Mask UV: world_px / total map pixel size.
    vec2 uv = world_px / map_pixel_size;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0;
    float mask = texture(occlusion_mask, uv).r;

    // Also check the dynamic character square.
    if (character_pos.x >= 0.0) {
        vec2 d = abs(world_px - character_pos);
        if (d.x < character_half && d.y < character_half * 2.0) {
            mask = max(mask, 1.0);
        }
    }
    return mask;
}

// Raycast from a pixel toward a light source through the mask.
// Returns true if the ray is blocked (occluded).
bool is_occluded(vec2 from_px, vec2 light_px) {
    vec2 dir = light_px - from_px;
    float len = length(dir);
    if (len < 1.0) return false; // on top of light

    // Skip the first and last ~8 px of the ray so a pixel sitting right
    // at the edge of an occluder doesn't self-shadow.
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

    // If THIS pixel is inside an occluder, it shouldn't receive light
    // (the graphic itself stays unlit behind its own silhouette).
    float self_occ = sample_occlusion(world_px);

    vec3 accum = vec3(0.0);

    for (int i = 0; i < 32; i++) {
        if (i >= num_lights) break;

        vec4  L     = lights[i];
        vec3  col   = light_colors[i].rgb;
        float dist  = distance(world_px, L.xy);
        float r     = L.z; // radius in world px
        if (dist > r) continue;

        // Quadratic falloff: bright center, soft edge.
        float t = dist / r;
        float falloff = (1.0 - t) * (1.0 - t);

        float energy = L.w * light_energy_mults[i];

        // If this pixel is itself blocked by an occluder, skip.
        if (self_occ > 0.4) continue;

        // Raycast toward the light — skip if occluded.
        if (is_occluded(world_px, L.xy)) continue;

        accum += col * falloff * energy;
    }

    // Output as additive overlay. Alpha controls how much the light
    // adds to the underlying map. 0.9 cap prevents over-burn.
    float alpha = clamp(length(accum), 0.0, 0.9);
    COLOR = vec4(accum, alpha);
}
