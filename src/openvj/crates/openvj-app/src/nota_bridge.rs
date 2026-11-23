//! Bridge to Nota (Syntetika Engine).
//!
//! Nota runs the audio engine and owns musical time; this process renders the visuals.
//! A loopback TCP socket carries Nota's transport (~125 Hz) and clip-launch commands.
//! Launch commands name the **beat** they must happen on, so the visual engine schedules
//! them on the same musical grid instead of reacting to packet arrival.
//!
//! Protocol v1, one JSON object per line:
//!   {"t":"hello","v":1}
//!   {"t":"clock","beat":12.5,"bpm":120,"playing":true}
//!   {"t":"scene","col":3,"atBeat":16}
//!   {"t":"slot","layer":0,"col":3,"atBeat":16}
//!   {"t":"stopLayer","layer":0}
//!   {"t":"stopAll"}
//!   {"t":"vizOutput","show":true}
//!   {"t":"vizUi","show":true}
//!   {"t":"vizMapping","show":true}
//!   {"t":"vizSlotTiming","layer":0,"col":0,"beats":8,"loop":true}
//!   {"t":"vizSlotPhase","layer":0,"col":0,"phase":3.5}
//!   {"t":"vizCopy","fromLayer":0,"fromCol":0,"toLayer":0,"toCol":1,"move":false}

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::mpsc::{Receiver, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

/// The newest transport sample from Nota, stamped with the local arrival time so the
/// render loop can extrapolate the beat between packets.
#[derive(Clone, Copy, Debug)]
pub struct ClockSample {
    pub beat: f64,
    pub bpm: f32,
    /// Nota's transport state. Kept for the freeze/clear choice when the transport stops.
    #[allow(dead_code)]
    pub playing: bool,
    pub at: Instant,
}

/// One playable NotA track, top to bottom. `uid` survives save/load; the visual layer is
/// matched by it, so reordering a track moves its visuals instead of swapping them.
#[derive(Clone, Debug)]
pub struct LayoutTrack {
    pub uid: String,
    pub name: String,
    pub kind: i64,
}

#[derive(Clone, Debug)]
pub enum Command {
    Scene { col: usize, at_beat: f64 },
    Slot { layer: usize, col: usize, at_beat: f64 },
    StopLayer { layer: usize, at_beat: Option<f64> },
    StopAll { at_beat: Option<f64> },
    Layout { tracks: Vec<LayoutTrack>, scenes: Vec<String> },
    /// Mirror NotA's selected slot: show that clip in Preview/Properties.
    Select { layer: usize, col: usize },
    /// OpenVJ tab: read or edit one slot's visual.
    VizGet { layer: usize, col: usize, seq: u64 },
    VizSet { layer: usize, col: usize, group: String, key: String, value: f32 },
    VizPlayback { layer: usize, col: usize, speed: Option<f32>, sync: Option<Option<f32>>, play_mode: Option<String>, trigger: Option<String> },
    VizFx { layer: usize, col: usize, op: String, a: usize, b: usize, id: String, value: f32 },
    VizAssign { layer: usize, col: usize, kind: String, id: String, path: String, app: String, server: String },
    VizClear { layer: usize, col: usize },
    VizCatalog { kind: String, query: String, seq: u64 },
    VizSave { path: String, uids: Vec<String>, media_dirs: Vec<String> },
    VizLoad { path: String, media_dir: String },
    VizLayer { layer: usize, opacity: Option<f32>, blend: Option<String>, bypass: Option<bool> },
    /// Show or hide the output window (Nota's View ▸ Visual Output).
    VizOutput { show: bool },
    /// Show or hide the editor window (Nota's View ▸ OpenVJ).
    VizUi { show: bool },
    /// Show or hide the output mapping window (Nota's View ▸ Projection Mapping).
    VizMapping { show: bool },
    /// The audio clip's loop length/state changed: retime the visual in that slot.
    VizSlotTiming { layer: usize, col: usize, beats: f32, bpm: f32, looped: bool, start: f32 },
    /// The audio slot's local beat phase, so the video's start/loop seam follows it.
    VizSlotPhase { layer: usize, col: usize, phase: f32, sample_beat: Option<f64> },
    /// Copy or move a slot's visual with its Session clip (copy/paste, duplicate, drag).
    VizCopy { from_layer: usize, from_col: usize, to_layer: usize, to_col: usize, moving: bool },
}

pub struct NotaBridge {
    clock: Arc<Mutex<Option<ClockSample>>>,
    rx: Receiver<Command>,
    /// Write half of the current connection, for replies (viz state, catalog).
    writer: Arc<Mutex<Option<TcpStream>>>,
}

impl NotaBridge {
    /// Connects in the background and reconnects whenever Nota restarts. Rendering is
    /// never blocked by the bridge.
    pub fn connect(addr: &str) -> Self {
        let clock = Arc::new(Mutex::new(None));
        let (tx, rx) = std::sync::mpsc::channel();
        let addr = addr.to_string();
        let writer = Arc::new(Mutex::new(None));
        let thread_clock = Arc::clone(&clock);
        let thread_writer = Arc::clone(&writer);
        std::thread::Builder::new()
            .name("nota-bridge".into())
            .spawn(move || run(&addr, tx, thread_clock, thread_writer))
            .ok();
        Self { clock, rx, writer }
    }

    pub fn clock(&self) -> Option<ClockSample> {
        *self.clock.lock().ok()?
    }

    /// True while the socket to Nota is up (used to report editor visibility on reconnect).
    pub fn connected(&self) -> bool {
        self.writer.lock().map(|w| w.is_some()).unwrap_or(false)
    }

    /// Sends one JSON line to NotA (viz replies). Ignored while disconnected.
    pub fn send(&self, line: &str) {
        if let Ok(mut guard) = self.writer.lock()
            && let Some(stream) = guard.as_mut()
        {
            let _ = stream.write_all(line.as_bytes());
            let _ = stream.write_all(b"\n");
        }
    }

    /// Commands received since the last frame.
    pub fn poll(&self) -> Vec<Command> {
        let mut out = Vec::new();
        while let Ok(cmd) = self.rx.try_recv() {
            out.push(cmd);
        }
        out
    }
}

fn run(addr: &str, tx: Sender<Command>, clock: Arc<Mutex<Option<ClockSample>>>, writer: Arc<Mutex<Option<TcpStream>>>) {
    // When NotA started this process, a long disconnect means NotA is gone: exit instead of
    // lingering as an invisible second app. A hand-started engine keeps retrying.
    let exit_on_disconnect = std::env::var_os("SYNTETIKA_EXIT_ON_DISCONNECT").is_some();
    let mut failed_attempts = 0u32;
    // Clock-quality stats: packet rate, implied BPM spread and largest beat jump. A steppy
    // source shows up here before anyone sees a stutter on screen.
    let mut stat_packets = 0u32;
    let mut stat_min_bpm = f64::MAX;
    let mut stat_max_bpm = 0.0f64;
    let mut stat_max_jump = 0.0f64;
    let mut stat_last: Option<(f64, Instant)> = None;
    let mut stat_since = Instant::now();
    loop {
        match TcpStream::connect(addr) {
            Ok(stream) => {
                failed_attempts = 0;
                stream.set_nodelay(true).ok();
                if let Ok(mut w) = writer.lock() {
                    *w = stream.try_clone().ok();
                }
                eprintln!("openVJ: Nota bridge connected ({addr})");
                let reader = BufReader::new(stream);
                for line in reader.lines() {
                    let Ok(line) = line else { break };
                    if line.trim().is_empty() {
                        continue;
                    }
                    if let Some((beat, _)) = parse_clock(&line) {
                        stat_packets += 1;
                        if let Some((prev, at)) = stat_last {
                            let dt = at.elapsed().as_secs_f64();
                            if dt > 1e-6 {
                                let bpm = (beat - prev) / dt * 60.0;
                                if bpm.is_finite() && bpm > 0.0 {
                                    stat_min_bpm = stat_min_bpm.min(bpm);
                                    stat_max_bpm = stat_max_bpm.max(bpm);
                                }
                                stat_max_jump = stat_max_jump.max((beat - prev).abs());
                            }
                        }
                        stat_last = Some((beat, Instant::now()));
                    }
                    handle(&line, &tx, &clock);
                    if stat_since.elapsed().as_secs_f64() >= 1.0 {
                        eprintln!(
                            "openVJ: clock {stat_packets} pkt/s, implied bpm {:.1}..{:.1}, max jump {:.3} beat",
                            if stat_min_bpm == f64::MAX { 0.0 } else { stat_min_bpm },
                            stat_max_bpm, stat_max_jump
                        );
                        stat_packets = 0;
                        stat_min_bpm = f64::MAX;
                        stat_max_bpm = 0.0;
                        stat_max_jump = 0.0;
                        stat_since = Instant::now();
                    }
                }
                if let Ok(mut w) = writer.lock() {
                    *w = None;
                }
                eprintln!("openVJ: Nota bridge disconnected; waiting for Nota");
            }
            Err(_) => { /* Nota isn't up yet; retry quietly. */ }
        }
        if exit_on_disconnect {
            failed_attempts += 1;
            if failed_attempts >= 3 {
                eprintln!("openVJ: Nota is gone; exiting the visual engine");
                std::process::exit(0);
            }
        }
        std::thread::sleep(Duration::from_millis(1000));
    }
}

fn parse_clock(line: &str) -> Option<(f64, f32)> {
    let v: serde_json::Value = serde_json::from_str(line).ok()?;
    if v.get("t").and_then(|t| t.as_str()) != Some("clock") {
        return None;
    }
    let beat = v.get("beat").and_then(|x| x.as_f64())?;
    let bpm = v.get("bpm").and_then(|x| x.as_f64()).unwrap_or(120.0) as f32;
    Some((beat, bpm))
}

fn handle(line: &str, tx: &Sender<Command>, clock: &Arc<Mutex<Option<ClockSample>>>) {
    let Ok(v) = serde_json::from_str::<serde_json::Value>(line) else { return };
    match v.get("t").and_then(|t| t.as_str()) {
        Some("clock") => {
            let Some(beat) = v.get("beat").and_then(|x| x.as_f64()) else { return };
            let bpm = v.get("bpm").and_then(|x| x.as_f64()).unwrap_or(120.0) as f32;
            let playing = v.get("playing").and_then(|x| x.as_bool()).unwrap_or(true);
            if let Ok(mut slot) = clock.lock() {
                *slot = Some(ClockSample { beat, bpm, playing, at: Instant::now() });
            }
        }
        Some("scene") => {
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let at_beat = v.get("atBeat").and_then(|x| x.as_f64()).unwrap_or(0.0);
            let _ = tx.send(Command::Scene { col, at_beat });
        }
        Some("slot") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let at_beat = v.get("atBeat").and_then(|x| x.as_f64()).unwrap_or(0.0);
            let _ = tx.send(Command::Slot { layer, col, at_beat });
        }
        Some("stopLayer") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let at_beat = v.get("atBeat").and_then(|x| x.as_f64());
            let _ = tx.send(Command::StopLayer { layer, at_beat });
        }
        Some("stopAll") => {
            let at_beat = v.get("atBeat").and_then(|x| x.as_f64());
            let _ = tx.send(Command::StopAll { at_beat });
        }
        Some("select") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let _ = tx.send(Command::Select { layer, col });
        }
        Some("vizGet") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let seq = v.get("seq").and_then(|x| x.as_u64()).unwrap_or(0);
            let _ = tx.send(Command::VizGet { layer, col, seq });
        }
        Some("vizSet") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let group = v.get("group").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let key = v.get("key").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let value = v.get("value").and_then(|x| x.as_f64()).unwrap_or(0.0) as f32;
            let _ = tx.send(Command::VizSet { layer, col, group, key, value });
        }
        Some("vizPlayback") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let speed = v.get("speed").and_then(|x| x.as_f64()).map(|s| s as f32);
            let sync = v.get("syncBeats").map(|s| s.as_f64().map(|b| b as f32));
            let play_mode = v.get("playMode").and_then(|x| x.as_str()).map(str::to_owned);
            let trigger = v.get("trigger").and_then(|x| x.as_str()).map(str::to_owned);
            let _ = tx.send(Command::VizPlayback { layer, col, speed, sync, play_mode, trigger });
        }
        Some("vizFx") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let op = v.get("op").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let a = v.get("index").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let b = v.get("to").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let id = v.get("id").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let value = v.get("value").and_then(|x| x.as_f64()).unwrap_or(0.0) as f32;
            let _ = tx.send(Command::VizFx { layer, col, op, a, b, id, value });
        }
        Some("vizAssign") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let kind = v.get("kind").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let id = v.get("id").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let path = v.get("path").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let app = v.get("app").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let server = v.get("server").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let _ = tx.send(Command::VizAssign { layer, col, kind, id, path, app, server });
        }
        Some("vizClear") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let _ = tx.send(Command::VizClear { layer, col });
        }
        Some("vizSave") => {
            let path = v.get("path").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let uids = v
                .get("uids")
                .and_then(|x| x.as_array())
                .map(|a| a.iter().filter_map(|u| u.as_str().map(str::to_owned)).collect())
                .unwrap_or_default();
            let media_dirs = v
                .get("mediaDirs")
                .and_then(|x| x.as_array())
                .map(|a| a.iter().filter_map(|u| u.as_str().map(str::to_owned)).collect())
                .unwrap_or_default();
            let _ = tx.send(Command::VizSave { path, uids, media_dirs });
        }
        Some("vizLoad") => {
            let path = v.get("path").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let media_dir = v.get("mediaDir").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let _ = tx.send(Command::VizLoad { path, media_dir });
        }
        Some("vizLayer") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let opacity = v.get("opacity").and_then(|x| x.as_f64()).map(|x| x as f32);
            let blend = v.get("blend").and_then(|x| x.as_str()).map(str::to_owned);
            let bypass = v.get("bypass").and_then(|x| x.as_bool());
            let _ = tx.send(Command::VizLayer { layer, opacity, blend, bypass });
        }
        Some("vizOutput") => {
            // Missing field means "show": a bare request should not hide the window.
            let show = v.get("show").and_then(|x| x.as_bool()).unwrap_or(true);
            let _ = tx.send(Command::VizOutput { show });
        }
        Some("vizUi") => {
            let show = v.get("show").and_then(|x| x.as_bool()).unwrap_or(true);
            let _ = tx.send(Command::VizUi { show });
        }
        Some("vizMapping") => {
            let show = v.get("show").and_then(|x| x.as_bool()).unwrap_or(true);
            let _ = tx.send(Command::VizMapping { show });
        }
        Some("vizSlotTiming") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let beats = v.get("beats").and_then(|x| x.as_f64()).unwrap_or(0.0) as f32;
            let bpm = v.get("bpm").and_then(|x| x.as_f64()).filter(|b| *b > 0.0).unwrap_or(120.0) as f32;
            let looped = v.get("loop").and_then(|x| x.as_bool()).unwrap_or(true);
            let start = v.get("start").and_then(|x| x.as_f64()).unwrap_or(0.0) as f32;
            let _ = tx.send(Command::VizSlotTiming { layer, col, beats, bpm, looped, start });
        }
        Some("vizSlotPhase") => {
            let layer = v.get("layer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let col = v.get("col").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let phase = v.get("phase").and_then(|x| x.as_f64()).unwrap_or(0.0) as f32;
            let sample_beat = v.get("beat").and_then(|x| x.as_f64());
            let _ = tx.send(Command::VizSlotPhase { layer, col, phase, sample_beat });
        }
        Some("vizCopy") => {
            let from_layer = v.get("fromLayer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let from_col = v.get("fromCol").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let to_layer = v.get("toLayer").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let to_col = v.get("toCol").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
            let moving = v.get("move").and_then(|x| x.as_bool()).unwrap_or(false);
            let _ = tx.send(Command::VizCopy { from_layer, from_col, to_layer, to_col, moving });
        }
        Some("vizCatalog") => {
            let kind = v.get("kind").and_then(|x| x.as_str()).unwrap_or("generators").to_owned();
            let query = v.get("query").and_then(|x| x.as_str()).unwrap_or("").to_owned();
            let seq = v.get("seq").and_then(|x| x.as_u64()).unwrap_or(0);
            let _ = tx.send(Command::VizCatalog { kind, query, seq });
        }
        Some("layout") => {
            let tracks: Vec<LayoutTrack> = v
                .get("tracks")
                .and_then(|x| x.as_array())
                .map(|list| {
                    list.iter()
                        .filter_map(|t| {
                            Some(LayoutTrack {
                                uid: t.get("uid")?.as_str()?.to_owned(),
                                name: t.get("name").and_then(|n| n.as_str()).unwrap_or("").to_owned(),
                                kind: t.get("kind").and_then(|k| k.as_i64()).unwrap_or(0),
                            })
                        })
                        .collect()
                })
                .unwrap_or_default();
            let scenes: Vec<String> = v
                .get("scenes")
                .and_then(|x| x.as_array())
                .map(|list| {
                    list.iter()
                        .map(|s| s.get("name").and_then(|n| n.as_str()).unwrap_or("").to_owned())
                        .collect()
                })
                .unwrap_or_default();
            eprintln!("openVJ: NOTA layout message: {} tracks / {} scenes", tracks.len(), scenes.len());
            let _ = tx.send(Command::Layout { tracks, scenes });
        }
        _ => {}
    }
}
