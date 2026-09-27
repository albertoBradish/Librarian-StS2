shader_type canvas_item;

// Original shader. The native texture is supplied only by the running game.
// Match the user-approved base color while retaining neutral ink and highlights.
uniform vec4 theme_color : source_color = vec4(0.92156863, 0.64313725, 0.4, 1.0);
varying vec4 vertex_color;

void vertex() {
    vertex_color = COLOR;
}

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    float high = max(source.r, max(source.g, source.b));
    float low = min(source.r, min(source.g, source.b));
    float chroma = high - low;
    float color_mask = smoothstep(0.025, 0.12, chroma);
    vec3 painted = theme_color.rgb * (high / 0.8);
    source.rgb = mix(source.rgb, clamp(painted, vec3(0.0), vec3(1.0)), color_mask);
    COLOR = source * vertex_color;
}
