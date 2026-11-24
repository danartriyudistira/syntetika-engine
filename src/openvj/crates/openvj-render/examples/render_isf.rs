//! Renders one ISF generator to a PNG for visual inspection.
//!
//! `cargo run --release -p openvj-render --example render_isf -- <folder> <out.png> [seconds]`

use std::env;

use openvj_core::effect_folders::Scanner;
use openvj_core::{ClipSource, Composition, Effect, ModInputs};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut args = env::args().skip(1);
    let folder = args.next().ok_or("usage: render_isf <folder> <out.png> [seconds]")?;
    let out = args.next().ok_or("usage: render_isf <folder> <out.png> [seconds]")?;
    let seconds: f64 = args.next().map(|s| s.parse()).transpose()?.unwrap_or(0.6);

    let mut scanner = Scanner::new(vec![folder.into()], openvj_render::check_effect);
    let result = scanner.scan().ok_or("no shader found")?;
    let generator = result.generators.first().ok_or("no generator found")?.clone();
    let id = generator.id().to_owned();
    openvj_core::set_library(result.into_library());
    eprintln!("rendering {id} at {seconds}s");

    let (w, h) = (1280, 720);
    let mut comp = Composition::new(w, h, 1, 1);
    comp.name = id.clone();
    let effect = Effect::new(&id);
    comp.set_clip(0, 0, ClipSource::Isf { generator: effect }).ok_or("could not set clip")?;
    comp.trigger(0, 0, 0.0);

    let instance = wgpu::Instance::new(wgpu::InstanceDescriptor::new_without_display_handle_from_env());
    let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions::default()))?;
    let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
        required_features: adapter.features() & wgpu::Features::TEXTURE_COMPRESSION_BC,
        required_limits: wgpu::Limits::downlevel_webgl2_defaults().using_resolution(adapter.limits()),
        ..Default::default()
    }))?;
    let mut engine = openvj_render::Engine::new(device, queue, w, h);
    let inputs = ModInputs { beat: seconds * 105.0 / 60.0, bands: [0.6, 0.45, 0.4, 0.35], ..Default::default() };
    for i in 0..3 {
        engine.render(&comp, seconds + i as f64 * 0.016, &inputs, None);
    }
    let pixels = engine.read_target(engine.output());
    image::save_buffer(&out, &pixels, w, h, image::ColorType::Rgba8)?;
    println!("wrote {out}");
    Ok(())
}
