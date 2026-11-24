//! Renders one column of a project's saved visual composition to a PNG.
//!
//! `cargo run --release -p openvj-render --example render_column -- <project.nota> <col> <out.png>`

use std::env;
use std::fs;
use std::path::Path;

use openvj_core::effect_folders::Scanner;
use openvj_core::{Composition, ModInputs};
use serde_json::Value;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut args = env::args().skip(1);
    let project = args.next().ok_or("usage: render_column <project.nota> <col> <out.png>")?;
    let col: usize = args.next().ok_or("missing column")?.parse()?;
    let out = args.next().ok_or("missing output path")?;

    let shaders = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../shaders");
    let mut scanner = Scanner::new(vec![shaders], openvj_render::check_effect);
    if let Some(result) = scanner.scan() {
        openvj_core::set_library(result.into_library());
    }

    let doc: Value =
        serde_json::from_str(&fs::read_to_string(Path::new(&project).join("visuals/composition.json"))?)?;
    let mut comp: Composition = serde_json::from_value(doc["composition"].clone())?;
    let (w, h) = (comp.width, comp.height);

    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle_from_env());
    let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions::default()))?;
    let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        required_features: adapter.features() & wgpu::Features::TEXTURE_COMPRESSION_BC,
        required_limits: wgpu::Limits::downlevel_webgl2_defaults().using_resolution(adapter.limits()),
        ..Default::default()
    }))?;
    let mut engine = openvj_render::Engine::new(device, queue, w, h);
    for layer in 0..comp.layers.len() {
        if comp.clip(layer, col).is_some() {
            comp.trigger(layer, col, 0.0);
        }
    }
    let inputs = ModInputs { beat: 4.0, bands: [0.6, 0.45, 0.4, 0.35], ..Default::default() };
    for i in 0..3 {
        engine.render(&comp, 1.0 + i as f64 * 0.016, &inputs, None);
    }
    let pixels = engine.read_target(engine.output());
    image::save_buffer(&out, &pixels, w, h, image::ColorType::Rgba8)?;
    println!("wrote {out} ({w}x{h}, column {col})");
    Ok(())
}
