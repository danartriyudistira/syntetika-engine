//! Writes the 3-layer industrial demo's visuals into an open project.
//!
//! One layer per playable track (bottom of the stack first). Every section gets its own
//! ink-kaleido composition — sparse star, dense rings, fluid snakes, hard shards, fading star —
//! while staying in the same monochrome ink style. Falls back to built-in generators when the
//! shader pack isn't present.
//!
//! Run:
//!   cargo run --release -p openvj-app --example demo_visuals -- \
//!     "/Users/me/Documents/Nota Projects/syntetika_demo.nota"

use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use openvj_core::{BlendMode, ClipSource, Composition, Effect, GeneratorKind, ModSource, Modulation, Param, PlayMode};
use serde_json::{Value, json};

/// One section's kaleidoscope recipe.
#[derive(Clone, Copy)]
struct Look {
    rings: f32,
    ring_count: f32,
    shards: f32,
    snakes: f32,
    hatch: f32,
    star: f32,
    star_size: f32,
    smoke: f32,
    contrast: f32,
    zoom: f32,
    speed: f32,
    phase: f32,
    grain: f32,
}

const LOOKS: [Look; 5] = [
    // Intro: mostly smoke and a large, lonely star.
    Look {
        rings: 0.18,
        ring_count: 2.0,
        shards: 0.08,
        snakes: 0.10,
        hatch: 0.08,
        star: 0.75,
        star_size: 1.15,
        smoke: 0.80,
        contrast: 1.52,
        zoom: 1.05,
        speed: 0.06,
        phase: 0.05,
        grain: 0.38,
    },
    // Drop 1: dense interlocking rings and tight ink hatching.
    Look {
        rings: 1.20,
        ring_count: 6.0,
        shards: 0.70,
        snakes: 0.60,
        hatch: 0.80,
        star: 0.75,
        star_size: 0.85,
        smoke: 0.35,
        contrast: 2.10,
        zoom: 1.70,
        speed: 0.32,
        phase: 0.15,
        grain: 0.22,
    },
    // Break: fluid snake bands, soft paper.
    Look {
        rings: 0.50,
        ring_count: 3.0,
        shards: 0.15,
        snakes: 1.60,
        hatch: 0.20,
        star: 0.90,
        star_size: 1.10,
        smoke: 0.72,
        contrast: 1.50,
        zoom: 0.90,
        speed: 0.10,
        phase: 0.35,
        grain: 0.32,
    },
    // Drop 2: hard faceted shards and aggressive contrast.
    Look {
        rings: 0.90,
        ring_count: 5.0,
        shards: 1.70,
        snakes: 0.35,
        hatch: 0.90,
        star: 0.65,
        star_size: 0.80,
        smoke: 0.30,
        contrast: 2.30,
        zoom: 1.45,
        speed: 0.40,
        phase: 0.60,
        grain: 0.20,
    },
    // Outro: the star fades into smoke.
    Look {
        rings: 0.05,
        ring_count: 1.0,
        shards: 0.02,
        snakes: 0.03,
        hatch: 0.03,
        star: 0.90,
        star_size: 1.55,
        smoke: 0.90,
        contrast: 1.45,
        zoom: 0.72,
        speed: 0.04,
        phase: 0.80,
        grain: 0.42,
    },
];

fn add_clip(
    comp: &mut Composition,
    layer: usize,
    col: usize,
    source: ClipSource,
    name: &str,
    sync: f32,
    source_mod: ModSource,
    amount: f32,
    params: &[(&str, f32)],
) {
    let mut source = source;
    if let ClipSource::Isf { generator } = &mut source {
        for (key, value) in params {
            generator.params.insert((*key).to_owned(), Param::new(*value));
        }
    }
    let Some(_) = comp.set_clip(layer, col, source) else { return };
    let Some(clip) = comp.clip_mut(layer, col) else { return };
    clip.name = name.to_owned();
    clip.sync_beats = Some(sync);
    clip.play_mode = PlayMode::Loop;
    clip.mods[0] = Modulation { source: source_mod, amount, ..Default::default() };
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let dir = env::args().nth(1).ok_or("usage: demo_visuals <project.nota>")?;
    let dir = Path::new(&dir);
    let path = dir.join("visuals").join("composition.json");
    let doc: Value = serde_json::from_str(&fs::read_to_string(&path)?)?;
    let old = &doc["composition"];
    let width = old["width"].as_u64().unwrap_or(1280) as u32;
    let height = old["height"].as_u64().unwrap_or(720) as u32;
    let columns = old["columns"].as_array().map(|a| a.len()).unwrap_or(8).max(8);
    let column_names: Vec<String> = old["columns"]
        .as_array()
        .map(|a| a.iter().map(|c| c["name"].as_str().unwrap_or("").to_owned()).collect())
        .unwrap_or_default();
    let scenes = column_names.iter().take_while(|n| !n.is_empty()).count().clamp(1, LOOKS.len());

    // Playable tracks, bottom of the visual stack first (Nota lists top-to-bottom).
    let project: Value = serde_json::from_str(&fs::read_to_string(dir.join("project.json"))?)?;
    let mut tracks: Vec<(String, String)> = project["tracks"]
        .as_array()
        .map(|a| {
            a.iter()
                .filter(|t| matches!(t["type"].as_u64(), Some(0 | 1)))
                .filter_map(|t| Some((t["name"].as_str()?.to_owned(), t["uid"].as_str()?.to_owned())))
                .collect()
        })
        .unwrap_or_default();
    tracks.reverse();
    if tracks.is_empty() {
        return Err("no playable tracks in project.json".into());
    }
    let uids: Vec<String> = tracks.iter().map(|(_, uid)| uid.clone()).collect();
    let bpm = project["transport"]["bpm"].as_f64().unwrap_or(105.0) as f32;

    let mut comp = Composition::new(width, height, tracks.len(), columns);
    comp.name = "Syntetika Demo — Ink Industrial 2:59".into();
    comp.bpm = bpm;
    for (i, (name, _)) in tracks.iter().enumerate() {
        comp.layers[i].name = name.clone();
    }
    for (i, name) in column_names.iter().take(columns).enumerate() {
        if let Some(col) = comp.columns.get_mut(i) {
            col.name = name.clone();
        }
    }

    let shader = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../shaders/syntetika/ink-kaleido.fs");
    let has_ink = shader.is_file();
    let find = |name: &str| tracks.iter().position(|(n, _)| n.eq_ignore_ascii_case(name));

    // role, layer opacity, modulation, and its small offset inside the family.
    let roles = [
        ("DrumBrute", 0.95, ModSource::BassHits, 0.30, 0.00, 1.00, 0.00, 1.00, 1.00, 0.05, 0.00, -0.08, 1.00),
        ("Model D", 0.25, ModSource::Bass, 0.20, 0.02, 0.92, 0.08, 0.80, 1.05, -0.05, 0.02, -0.10, 1.00),
        ("Kobol", 0.22, ModSource::HighMid, 0.25, 0.04, 1.08, 0.00, 1.00, 1.10, -0.02, 0.03, 0.12, 1.05),
    ];

    for (role, opacity, source_mod, amount, dphase, dzoom, dsmoke, dshards, dsnakes, dcontrast, dhatch, dstar, dsize) in
        roles
    {
        let Some(layer) = find(role) else { continue };
        comp.layers[layer].opacity = opacity;
        comp.layers[layer].blend = BlendMode::Normal;
        for col in 0..scenes {
            let look = LOOKS[col];
            let source = if has_ink {
                ClipSource::Isf { generator: Effect::new("ink-kaleido") }
            } else {
                ClipSource::Generator {
                    kind: match role {
                        "DrumBrute" => GeneratorKind::Rings,
                        "Model D" => GeneratorKind::Bars,
                        _ => GeneratorKind::Plasma,
                    },
                }
            };
            let name = format!("{role} {}", column_names.get(col).map(String::as_str).unwrap_or(""));
            let params = [
                ("speed", look.speed),
                ("zoom", (look.zoom * dzoom).clamp(0.6, 2.5)),
                ("phase", (look.phase + dphase).fract()),
                ("hatch", (look.hatch + dhatch).clamp(0.0, 1.0)),
                ("grain", look.grain),
                ("contrast", (look.contrast + dcontrast).clamp(0.8, 2.5)),
                ("smoke", (look.smoke + dsmoke).clamp(0.0, 1.0)),
                ("star", (look.star + dstar).clamp(0.0, 1.0)),
                ("rings", look.rings),
                ("ringCount", look.ring_count),
                ("shards", (look.shards * dshards).clamp(0.0, 1.5)),
                ("snakes", (look.snakes * dsnakes).clamp(0.0, 1.5)),
                ("starSize", (look.star_size * dsize).clamp(0.5, 1.6)),
            ];
            add_clip(&mut comp, layer, col, source, &name, 8.0, source_mod, amount, &params);
        }
    }

    let out = json!({
        "version": 1,
        "layout_uids": uids,
        "composition": serde_json::to_value(&comp)?,
    });
    fs::write(&path, serde_json::to_string_pretty(&out)?)?;
    println!(
        "wrote {} ({} layers, {} columns, {} clips, {} looks, ink shader: {})",
        path.display(),
        comp.layers.len(),
        comp.columns.len(),
        comp.all_clips().count(),
        scenes,
        has_ink
    );
    Ok(())
}
