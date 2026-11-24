/*{
    "DESCRIPTION": "Monochrome eight-fold ink kaleidoscope: interlocking rings, faceted shards, snake waves, a central star, smoky paper and film grain",
    "CREDIT": "Syntetika Engine",
    "ISFVSN": "2.0",
    "CATEGORIES": ["Generator", "Kaleidoscope", "Audio Reactive"],
    "INPUTS": [
        {"NAME": "speed", "TYPE": "float", "MIN": 0.0, "MAX": 1.5, "DEFAULT": 0.25},
        {"NAME": "zoom", "TYPE": "float", "MIN": 0.6, "MAX": 2.5, "DEFAULT": 1.15},
        {"NAME": "phase", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.0},
        {"NAME": "hatch", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.6},
        {"NAME": "grain", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.25},
        {"NAME": "contrast", "TYPE": "float", "MIN": 0.8, "MAX": 2.5, "DEFAULT": 1.7},
        {"NAME": "smoke", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.5},
        {"NAME": "star", "TYPE": "float", "MIN": 0.0, "MAX": 1.0, "DEFAULT": 0.7},
        {"NAME": "rings", "TYPE": "float", "MIN": 0.0, "MAX": 1.5, "DEFAULT": 1.0},
        {"NAME": "ringCount", "TYPE": "float", "MIN": 1.0, "MAX": 6.0, "DEFAULT": 6.0},
        {"NAME": "shards", "TYPE": "float", "MIN": 0.0, "MAX": 1.5, "DEFAULT": 1.0},
        {"NAME": "snakes", "TYPE": "float", "MIN": 0.0, "MAX": 1.5, "DEFAULT": 1.0},
        {"NAME": "starSize", "TYPE": "float", "MIN": 0.5, "MAX": 1.6, "DEFAULT": 1.0}
    ]
}*/

#define PI 3.14159265359

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 4; i++) {
        v += amp * noise(p);
        p = p * 2.03 + vec2(17.1, 9.7);
        amp *= 0.5;
    }
    return v;
}

void main() {
    vec2 uv = isf_FragNormCoord * 2.0 - 1.0;
    uv.x *= RENDERSIZE.x / RENDERSIZE.y;
    uv /= zoom;

    float t = TIME * speed;
    float rot = phase * PI * 2.0 + t * 0.12;
    uv = mat2(cos(rot), -sin(rot), sin(rot), cos(rot)) * uv;

    float r = length(uv);
    float a = atan(uv.y, uv.x);
    float seg = PI / 4.0;
    float af = abs(mod(a + seg * 0.5, seg) - seg * 0.5);
    vec2 p = vec2(cos(af), sin(af)) * r;

    float bass = max(audioBass, 0.2 + 0.1 * sin(TIME * 0.5));
    float high = max(audioHigh, 0.2 + 0.1 * sin(TIME * 0.9));
    float beat = audioBeat;

    // Smoky paper backdrop.
    float n = fbm(p * 2.6 + vec2(t * 0.12, -t * 0.09));
    float paper = mix(0.92, 0.32, smoke * (0.35 + 0.65 * n));
    paper *= 0.93 + 0.07 * fbm(p * 11.0 - t * 0.1);

    // Interlocking rings, two families wobbling against each other.
    float ringInk = 0.0;
    for (int i = 0; i < 6; i++) {
        float fi = float(i);
        if (fi >= ringCount) break;
        float rr = 0.20 + 0.125 * fi
                 + 0.022 * sin(a * 8.0 + t * 0.35 + fi * 0.9)
                 + 0.010 * sin(a * 16.0 - t * 0.22 + fi * 2.1)
                 + beat * 0.012 * sin(fi * 1.7);
        float w = 0.006 + 0.0025 * (1.0 + sin(fi * 2.3 + t));
        ringInk += smoothstep(w, 0.0, abs(r - rr));
        float rr2 = rr + 0.018;
        ringInk += 0.55 * smoothstep(w * 0.7, 0.0, abs(r - rr2));
    }
    ringInk = clamp(ringInk, 0.0, 1.0);

    // Snake-like ink bands winding around the star.
    float snake = abs(sin(r * 15.0 - a * 8.0 * 0.5 + t * 1.1));
    float snake2 = abs(sin(r * 9.0 + a * 8.0 + t * 0.7 + 1.7));
    float snakeInk = smoothstep(0.055, 0.0, snake) * smoothstep(1.05, 0.18, r)
                   + 0.65 * smoothstep(0.045, 0.0, snake2) * smoothstep(0.95, 0.25, r);
    snakeInk *= 0.75 + 0.25 * sin(a * 8.0 + t);

    // Faceted geometric shards in the folded wedge, held near the inner field.
    float field = smoothstep(1.02, 0.35, r);
    vec2 g = p * 5.5;
    vec2 id = floor(g);
    vec2 f = fract(g) - 0.5;
    float diamond = abs(f.x) + abs(f.y);
    float shardEdge = smoothstep(0.022, 0.0, abs(diamond - 0.40)) * field;
    float shardTone = 0.04 * (hash21(id + 3.7) - 0.5) * field;
    float shardBands = smoothstep(0.02, 0.0, abs(f.x * f.y) - 0.012) * 0.22 * field;

    // Hand-inked hatching: one fine comb along the fold, one softer cross-hatch.
    float ang = 0.6 + 0.25 * sin(t * 0.17);
    vec2 hp = vec2(p.x * cos(ang) + p.y * sin(ang), -p.x * sin(ang) + p.y * cos(ang));
    float hatchLine = smoothstep(0.72, 0.97, sin(hp.x * 210.0 + 2.5 * sin(hp.y * 26.0)));
    float hatchLine2 = 0.3 * smoothstep(0.84, 0.99, sin(hp.y * 150.0 - 2.0 * sin(hp.x * 17.0) + 1.3));
    float curvedHatch = 0.25 * smoothstep(0.86, 0.99, sin(r * 150.0 + af * 22.0 + 2.0 * sin(af * 8.0 + t * 0.4)));
    float hatchMask = hatch * smoothstep(1.05, 0.30, r) * (0.35 + 0.65 * (1.0 - paper));
    hatchLine = (hatchLine + hatchLine2 + curvedHatch) * hatchMask;

    // Central eight-point star and radial starlight.
    float starScale = starSize;
    float starPoints = pow(max(0.0, cos(a * 8.0)), 30.0) * smoothstep(0.66 * starScale, 0.0, r);
    float starCore = smoothstep(0.21 * starScale, 0.0, r);
    float starRing = smoothstep(0.018, 0.0, abs(r - 0.19 * starScale))
                   * smoothstep(0.35 * starScale, 0.12 * starScale, r);
    float fineRays = pow(max(0.0, cos(af * 16.0 + t * 0.08)), 8.0) * exp(-r * 1.1);
    float rayPoints = pow(max(0.0, cos(a * 8.0)), 16.0) * exp(-r * 0.9);
    float rays = 0.6 * fineRays + 0.4 * rayPoints;
    float glow = exp(-r * 2.6) * 0.16 + pow(max(0.0, 1.0 - r), 6.0) * 0.10;

    float ink = ringInk * rings * 0.95 + snakeInk * snakes * 1.05
              + (shardEdge * 0.65 + shardBands * 0.5) * shards
              + hatchLine * 0.9 + shardTone * shards
              + (starPoints * 0.9 + starCore * 0.95 + starRing * 0.8) * star;
    ink = clamp(ink, 0.0, 1.0);

    vec3 col = vec3(paper);
    col += vec3(rays * 0.24 + glow) * (0.6 + 0.4 * high);
    col *= 1.0 - ink * 0.97;
    col += vec3(starCore * 0.20 * star);
    col += vec3(starRing * 0.10);

    // Grain and vignette, then contrast.
    float grainNoise = hash21(gl_FragCoord.xy * 0.7 + fract(TIME) * 137.0);
    col += (grainNoise - 0.5) * 0.13 * grain;
    col *= 1.0 - 0.28 * smoothstep(0.55, 1.25, r);
    col = clamp((col - 0.5) * contrast + 0.5, 0.0, 1.0);

    gl_FragColor = vec4(col, 1.0);
}
