//! Parameter editing for the NotA OpenVJ tab (Syntetika Engine).
//!
//! NotA shows the same sliders as openVJ's Clip panel: source parameters, the fixed
//! Transform, and every effect in the clip's chain (plus bypass and dry/wet). The schema is
//! built here from the same `EffectType`/`InputDef` metadata the openVJ UI uses, so labels,
//! ranges and units can never drift apart.

use openvj_core::{library, ClipId, EffectType};
use openvj_core::{Clip, ClipSource, Composition, Effect, GeneratorKind};
use serde_json::{json, Value};
use std::path::PathBuf;

/// A short name for the clip's source, for the tab's header line.
fn source_name(clip: &Clip) -> (String, String) {
    match &clip.source {
        ClipSource::Generator { kind } => ("generator".into(), kind.name().to_string()),
        ClipSource::Isf { generator } => ("isf".into(), generator.name()),
        ClipSource::Video { path } | ClipSource::Image { path } => {
            let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("media").to_string();
            ("media".into(), name)
        }
        ClipSource::Camera { .. } => ("camera".into(), "Camera".into()),
        ClipSource::Syphon { .. } => ("syphon".into(), "Syphon".into()),
        ClipSource::Milkdrop { .. } => ("milkdrop".into(), "MilkDrop".into()),
        ClipSource::Router { .. } => ("router".into(), "Layer Router".into()),
    }
}

/// One channel as a slider: the real value, in the unit the shader sees.
fn channel_json(ty: &EffectType, key: &str, value: f32) -> Option<Value> {
    let ch = ty.channels.iter().find(|c| c.key == key)?;
    let input = ty.def.inputs.get(ch.input)?;
    use openvj_core::isf::InputType;
    let comp = ch.component;
    match &input.ty {
        InputType::Float { min, max, .. } => Some(json!({
            "key": ch.key, "label": ch.label, "type": "float",
            "min": min, "max": max, "unit": input.unit,
            "value": min + (max - min) * value,
        })),
        InputType::Long { values, labels, .. } => {
            let i = ((value * values.len() as f32).floor() as usize).min(values.len().saturating_sub(1));
            Some(json!({
                "key": ch.key, "label": ch.label, "type": "long",
                "labels": labels, "values": values, "value": values.get(i).copied().unwrap_or(0),
            }))
        }
        InputType::Bool { .. } => Some(json!({
            "key": ch.key, "label": ch.label, "type": "bool",
            "value": value >= 0.5,
        })),
        InputType::Event => Some(json!({
            "key": ch.key, "label": ch.label, "type": "event", "value": 0,
        })),
        InputType::Color { .. } => Some(json!({
            "key": ch.key, "label": ch.label, "type": "float",
            "min": 0.0, "max": 1.0, "unit": "", "value": value,
        })),
        InputType::Point2D { min, max, .. } => Some(json!({
            "key": ch.key, "label": ch.label, "type": "float",
            "min": min[comp], "max": max[comp], "unit": input.unit,
            "value": min[comp] + (max[comp] - min[comp]) * value,
        })),
        InputType::Image => None,
    }
}

fn effect_json(group: &str, title: &str, fx: &Effect) -> Value {
    let params = match fx.ty() {
        Some(ty) => ty
            .channels
            .iter()
            .filter_map(|ch| {
                let v = fx.params.get(&ch.key).map_or(ch.default, |p| p.value);
                channel_json(&ty, &ch.key, v)
            })
            .collect::<Vec<_>>(),
        None => Vec::new(),
    };
    json!({
        "id": group,
        "title": title,
        "enabled": fx.enabled,
        "mix": fx.mix.value,
        "missing": fx.ty().is_none(),
        "params": params,
    })
}

fn generator_params(clip: &Clip, kind: GeneratorKind) -> Value {
    let labels = kind.params();
    let params = labels
        .iter()
        .enumerate()
        .map(|(i, spec)| {
            json!({
                "key": format!("p{i}"), "label": spec.name, "type": "float",
                "min": 0.0, "max": 100.0, "unit": "%",
                "value": clip.params.get(i).copied().unwrap_or(spec.default) * 100.0,
            })
        })
        .collect::<Vec<_>>();
    Value::Array(params)
}

/// The full state of one slot, for NotA's OpenVJ tab.
pub fn state(comp: &Composition, layer: usize, col: usize) -> Value {
    let Some(clip) = comp.clip(layer, col) else {
        return json!({ "has_visual": false, "layer": layer, "col": col });
    };
    let (kind, name) = source_name(clip);
    let note = if matches!(clip.source, ClipSource::Video { .. }) && !openvj_media::ffmpeg_available() {
        "Video needs FFmpeg: install it, or put ffmpeg and ffprobe next to the visual engine."
    } else {
        ""
    };
    let source_params = match &clip.source {
        ClipSource::Generator { kind } => generator_params(clip, *kind),
        ClipSource::Isf { generator } => {
            effect_json("source", "Source", generator)["params"].clone()
        }
        _ => Value::Array(Vec::new()),
    };
    let layer_state = comp.layers.get(layer).map(|l| {
        json!({
            "opacity": l.opacity,
            "blend": l.blend.name(),
            "bypass": l.bypass,
        })
    });
    let effects = clip
        .effects
        .iter()
        .enumerate()
        .map(|(i, fx)| effect_json(&format!("fx:{i}"), &fx.name(), fx))
        .collect::<Vec<_>>();
    json!({
        "has_visual": true,
        "layer": layer,
        "col": col,
        "source": { "kind": kind, "name": name, "note": note },
        "groups": {
            "source": source_params,
            "transform": effect_json("transform", "Transform", &clip.transform),
            "effects": effects,
        },
        "layer_state": layer_state,
        "playback": {
            "speed": clip.speed,
            "sync_beats": clip.sync_beats,
            "play_mode": clip.play_mode.name(),
            "trigger": clip.trigger.name(),
        },
    })
}

/// Blend mode by its display name, for the layer control.
pub fn blend_by_name(name: &str) -> Option<openvj_core::BlendMode> {
    (0..9).map(openvj_core::BlendMode::from_index).find(|b| b.name().eq_ignore_ascii_case(name))
}

/// Applies a slider edit. `group` is "source", "transform" or "fx:N"; values arrive in the
/// real unit and are normalised here, exactly as a wheel/drag in openVJ would.
pub fn set(comp: &mut Composition, layer: usize, col: usize, group: &str, key: &str, value: f32) {
    let Some(clip) = comp.clip_mut(layer, col) else { return };
    match group {
        "source" => match &mut clip.source {
            ClipSource::Generator { kind } => {
                if let Some(i) = key.strip_prefix('p').and_then(|s| s.parse::<usize>().ok())
                    && i < clip.params.len()
                {
                    clip.params[i] = (value / 100.0).clamp(0.0, 1.0);
                }
                let _ = kind;
            }
            ClipSource::Isf { generator } => set_channel(generator, key, value),
            _ => {}
        },
        "transform" => set_channel(&mut clip.transform, key, value),
        _ => {
            if let Some(i) = group.strip_prefix("fx:").and_then(|s| s.parse::<usize>().ok())
                && let Some(fx) = clip.effects.get_mut(i)
            {
                set_channel(fx, key, value);
            }
        }
    }
}

fn set_channel(fx: &mut Effect, key: &str, value: f32) {
    let Some(ty) = fx.ty() else { return };
    let Some(ch) = ty.channels.iter().find(|c| c.key == key) else { return };
    let Some(input) = ty.def.inputs.get(ch.input) else { return };
    let norm = input.norm(ch.component, value);
    if let Some(p) = fx.param_mut(key) {
        p.value = norm;
    }
}

/// Speed / beat sync / play mode from the tab.
pub fn set_playback(
    comp: &mut Composition,
    layer: usize,
    col: usize,
    speed: Option<f32>,
    sync_beats: Option<Option<f32>>,
    play_mode: Option<&str>,
    trigger: Option<&str>,
) {
    let Some(clip) = comp.clip_mut(layer, col) else { return };
    if let Some(s) = speed {
        clip.speed = s.clamp(0.05, 8.0);
    }
    if let Some(sync) = sync_beats {
        clip.sync_beats = sync.map(|b| b.clamp(0.25, 256.0));
    }
    if let Some(name) = play_mode {
        use openvj_core::PlayMode;
        let modes = [
            (PlayMode::Loop, "Loop"),
            (PlayMode::Bounce, "Bounce"),
            (PlayMode::Random, "Random"),
            (PlayMode::OnceClear, "Play once & clear"),
            (PlayMode::OnceHold, "Play once & hold"),
        ];
        if let Some((mode, _)) = modes.iter().find(|(m, _)| m.name().eq_ignore_ascii_case(name)) {
            clip.play_mode = *mode;
        }
    }
    if let Some(name) = trigger {
        use openvj_core::TriggerStyle;
        let styles = [
            (TriggerStyle::Normal, "normal"),
            (TriggerStyle::Toggle, "toggle"),
            (TriggerStyle::Piano, "piano"),
        ];
        if let Some((style, _)) = styles.iter().find(|(s, _)| s.name().eq_ignore_ascii_case(name)) {
            clip.trigger = *style;
        }
    }
}

/// Effect chain edits: add, remove, move, bypass, dry/wet.
pub fn fx_op(comp: &mut Composition, layer: usize, col: usize, op: &str, a: usize, b: usize, id: &str, value: f32) {
    let Some(clip) = comp.clip_mut(layer, col) else { return };
    match op {
        "add" => {
            if !id.is_empty() {
                clip.effects.push(Effect::new(id));
            }
        }
        "remove" => {
            if a < clip.effects.len() {
                clip.effects.remove(a);
            }
        }
        "move" => {
            if a < clip.effects.len() && b < clip.effects.len() && a != b {
                let fx = clip.effects.remove(a);
                clip.effects.insert(b, fx);
            }
        }
        "enabled" => {
            if let Some(fx) = clip.effects.get_mut(a) {
                fx.enabled = value >= 0.5;
            }
        }
        "mix" => {
            if let Some(fx) = clip.effects.get_mut(a) {
                fx.mix.value = value.clamp(0.0, 1.0);
            }
        }
        _ => {}
    }
}

/// Creates a visual on a slot from a catalog entry ("generator:plasma" / "isf:…").
pub fn assign(comp: &mut Composition, layer: usize, col: usize, kind: &str, id: &str) -> bool {
    if layer >= comp.layers.len() || col >= comp.columns.len() {
        return false;
    }
    let source = match kind {
        "generator" => GeneratorKind::from_name(id).map(|k| ClipSource::Generator { kind: k }),
        "isf" => library()
            .generator(id)
            .map(|t| ClipSource::Isf { generator: Effect::new(&t.def.id) }),
        _ => None,
    };
    let Some(source) = source else { return false };
    comp.set_clip(layer, col, source).is_some()
}

/// Assigns a video or image file (the path is absolute for this session; `vizSave` rewrites
/// it to `media/<name>` relative to the project bundle).
pub fn assign_media(comp: &mut Composition, layer: usize, col: usize, kind: &str, path: &str) -> Option<ClipId> {
    if layer >= comp.layers.len() || col >= comp.columns.len() {
        return None;
    }
    let source = match kind {
        "video" => ClipSource::Video { path: PathBuf::from(path) },
        "image" => ClipSource::Image { path: PathBuf::from(path) },
        _ => return None,
    };
    comp.set_clip(layer, col, source)
}

/// Assigns a camera or Syphon source by the name/id openVJ uses.
pub fn assign_live(comp: &mut Composition, layer: usize, col: usize, kind: &str, id: &str, app: &str, server: &str) -> Option<ClipId> {
    if layer >= comp.layers.len() || col >= comp.columns.len() {
        return None;
    }
    let source = match kind {
        "camera" => ClipSource::Camera { device: id.to_owned(), device_id: String::new() },
        "syphon" => ClipSource::Syphon { app: app.to_owned(), server: server.to_owned() },
        _ => return None,
    };
    comp.set_clip(layer, col, source)
}

/// Turns absolute media paths under the given folders into `media/<file>` before saving.
pub fn paths_relative(value: &mut Value, media_dirs: &[String]) {
    walk_paths(value, &mut |p| {
        for dir in media_dirs {
            let prefix = format!("{}/", dir.trim_end_matches('/'));
            if let Some(rest) = p.strip_prefix(&prefix) {
                *p = format!("media/{rest}");
                return;
            }
        }
    });
}

/// Turns `media/<file>` back into an absolute path under the project's media folder.
pub fn paths_absolute(value: &mut Value, media_dir: &str) {
    let base = media_dir.trim_end_matches('/').to_owned();
    walk_paths(value, &mut |p| {
        if let Some(rest) = p.strip_prefix("media/") {
            *p = format!("{base}/{rest}");
        }
    });
}

fn walk_paths(value: &mut Value, f: &mut impl FnMut(&mut String)) {
    match value {
        Value::Object(map) => {
            for (key, child) in map.iter_mut() {
                if key == "path" {
                    if let Value::String(s) = child {
                        f(s);
                    }
                } else {
                    walk_paths(child, f);
                }
            }
        }
        Value::Array(items) => {
            for child in items {
                walk_paths(child, f);
            }
        }
        _ => {}
    }
}

pub fn clear(comp: &mut Composition, layer: usize, col: usize) {
    if layer < comp.layers.len() && col < comp.columns.len() {
        comp.remove_clip(layer, col);
    }
}

/// Names for the assign / add-effect pickers.
pub fn catalog(kind: &str, query: &str) -> Value {
    let q = query.to_lowercase();
    let mut items = Vec::new();
    match kind {
        "generators" => {
            for k in GeneratorKind::ALL {
                if q.is_empty() || k.name().to_lowercase().contains(&q) {
                    items.push(json!({ "kind": "generator", "id": k.name().to_lowercase(), "name": k.name() }));
                }
            }
            for t in library().generators() {
                let name = t.def.name.clone();
                if q.is_empty() || name.to_lowercase().contains(&q) || t.def.id.to_lowercase().contains(&q) {
                    items.push(json!({ "kind": "isf", "id": t.def.id, "name": name }));
                }
            }
        }
        "cameras" => {
            for d in openvj_media::live::list_cameras() {
                items.push(json!({ "kind": "camera", "id": d.id, "name": d.name }));
            }
        }
        "syphon" => {
            for s in openvj_media::live::list_syphon_servers() {
                let name = if s.server.is_empty() { s.app.clone() } else { format!("{} · {}", s.app, s.server) };
                items.push(json!({ "kind": "syphon", "id": format!("{}|{}", s.app, s.server), "name": name, "app": s.app, "server": s.server }));
            }
        }
        _ => {
            for t in library().all() {
                let name = t.def.name.clone();
                if q.is_empty() || name.to_lowercase().contains(&q) || t.def.id.to_lowercase().contains(&q) {
                    items.push(json!({ "kind": "effect", "id": t.def.id, "name": name }));
                }
            }
        }
    }
    json!({ "items": items.into_iter().take(500).collect::<Vec<_>>() })
}
