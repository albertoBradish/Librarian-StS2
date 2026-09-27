shader_type canvas_item;
render_mode unshaded;

uniform float motion_time = 0.0;
// Filled-in book/page region on the final continuous painting; smooth influence never creates seams.
uniform vec2 page_center = vec2(0.48, 0.51);
uniform vec2 page_radius = vec2(0.33, 0.14);
uniform float page_motion = 0.0015;
varying vec4 inherited_tint;

void vertex() { inherited_tint = COLOR; }

void fragment() {
    vec2 uv = UV;
    float page_mask = 1.0 - smoothstep(0.25, 1.0, length((uv - page_center) / page_radius));
    float edge_fade = smoothstep(0.0, 0.04, uv.x) * smoothstep(0.0, 0.04, 1.0 - uv.x)
        * smoothstep(0.0, 0.04, uv.y) * smoothstep(0.0, 0.04, 1.0 - uv.y);
    uv.y += sin(uv.x * 18.0 + motion_time * 1.5707963) * page_motion * page_mask * edge_fade;
    vec4 art = texture(TEXTURE, uv);
    // A restrained change to existing cyan brushwork; no rectangular glow or painted-over eye.
    float magic = smoothstep(0.06, 0.24, min(art.g, art.b) - art.r) * smoothstep(0.25, 0.75, art.g);
    art.rgb *= 1.0 + magic * (0.035 + 0.025 * sin(motion_time * 1.5707963 + uv.y * 5.0));
    COLOR = art * inherited_tint;
}
