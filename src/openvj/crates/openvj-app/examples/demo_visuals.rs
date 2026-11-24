//! Writes the Syntetika demo's visual composition into an open project.
//!
//! The audio session is built separately (the Session grid); this fills the matching four
//! visual layers with generative clips per scene, plus effects and audio modulation. It reads
//! the project's existing `visuals/composition.json` only for the layout UIDs, size, columns
//! and layer names, so it works with whatever track order the project saved.
//!
//! Run:
//!   cargo run --release -p openvj-app --example demo_visuals -- \
//!     "/Users/me/Documents/Nota Projects/syntetika_demo.nota"

use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

use openvj_core::{BlendMode, ClipSource, Composition, Effect, GeneratorKind, ModSource, Modulation, PlayMode};
use serde_json::{Value, json};

fn put_source(
    comp: &mut Composition,
    layer: usize,
    col: usize,
    source: ClipSource,
    name: &str,
    sync: f32,
    effect: &str,
    source_mod: ModSource,
    amount: f32,
) {
    let Some(_) = comp.set_clip(layer, col, source) else { return };
    let Some(clip) = comp.clip_mut(layer, col) else { return };
    clip.name = name.to_owned();
    clip.sync_beats = Some(sync);
    clip.play_mode = PlayMode::Loop;
    clip.effects.push(Effect::new(effect));
    clip.mods[0] = Modulation { source: source_mod, amount, ..Default::default() };
}

fn put(
    comp: &mut Composition,
    layer: usize,
    col: usize,
    kind: GeneratorKind,
    name: &str,
    sync: f32,
    effect: &str,
    source: ModSource,
    amount: f32,
) {
    put_source(comp, layer, col, ClipSource::Generator { kind }, name, sync, effect, source, amount);
}

/// Copies a file into the bundle's content-addressed media folder and returns its
/// `media/<sha256>.<ext>` path, the form `vizLoad` resolves inside the project.
fn import_media(media_dir: &Path, src: &Path) -> Result<String, Box<dyn std::error::Error>> {
    let out = Command::new("shasum").arg("-a").arg("256").arg(src).output()?;
    if !out.status.success() {
        return Err(format!("shasum failed for {}", src.display()).into());
    }
    let hash = String::from_utf8(out.stdout)?.split_whitespace().next().ok_or("empty shasum output")?.to_owned();
    let ext = src.extension().and_then(|e| e.to_str()).unwrap_or("bin");
    let name = format!("{hash}.{ext}");
    fs::create_dir_all(media_dir)?;
    fs::copy(src, media_dir.join(&name))?;
    Ok(format!("media/{name}"))
}

/// An ISF clip references the shader by id; the app's bundled shader pack supplies it.
fn put_isf(
    comp: &mut Composition,
    layer: usize,
    col: usize,
    id: &str,
    name: &str,
    sync: f32,
    effect: &str,
    source: ModSource,
    amount: f32,
) {
    put_source(comp, layer, col, ClipSource::Isf { generator: Effect::new(id) }, name, sync, effect, source, amount);
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let dir = env::args().nth(1).ok_or("usage: demo_visuals <project.nota>")?;
    let dir = Path::new(&dir);
    let path = dir.join("visuals").join("composition.json");
    let doc: Value = serde_json::from_str(&fs::read_to_string(&path)?)?;
    let mut uids: Vec<String> = doc["layout_uids"]
        .as_array()
        .map(|a| a.iter().filter_map(|v| v.as_str().map(str::to_owned)).collect())
        .unwrap_or_default();
    if uids.is_empty() {
        // A quit-save before the uid-map fix could write `layout_uids: []`. The project's
        // track uids still identify the layers, bottom of the stack first.
        let project: Value = serde_json::from_str(&fs::read_to_string(dir.join("project.json"))?)?;
        uids = project["tracks"]
            .as_array()
            .map(|a| {
                a.iter()
                    .filter(|t| matches!(t["type"].as_u64(), Some(0 | 1)))
                    .filter_map(|t| t["uid"].as_str().map(str::to_owned))
                    .collect::<Vec<_>>()
            })
            .unwrap_or_default();
        uids.reverse();
    }
    let old = &doc["composition"];
    let width = old["width"].as_u64().unwrap_or(1280) as u32;
    let height = old["height"].as_u64().unwrap_or(720) as u32;
    let columns = old["columns"].as_array().map(|a| a.len()).unwrap_or(8).max(8);
    let layer_names: Vec<String> = old["layers"]
        .as_array()
        .map(|a| a.iter().map(|l| l["name"].as_str().unwrap_or("").to_owned()).collect())
        .unwrap_or_default();

    let mut comp = Composition::new(width, height, 4, columns);
    comp.name = "Syntetika Demo".into();
    comp.bpm = 124.0;
    for (i, name) in layer_names.iter().take(4).enumerate() {
        comp.layers[i].name = name.clone();
    }
    comp.layers[0].opacity = 0.85;
    comp.layers[1].opacity = 0.75;
    comp.layers[1].blend = BlendMode::Add;
    comp.layers[2].opacity = 0.8;
    comp.layers[2].blend = BlendMode::Screen;
    comp.layers[3].opacity = 0.9;

    // Layer order is bottom -> top: Pad, Bass, Drums, Lead (the project tracks reversed).
    for (col, name) in [(0, "Void intro"), (3, "Void break"), (4, "Void drop"), (5, "Void outro")] {
        put(&mut comp, 0, col, GeneratorKind::Tunnel, name, 8.0, "blur", ModSource::LowMid, 0.35);
    }
    for (col, name) in [(2, "Bars groove"), (4, "Bars drop")] {
        put(&mut comp, 1, col, GeneratorKind::Bars, name, 4.0, "kaleido", ModSource::Bass, 0.6);
    }
    for (col, name) in [(2, "Rings groove"), (4, "Rings drop")] {
        put(&mut comp, 2, col, GeneratorKind::Rings, name, 4.0, "rgb_split", ModSource::BassHits, 0.75);
    }
    for (col, name) in [(2, "Plasma arp"), (4, "Plasma drop")] {
        put(&mut comp, 3, col, GeneratorKind::Plasma, name, 4.0, "kaleido", ModSource::HighMid, 0.5);
    }

    // One clip of each media kind, so the demo shows every source type beside the generators.
    let media_dir = dir.join("visuals").join("media");
    let isf = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../shaders/ghost-arcade/DM-NoiseCloud.fs");
    if isf.is_file() {
        put_isf(&mut comp, 1, 1, "DM-NoiseCloud", "Noise pulse", 4.0, "rgb_split", ModSource::BassHits, 0.5);
        println!("ISF: {}", isf.display());
    } else {
        put(&mut comp, 1, 1, GeneratorKind::Bars, "Bars pulse", 4.0, "kaleido", ModSource::Bass, 0.6);
    }

    let image = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../../assets/syntetika-icon.svg.png");
    if image.is_file() {
        let path = import_media(&media_dir, &image)?;
        put_source(
            &mut comp,
            2,
            1,
            ClipSource::Image { path: path.clone().into() },
            "Glyph pulse",
            4.0,
            "pixelate",
            ModSource::Off,
            0.0,
        );
        println!("image: {path}");
    } else {
        put(&mut comp, 2, 1, GeneratorKind::Rings, "Rings pulse", 4.0, "rgb_split", ModSource::BassHits, 0.75);
    }

    let video = env::var_os("HOME").map(|h| PathBuf::from(h).join("Movies/Sintel_Trailer.mp4")).filter(|p| p.is_file());
    if let Some(video) = video {
        let path = import_media(&media_dir, &video)?;
        put_source(
            &mut comp,
            3,
            3,
            ClipSource::Video { path: path.clone().into() },
            "Sintel break",
            8.0,
            "kaleido",
            ModSource::Off,
            0.0,
        );
        println!("video: {path}");
    } else {
        put(&mut comp, 3, 3, GeneratorKind::Plasma, "Plasma break", 4.0, "kaleido", ModSource::HighMid, 0.5);
    }

    let out = json!({
        "version": 1,
        "layout_uids": uids,
        "composition": serde_json::to_value(&comp)?,
    });
    fs::write(&path, serde_json::to_string_pretty(&out)?)?;
    println!(
        "wrote {} ({} layers, {} columns, {} clips)",
        path.display(),
        comp.layers.len(),
        comp.columns.len(),
        comp.all_clips().count()
    );
    Ok(())
}
