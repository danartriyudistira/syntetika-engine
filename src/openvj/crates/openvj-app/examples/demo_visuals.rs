//! Writes the 3-layer industrial demo's visuals into an open project.
//!
//! One layer per playable track (bottom of the stack first). Each layer plays the bespoke
//! `ink-kaleido` shader with its own zoom, phase, hatching and blend, covering the five
//! sections. Falls back to built-in generators when the shader pack isn't present.
//!
//! Run:
//!   cargo run --release -p openvj-app --example demo_visuals -- \
//!     "/Users/me/Documents/Nota Projects/syntetika_demo.nota"

use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use openvj_core::{BlendMode, ClipSource, Composition, Effect, GeneratorKind, ModSource, Modulation, Param, PlayMode};
use serde_json::{Value, json};

fn add_clip(
    comp: &mut Composition,
    layer: usize,
    col: usize,
    source: ClipSource,
    name: &str,
    sync: f32,
    effect: Option<&str>,
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
    if let Some(effect) = effect {
        clip.effects.push(Effect::new(effect));
    }
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

    let mut comp = Composition::new(width, height, tracks.len(), columns);
    comp.name = "Syntetika Demo — Ink Industrial 2:59".into();
    comp.bpm = 105.0;
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
    let scene = |word: &str, col: usize| format!("{word} {}", column_names.get(col).map(String::as_str).unwrap_or(""));

    // Five sections: Intro, Drop 1, Break, Drop 2, Outro.
    for col in 0..5 {
        if let Some(layer) = find("DrumBrute") {
            comp.layers[layer].opacity = 0.95;
            let source = if has_ink {
                ClipSource::Isf { generator: Effect::new("ink-kaleido") }
            } else {
                ClipSource::Generator { kind: GeneratorKind::Rings }
            };
            add_clip(
                &mut comp,
                layer,
                col,
                source,
                &scene("Brute ink", col),
                8.0,
                None,
                ModSource::BassHits,
                0.3,
                &[
                    ("speed", 0.20),
                    ("zoom", 1.55),
                    ("phase", 0.0),
                    ("hatch", 0.72),
                    ("grain", 0.33),
                    ("contrast", 2.05),
                    ("smoke", 0.55),
                    ("star", 0.60),
                ],
            );
        }
        if let Some(layer) = find("Model D") {
            comp.layers[layer].opacity = 0.25;
            comp.layers[layer].blend = BlendMode::Normal;
            let source = if has_ink {
                ClipSource::Isf { generator: Effect::new("ink-kaleido") }
            } else {
                ClipSource::Generator { kind: GeneratorKind::Bars }
            };
            add_clip(
                &mut comp,
                layer,
                col,
                source,
                &scene("Bass ink", col),
                8.0,
                None,
                ModSource::Bass,
                0.2,
                &[
                    ("speed", 0.20),
                    ("zoom", 1.55),
                    ("phase", 0.00),
                    ("hatch", 0.72),
                    ("grain", 0.33),
                    ("contrast", 2.05),
                    ("smoke", 0.55),
                    ("star", 0.60),
                ],
            );
        }
        if let Some(layer) = find("Kobol") {
            comp.layers[layer].opacity = 0.22;
            comp.layers[layer].blend = BlendMode::Normal;
            let source = if has_ink {
                ClipSource::Isf { generator: Effect::new("ink-kaleido") }
            } else {
                ClipSource::Generator { kind: GeneratorKind::Plasma }
            };
            add_clip(
                &mut comp,
                layer,
                col,
                source,
                &scene("Kobol ink", col),
                8.0,
                None,
                ModSource::HighMid,
                0.25,
                &[
                    ("speed", 0.20),
                    ("zoom", 1.55),
                    ("phase", 0.00),
                    ("hatch", 0.72),
                    ("grain", 0.33),
                    ("contrast", 2.05),
                    ("smoke", 0.55),
                    ("star", 0.60),
                ],
            );
        }
    }

    let out = json!({
        "version": 1,
        "layout_uids": uids,
        "composition": serde_json::to_value(&comp)?,
    });
    fs::write(&path, serde_json::to_string_pretty(&out)?)?;
    println!(
        "wrote {} ({} layers, {} columns, {} clips, ink shader: {})",
        path.display(),
        comp.layers.len(),
        comp.columns.len(),
        comp.all_clips().count(),
        has_ink
    );
    Ok(())
}
