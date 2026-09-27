shader_type canvas_item;
render_mode unshaded;

uniform float motion_time = 0.0;
uniform float aspect = 1.7777778;
varying vec4 inherited_tint;

void vertex() { inherited_tint = COLOR; }

void fragment() {
    float dust = 0.0;
    // Sparse deterministic motes on the illustration side, never on native information text.
    for (int i = 0; i < 14; i++) {
        float seed = float(i);
        vec2 origin = vec2(0.48 + fract(seed * 0.6180339) * 0.48, fract(seed * 0.381966));
        vec2 center = origin + vec2(sin(motion_time * 0.15707963 + seed) * 0.007,
            sin(motion_time * 0.31415927 + seed * 2.0) * 0.025);
        float radius = 0.001 + fract(seed * 0.41) * 0.00065;
        float shape = 1.0 - smoothstep(radius * 0.25, radius, length((UV - center) * vec2(aspect, 1.0)));
        dust += shape * (0.10 + 0.10 * sin(motion_time * 0.78539816 + seed));
    }
    COLOR = vec4(0.92, 0.73, 0.44, min(dust, 0.22)) * inherited_tint;
}
