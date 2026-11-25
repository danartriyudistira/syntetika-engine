//! Spout output: publishes the composition to other apps on Windows, as Resolume publishes
//! "Arena - Composition".
//!
//! The engine's shared BGRA target (see `Engine::set_shared_output`) is read back and handed
//! to Spout, which uploads it to its own DirectX 11 shared texture. Spout2's protocol is
//! D3D11 shared textures, while the renderer runs on D3D12, so this first version pays one
//! GPU→CPU copy per frame instead of sharing the texture across APIs. The frame rate of the
//! sender therefore follows the output frame rate, with one readback in the way.

use openvj_render::Engine;

/// True when this build can send Spout (Windows with the `spout` feature).
pub const AVAILABLE: bool = cfg!(all(feature = "spout", target_os = "windows"));

/// The sender name other apps list.
pub const SENDER_NAME: &str = "Syntetika Engine";

/// The composition's Spout sender. Does nothing where Spout isn't available.
pub struct SpoutOutput {
    #[cfg(all(feature = "spout", target_os = "windows"))]
    sender: Option<spout2::dx::Sender>,
    /// The Settings switch. Off by default on Windows: publishing costs a readback per frame.
    enabled: bool,
    /// Why there is no sender, once creating one failed; it isn't retried.
    error: Option<String>,
}

impl SpoutOutput {
    pub fn new(_device: &wgpu::Device, _queue: &wgpu::Queue) -> Self {
        // Off by default (a readback per frame is not free), remembered across launches.
        // OPENVJ_SPOUT forces it on for headless runs and screenshots.
        let enabled = AVAILABLE
            && (std::env::var_os("OPENVJ_SPOUT").is_some() || crate::prefs::flag("spout"));
        Self {
            #[cfg(all(feature = "spout", target_os = "windows"))]
            sender: None,
            enabled,
            error: None,
        }
    }

    pub fn is_enabled(&self) -> bool {
        self.enabled
    }

    /// Turns the sender on or off. Off drops it, so other apps no longer list openVJ.
    pub fn set_enabled(&mut self, on: bool) {
        let on = on && AVAILABLE;
        crate::prefs::set_flag("spout", on);
        self.enabled = on;
        if !self.enabled {
            #[cfg(all(feature = "spout", target_os = "windows"))]
            {
                self.sender = None;
            }
            self.error = None;
        }
    }

    /// True while publishing is on. Spout has no receiver count, so the switch decides —
    /// this also keeps the engine rendering when no window is on screen.
    pub fn is_receiving(&self) -> bool {
        self.enabled
    }

    /// Why Spout output couldn't start, if it couldn't.
    pub fn error(&self) -> Option<&str> {
        self.error.as_deref()
    }

    /// Call before rendering a frame: keeps the engine's BGRA copy alive while sending.
    pub fn before_render(&mut self, engine: &mut Engine) {
        engine.set_shared_output(self.enabled);
    }

    /// Call after rendering a frame: hands the frame to Spout if the sender is on.
    pub fn after_render(&mut self, engine: &Engine) {
        if !self.enabled {
            return;
        }
        #[cfg(all(feature = "spout", target_os = "windows"))]
        {
            let Some(target) = engine.shared_output() else { return };
            let (w, h) = target.size();
            if w == 0 || h == 0 {
                return;
            }
            if self.sender.is_none() {
                match spout2::dx::Sender::new(SENDER_NAME) {
                    Ok(s) => self.sender = Some(s),
                    Err(e) => {
                        self.error = Some(format!("{e}"));
                        self.enabled = false;
                        return;
                    }
                }
            }
            let pixels = engine.read_target(target);
            let Some(sender) = self.sender.as_mut() else { return };
            match sender.send_image(&pixels, w, h) {
                Ok(()) => {}
                Err(e) => {
                    self.error = Some(format!("{e}"));
                    self.enabled = false;
                    self.sender = None;
                }
            }
        }
    }
}
