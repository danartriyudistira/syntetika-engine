//! Writes the Syntetika demo's visual composition into an open project.
//!
//! The audio session is built separately (the Session grid); this maps one visual layer to
//! every playable track and fills the demo's scenes with generative clips, plus effects and
//! audio modulation. Layers are matched to tracks by name (bottom of the stack first), so it
//! works with whatever track order the project saved.
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
    comp.name = "Syntetika Demo — Melodic Techno".into();
    comp.bpm = 124.0;
    for (i, (name, _)) in tracks.iter().enumerate() {
        comp.layers[i].name = name.clone();
    }
    for (i, name) in column_names.iter().take(columns).enumerate() {
        if let Some(col) = comp.columns.get_mut(i) {
            col.name = name.clone();
        }
    }

    let find = |name: &str| tracks.iter().position(|(n, _)| n.eq_ignore_ascii_case(name));
    let scene = |word: &str, col: usize| format!("{word} {}", column_names.get(col).map(String::as_str).unwrap_or(""));

    if let Some(pad) = find("Pad") {
        comp.layers[pad].opacity = 0.85;
        for col in 0..6 {
            put(
                &mut comp,
                pad,
                col,
                GeneratorKind::Tunnel,
                &scene("Tunnel", col),
                16.0,
                "blur",
                ModSource::LowMid,
                0.35,
            );
        }
    }

    if let Some(bass) = find("Bass") {
        comp.layers[bass].opacity = 0.72;
        comp.layers[bass].blend = BlendMode::Add;
        for col in [1, 2, 4] {
            put(&mut comp, bass, col, GeneratorKind::Bars, &scene("Bars", col), 8.0, "kaleido", ModSource::Bass, 0.6);
        }
    }

    let media_dir = dir.join("visuals").join("media");
    let image = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../../assets/syntetika-icon.svg.png");
    let image_path = if image.is_file() { Some(import_media(&media_dir, &image)?) } else { None };
    let video = env::var_os("HOME").map(|h| PathBuf::from(h).join("Movies/Sintel_Trailer.mp4")).filter(|p| p.is_file());
    let video_path = match video {
        Some(video) => Some(import_media(&media_dir, &video)?),
        None => None,
    };

    if let Some(drums) = find("Drums") {
        comp.layers[drums].opacity = 0.8;
        comp.layers[drums].blend = BlendMode::Screen;
        match &image_path {
            Some(path) => put_source(
                &mut comp,
                drums,
                1,
                ClipSource::Image { path: path.clone().into() },
                "Glyph pulse",
                8.0,
                "pixelate",
                ModSource::Off,
                0.0,
            ),
            None => put(
                &mut comp,
                drums,
                1,
                GeneratorKind::Rings,
                "Rings pulse",
                8.0,
                "rgb_split",
                ModSource::BassHits,
                0.75,
            ),
        }
        for col in [2, 4] {
            put(
                &mut comp,
                drums,
                col,
                GeneratorKind::Rings,
                &scene("Rings", col),
                8.0,
                "rgb_split",
                ModSource::BassHits,
                0.75,
            );
        }
    }

    if let Some(arp) = find("Arp") {
        comp.layers[arp].opacity = 0.78;
        comp.layers[arp].blend = BlendMode::Add;
        let isf = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../shaders/ghost-arcade/DM-NoiseCloud.fs");
        if isf.is_file() {
            for (col, effect) in [(0, "rgb_split"), (2, "wave"), (3, "pixelate"), (4, "rgb_split"), (5, "mirror")] {
                put_isf(
                    &mut comp,
                    arp,
                    col,
                    "DM-NoiseCloud",
                    &scene("Noise", col),
                    8.0,
                    effect,
                    ModSource::BassHits,
                    0.5,
                );
            }
        } else {
            for col in [0, 2, 3, 4, 5] {
                put(
                    &mut comp,
                    arp,
                    col,
                    GeneratorKind::Bars,
                    &scene("Bars", col),
                    8.0,
                    "wave",
                    ModSource::BassHits,
                    0.5,
                );
            }
        }
    }

    if let Some(lead) = find("Lead") {
        comp.layers[lead].opacity = 0.95;
        match &video_path {
            Some(path) => put_source(
                &mut comp,
                lead,
                3,
                ClipSource::Video { path: path.clone().into() },
                "Sintel break",
                8.0,
                "kaleido",
                ModSource::Off,
                0.0,
            ),
            None => {
                put(&mut comp, lead, 3, GeneratorKind::Plasma, "Plasma break", 8.0, "kaleido", ModSource::HighMid, 0.5)
            }
        }
        put(&mut comp, lead, 4, GeneratorKind::Plasma, "Plasma drop", 8.0, "kaleido", ModSource::HighMid, 0.5);
        put(&mut comp, lead, 5, GeneratorKind::Plasma, "Plasma outro", 16.0, "blur", ModSource::HighMid, 0.4);
    }

    let out = json!({
        "version": 1,
        "layout_uids": uids,
        "composition": serde_json::to_value(&comp)?,
    });
    fs::write(&path, serde_json::to_string_pretty(&out)?)?;
    if let Some(path) = &image_path {
        println!("image: {path}");
    }
    if let Some(path) = &video_path {
        println!("video: {path}");
    }
    println!(
        "wrote {} ({} layers, {} columns, {} clips)",
        path.display(),
        comp.layers.len(),
        comp.columns.len(),
        comp.all_clips().count()
    );
    Ok(())
}
