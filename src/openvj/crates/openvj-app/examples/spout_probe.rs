//! Receives the Spout sender for a few seconds and prints what came through.
//!
//! Run it while the visual engine is publishing (`OPENVJ_SPOUT=1`):
//!   cargo run --release -p openvj-app --example spout_probe --features spout

#[cfg(all(target_os = "windows", feature = "spout"))]
fn main() -> spout2::Result<()> {
    use std::time::{Duration, Instant};

    let receiver = spout2::dx::Receiver::new(None)?;
    println!("senders: {:?}", receiver.sender_list());
    if !receiver.sender_list().iter().any(|n| n.contains("Syntetika")) {
        println!("NO SENDER");
        return Ok(());
    }
    let mut receiver = spout2::dx::Receiver::new(Some("Syntetika Engine"))?;
    let (mut w, mut h) = (256u32, 256u32);
    let mut pixels = vec![0u8; (w * h * 4) as usize];
    let started = Instant::now();
    let mut frames = 0u32;
    let mut checksum = 0u64;
    while started.elapsed() < Duration::from_secs(4) {
        let connected = receiver.receive_image(&mut pixels, w, h, false, false)?;
        if receiver.is_updated() {
            (w, h) = receiver.sender_size();
            pixels.resize((w * h * 4) as usize, 0);
            continue;
        }
        if connected && receiver.is_frame_new() {
            frames += 1;
            checksum = pixels.iter().step_by(997).map(|b| *b as u64).sum();
        }
        std::thread::sleep(Duration::from_millis(16));
    }
    println!(
        "name={} size={}x{} frames={} checksum={} fps={:.1}",
        receiver.sender_name(),
        w,
        h,
        frames,
        checksum,
        receiver.sender_fps()
    );
    if frames == 0 {
        println!("NO FRAMES");
    }
    Ok(())
}

#[cfg(not(all(target_os = "windows", feature = "spout")))]
fn main() {
    println!("spout probe: needs Windows with the `spout` feature");
}
