shader_type canvas_item;

// Animate the accepted painting itself. Broad, feathered influence regions keep
// its original palette, highlights and silhouettes continuous with the scene.
uniform float motion_time = 0.0;
uniform float motion_strength : hint_range(0.0, 1.0) = 1.0;
uniform vec2 skill_hand_offset = vec2(0.0);
uniform vec2 attack_hand_offset = vec2(0.0);
uniform vec2 fire_offset = vec2(0.0);
uniform vec2 growth_offset = vec2(0.0);
uniform vec2 tide_offset = vec2(0.0);
uniform float core_pulse = 0.0;

float influence(vec2 uv, vec2 center, vec2 radius) {
    float d = length((uv - center) / radius);
    return 1.0 - smoothstep(0.32, 1.0, d);
}

void fragment() {
    vec2 uv = UV;
    vec2 displacement = vec2(0.0);
    displacement += skill_hand_offset * influence(uv, vec2(0.506, 0.328), vec2(0.078, 0.233));
    displacement += attack_hand_offset * influence(uv, vec2(0.919, 0.390), vec2(0.078, 0.236));
    displacement += fire_offset * influence(uv, vec2(0.506, 0.142), vec2(0.076, 0.174));
    displacement += growth_offset * influence(uv, vec2(0.670, 0.069), vec2(0.093, 0.150));
    displacement += tide_offset * influence(uv, vec2(0.879, 0.256), vec2(0.083, 0.165));
    // A tiny staggered page motion, anchored at the spine rather than rigidly
    // translating the book and architecture as one block.
    displacement.y += sin(motion_time * 0.83 + uv.x * 15.0) * 0.0012
        * influence(uv, vec2(0.707, 0.533), vec2(0.153, 0.115));
    vec4 painted = texture(TEXTURE, clamp(uv - displacement * motion_strength, vec2(0.001), vec2(0.999)));
    float eye = influence(uv, vec2(0.706, 0.379), vec2(0.026, 0.051));
    float spine = influence(uv, vec2(0.706, 0.581), vec2(0.047, 0.087));
    float cyan = smoothstep(0.05, 0.28, min(painted.g, painted.b) - painted.r);
    // Pulse only existing cyan paint; no extra disk, sticker or bloom cloud.
    painted.rgb += vec3(0.01, 0.10, 0.12) * cyan * max(eye, spine * 0.55) * core_pulse;
    COLOR = painted;
}
