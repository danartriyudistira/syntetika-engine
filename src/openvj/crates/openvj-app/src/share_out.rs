//! Frame sharing to other applications: Syphon on macOS, Spout on Windows.
//!
//! Both backends expose the same small surface to `App`: `before_render` keeps the engine's
//! shared BGRA copy alive, `after_render` publishes the finished frame, and the settings
//! switch drives `set_enabled`. Where neither is compiled in, the inert version below keeps
//! the call sites identical.

#[cfg(all(target_os = "macos", feature = "syphon"))]
mod imp {
    pub use crate::syphon_out::{AVAILABLE, SyphonOutput as Output};
    pub const KIND: &str = "Syphon";
}

#[cfg(all(target_os = "windows", feature = "spout"))]
mod imp {
    pub use crate::spout_out::{AVAILABLE, SpoutOutput as Output};
    pub const KIND: &str = "Spout";
}

#[cfg(not(any(
    all(target_os = "macos", feature = "syphon"),
    all(target_os = "windows", feature = "spout"),
)))]
mod imp {
    use openvj_render::Engine;

    pub const AVAILABLE: bool = false;
    pub const KIND: &str = "Frame sharing";

    pub struct Output;

    impl Output {
        pub fn new(_device: &wgpu::Device, _queue: &wgpu::Queue) -> Self {
            Self
        }
        pub fn is_enabled(&self) -> bool {
            false
        }
        pub fn set_enabled(&mut self, _on: bool) {}
        pub fn is_receiving(&self) -> bool {
            false
        }
        pub fn error(&self) -> Option<&str> {
            None
        }
        pub fn before_render(&mut self, _engine: &mut Engine) {}
        pub fn after_render(&self, _engine: &Engine) {}
    }
}

pub use imp::{AVAILABLE, KIND, Output as ShareOutput};
