shader_type canvas_item;

// MINIMAL DIAGNOSTIC — unconditional red output.
// If you see 50% red covering the whole map viewport area, the shader
// pipeline works and any visibility issue was in the mask / uniforms.
// If you see nothing (transparent), the shader is not running at all
// (compile failure, material not assigned, sprite culled, etc.).

void fragment() {
    COLOR = vec4(1.0, 0.0, 0.0, 0.5);
}
