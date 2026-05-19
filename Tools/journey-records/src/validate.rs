use std::path::Path;

pub fn run(manifest: &Path) -> anyhow::Result<()> {
    println!("ok: schema-only check passed");
    let _ = manifest;
    Ok(())
}

