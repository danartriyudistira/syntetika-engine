//! Nota's master-output tap.
//!
//! When Nota hosts openVJ it publishes the sound it is performing into a file-backed ring
//! buffer (see Nota's `src/engine/AudioTap.h` for the layout). Reading it here lets every
//! audio-reactive parameter follow the DAW's master — including tracks, plugins and the
//! master chain — instead of openVJ's own input device.

use std::fs::File;
use std::os::unix::io::AsRawFd;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU32, AtomicU64, Ordering};
use std::time::{Duration, Instant};

const MAGIC: u32 = 0x3141_544E; // "NTA1"
const VERSION: u32 = 1;
const CHANNELS: usize = 2;
const RATE_OFFSET: usize = 16;
const INDEX_OFFSET: usize = 24;
const DATA_OFFSET: usize = 64;
const RETRY: Duration = Duration::from_secs(1);

struct Mapping {
    base: *mut u8,
    len: usize,
}

impl Drop for Mapping {
    fn drop(&mut self) {
        unsafe { libc::munmap(self.base as *mut libc::c_void, self.len) };
    }
}

pub struct NotaTap {
    path: PathBuf,
    map: Option<Mapping>,
    last_total: u64,
    rate: u32,
    next_try: Instant,
}

impl NotaTap {
    pub fn new(path: impl Into<PathBuf>) -> Self {
        Self { path: path.into(), map: None, last_total: 0, rate: 0, next_try: Instant::now() }
    }

    /// Drains the frames written since the last call into `out` (mono, -1..1). Returns the
    /// tap's sample rate while it is readable; `None` before Nota has opened it.
    pub fn drain(&mut self, out: &mut Vec<f32>) -> Option<u32> {
        if self.map.is_none() {
            if Instant::now() < self.next_try {
                return None;
            }
            if !self.try_map() {
                self.next_try = Instant::now() + RETRY;
                return None;
            }
        }
        let map = self.map.as_ref()?;
        let base = map.base;
        let magic = read_u32(base, 0);
        let version = read_u32(base, 4);
        let channels = read_u32(base, 8) as usize;
        let frames = read_u32(base, 12) as usize;
        if magic != MAGIC || version != VERSION || channels != CHANNELS || !frames.is_power_of_two() || frames == 0 {
            // Nota is (re)creating the file: the header returns in a frame or two.
            return None;
        }
        if DATA_OFFSET + frames * channels * 4 > map.len {
            return None;
        }
        let rate = f32::from_bits(load_u32(base, RATE_OFFSET)).max(1.0) as u32;
        self.rate = rate;
        let total = load_u64(base, INDEX_OFFSET);
        if total < self.last_total {
            self.last_total = total; // the audio thread restarted at zero
            return Some(rate);
        }
        let unread = total - self.last_total;
        if unread == 0 {
            return Some(rate);
        }
        let start = if unread > frames as u64 { total - frames as u64 } else { self.last_total };
        let count = (total - start) as usize;
        let first = start as usize % frames;
        let head = (frames - first).min(count);
        let ring = unsafe { std::slice::from_raw_parts((base as *const f32).add(DATA_OFFSET / 4), frames * channels) };
        out.reserve(count);
        for part in [(first, head), (0, count - head)] {
            for frame in ring[part.0 * channels..(part.0 + part.1) * channels].chunks_exact(channels) {
                out.push(frame.iter().sum::<f32>() / channels as f32);
            }
        }
        self.last_total = total;
        Some(rate)
    }

    fn try_map(&mut self) -> bool {
        let Ok(file) = File::open(&self.path) else { return false };
        let Ok(meta) = file.metadata() else { return false };
        let len = meta.len() as usize;
        if len < DATA_OFFSET + CHANNELS * 4 {
            return false;
        }
        let base =
            unsafe { libc::mmap(std::ptr::null_mut(), len, libc::PROT_READ, libc::MAP_SHARED, file.as_raw_fd(), 0) };
        if base == libc::MAP_FAILED {
            return false;
        }
        self.map = Some(Mapping { base: base as *mut u8, len });
        self.last_total = 0;
        true
    }
}

fn read_u32(base: *const u8, offset: usize) -> u32 {
    unsafe { (base.add(offset) as *const u32).read_unaligned() }
}

fn load_u32(base: *const u8, offset: usize) -> u32 {
    unsafe { (*(base.add(offset) as *const AtomicU32)).load(Ordering::Acquire) }
}

fn load_u64(base: *const u8, offset: usize) -> u64 {
    unsafe { (*(base.add(offset) as *const AtomicU64)).load(Ordering::Acquire) }
}
