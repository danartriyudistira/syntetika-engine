//! Undo/redo for visual edits.
//!
//! Nota's OpenVJ tab and the video import paths change the composition through the bridge.
//! Every mutating command marks the affected slots/layers here *before* it applies, so undo
//! restores the exact clip definition (source, parameters, playback, effects) and the layer's
//! opacity/blend/bypass. Marks for the same control coalesce, so a slider drag is one step.
//!
//! Playback is deliberately not part of the snapshot: undo changes what a clip *is*, not what
//! is playing. If the layer was playing a clip that the restored state removes, the layer is
//! left without an active clip instead of pointing at something that no longer exists.

use openvj_core::{BlendMode, Clip, Composition};

const MAX_STEPS: usize = 100;

#[derive(Clone)]
pub(crate) struct SlotShot {
    pub(crate) layer: usize,
    pub(crate) col: usize,
    clip: Option<Clip>,
}

#[derive(Clone, Copy)]
pub(crate) struct LayerShot {
    pub(crate) layer: usize,
    opacity: f32,
    blend: BlendMode,
    bypass: bool,
}

/// One undo step: the clips and layer settings to put back.
#[derive(Clone)]
pub(crate) struct VisualEdit {
    pub(crate) slots: Vec<SlotShot>,
    pub(crate) layers: Vec<LayerShot>,
}

pub(crate) struct VisualHistory {
    undo: Vec<VisualEdit>,
    redo: Vec<VisualEdit>,
    /// Time and key of the last mark, to coalesce a drag into one step.
    last: Option<(f64, String)>,
}

impl VisualHistory {
    pub fn new() -> Self {
        Self { undo: Vec::new(), redo: Vec::new(), last: None }
    }

    pub fn undo_len(&self) -> usize {
        self.undo.len()
    }

    pub fn redo_len(&self) -> usize {
        self.redo.len()
    }

    /// Records the current state before an edit. `key` names the control; marks with the same
    /// key within `coalesce` seconds fold into the previous step. Returns true when a new step
    /// was pushed (the caller can then tell Nota the counts changed).
    pub fn mark(
        &mut self,
        comp: &Composition,
        slots: &[(usize, usize)],
        layers: &[usize],
        key: String,
        now: f64,
        coalesce: f64,
    ) -> bool {
        if coalesce > 0.0
            && let Some((at, last)) = &self.last
            && *last == key
            && now - *at < coalesce
        {
            self.last = Some((now, key));
            return false;
        }
        let edit = snapshot(comp, slots, layers);
        if edit.slots.is_empty() && edit.layers.is_empty() {
            return false;
        }
        self.undo.push(edit);
        if self.undo.len() > MAX_STEPS {
            self.undo.remove(0);
        }
        self.redo.clear();
        self.last = Some((now, key));
        true
    }

    /// Restores the previous step and returns it (the caller reloads media and echoes state).
    pub fn undo(&mut self, comp: &mut Composition) -> Option<VisualEdit> {
        let edit = self.undo.pop()?;
        self.redo.push(snapshot_like(comp, &edit));
        apply(comp, &edit);
        self.last = None;
        Some(edit)
    }

    pub fn redo(&mut self, comp: &mut Composition) -> Option<VisualEdit> {
        let edit = self.redo.pop()?;
        self.undo.push(snapshot_like(comp, &edit));
        apply(comp, &edit);
        self.last = None;
        Some(edit)
    }

    /// Drops the history (composition or layout changed under it). True when it had entries.
    pub fn clear(&mut self) -> bool {
        let had = !self.undo.is_empty() || !self.redo.is_empty();
        self.undo.clear();
        self.redo.clear();
        self.last = None;
        had
    }
}

fn snapshot(comp: &Composition, slots: &[(usize, usize)], layers: &[usize]) -> VisualEdit {
    let mut seen = Vec::new();
    let mut slot_shots = Vec::new();
    for &(layer, col) in slots {
        let Some(l) = comp.layers.get(layer) else { continue };
        if col >= l.clips.len() {
            continue;
        }
        slot_shots.push(SlotShot { layer, col, clip: l.clips[col].clone() });
        if !seen.contains(&layer) {
            seen.push(layer);
        }
    }
    for &layer in layers {
        if !seen.contains(&layer) {
            seen.push(layer);
        }
    }
    let layer_shots = seen
        .into_iter()
        .filter_map(|layer| {
            let l = comp.layers.get(layer)?;
            Some(LayerShot { layer, opacity: l.opacity, blend: l.blend, bypass: l.bypass })
        })
        .collect();
    VisualEdit { slots: slot_shots, layers: layer_shots }
}

/// The current state at the same places an edit touches, for the opposite stack.
fn snapshot_like(comp: &Composition, edit: &VisualEdit) -> VisualEdit {
    let slots: Vec<(usize, usize)> = edit.slots.iter().map(|s| (s.layer, s.col)).collect();
    let layers: Vec<usize> = edit.layers.iter().map(|l| l.layer).collect();
    snapshot(comp, &slots, &layers)
}

fn apply(comp: &mut Composition, edit: &VisualEdit) {
    for s in &edit.slots {
        let Some(layer) = comp.layers.get_mut(s.layer) else { continue };
        if s.col >= layer.clips.len() {
            continue;
        }
        layer.clips[s.col] = s.clip.clone();
    }
    for l in &edit.layers {
        if let Some(layer) = comp.layers.get_mut(l.layer) {
            layer.opacity = l.opacity;
            layer.blend = l.blend;
            layer.bypass = l.bypass;
        }
    }
    // A layer must not keep playing a clip the step removed.
    for s in &edit.slots {
        if let Some(layer) = comp.layers.get_mut(s.layer)
            && let Some(active) = layer.active
            && !layer.clips.iter().flatten().any(|c| c.id == active.clip_id)
        {
            layer.active = None;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use openvj_core::{ClipSource, GeneratorKind};

    fn comp_with_clip() -> Composition {
        let mut comp = Composition::new(64, 36, 1, 1);
        comp.set_clip(0, 0, ClipSource::Generator { kind: GeneratorKind::Plasma }).unwrap();
        comp
    }

    #[test]
    fn undo_and_redo_a_parameter() {
        let mut comp = comp_with_clip();
        let before = comp.clip(0, 0).unwrap().params[0];
        let mut h = VisualHistory::new();
        assert!(h.mark(&comp, &[(0, 0)], &[0], "set".into(), 1.0, 0.8));
        comp.clip_mut(0, 0).unwrap().params[0] = 0.75;
        assert_eq!(h.undo_len(), 1);
        let edit = h.undo(&mut comp).unwrap();
        assert!(!edit.slots.is_empty());
        assert_eq!(comp.clip(0, 0).unwrap().params[0], before);
        h.redo(&mut comp).unwrap();
        assert_eq!(comp.clip(0, 0).unwrap().params[0], 0.75);
    }

    #[test]
    fn a_drag_coalesces_but_a_different_control_does_not() {
        let mut comp = comp_with_clip();
        let mut h = VisualHistory::new();
        h.mark(&comp, &[(0, 0)], &[0], "p0".into(), 1.0, 0.8);
        h.mark(&comp, &[(0, 0)], &[0], "p0".into(), 1.1, 0.8);
        h.mark(&comp, &[(0, 0)], &[0], "p1".into(), 1.2, 0.8);
        assert_eq!(h.undo_len(), 2);
    }

    #[test]
    fn undo_restores_a_removed_clip_and_layer_state() {
        let mut comp = comp_with_clip();
        let mut h = VisualHistory::new();
        h.mark(&comp, &[(0, 0)], &[0], "edit".into(), 1.0, 0.0);
        comp.remove_clip(0, 0);
        comp.layers[0].opacity = 0.25;
        h.undo(&mut comp).unwrap();
        assert!(comp.clip(0, 0).is_some());
        assert_eq!(comp.layers[0].opacity, 1.0);
    }

    #[test]
    fn a_new_edit_clears_redo() {
        let mut comp = comp_with_clip();
        let mut h = VisualHistory::new();
        h.mark(&comp, &[(0, 0)], &[0], "a".into(), 1.0, 0.0);
        h.undo(&mut comp).unwrap();
        assert_eq!(h.redo_len(), 1);
        h.mark(&comp, &[(0, 0)], &[0], "b".into(), 2.0, 0.0);
        assert_eq!(h.redo_len(), 0);
    }

    #[test]
    fn undo_restores_both_slots_of_a_copy_paste() {
        let mut comp = Composition::new(64, 36, 2, 2);
        comp.set_clip(0, 0, ClipSource::Generator { kind: GeneratorKind::Plasma }).unwrap();
        comp.set_clip(1, 1, ClipSource::Generator { kind: GeneratorKind::Rings }).unwrap();
        let source = comp.clip(0, 0).unwrap().source.clone();
        let mut h = VisualHistory::new();
        h.mark(&comp, &[(0, 0), (1, 1)], &[0, 1], "copy".into(), 1.0, 0.0);
        comp.set_clip(1, 1, source).unwrap();
        h.undo(&mut comp).unwrap();
        assert!(matches!(comp.clip(1, 1).unwrap().source, ClipSource::Generator { kind: GeneratorKind::Rings }));
        h.redo(&mut comp).unwrap();
        assert!(matches!(comp.clip(1, 1).unwrap().source, ClipSource::Generator { kind: GeneratorKind::Plasma }));
    }

    #[test]
    fn undo_restores_a_media_assignment() {
        let mut comp = comp_with_clip();
        let image = std::path::PathBuf::from("/tmp/a.png");
        comp.set_clip(0, 0, ClipSource::Image { path: image.clone() }).unwrap();
        let mut h = VisualHistory::new();
        h.mark(&comp, &[(0, 0)], &[0], "assign".into(), 1.0, 0.0);
        comp.set_clip(0, 0, ClipSource::Video { path: "/tmp/b.mp4".into() }).unwrap();
        h.undo(&mut comp).unwrap();
        match &comp.clip(0, 0).unwrap().source {
            ClipSource::Image { path } => assert_eq!(path, &image),
            other => panic!("expected the image back, got {other:?}"),
        }
        h.redo(&mut comp).unwrap();
        assert!(matches!(comp.clip(0, 0).unwrap().source, ClipSource::Video { .. }));
    }

    #[test]
    fn undo_restores_playback_and_effects() {
        let mut comp = comp_with_clip();
        let mut h = VisualHistory::new();
        h.mark(&comp, &[(0, 0)], &[0], "play".into(), 1.0, 0.0);
        {
            let clip = comp.clip_mut(0, 0).unwrap();
            clip.sync_beats = Some(4.0);
            clip.speed = 2.0;
            clip.effects.push(openvj_core::Effect::new("kaleido"));
        }
        h.undo(&mut comp).unwrap();
        {
            let clip = comp.clip(0, 0).unwrap();
            assert_eq!(clip.sync_beats, None);
            assert_eq!(clip.speed, 1.0);
            assert!(clip.effects.is_empty());
        }
        h.redo(&mut comp).unwrap();
        {
            let clip = comp.clip(0, 0).unwrap();
            assert_eq!(clip.sync_beats, Some(4.0));
            assert_eq!(clip.speed, 2.0);
            assert_eq!(clip.effects.len(), 1);
        }
    }
}
