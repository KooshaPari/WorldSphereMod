use std::{
    ffi::OsStr,
    fs,
    path::{Path, PathBuf},
    process::Command,
};

use anyhow::{anyhow, bail, Context};

pub fn run(video: &Path, out: &Path) -> anyhow::Result<()> {
    let input = fs::canonicalize(video)
        .with_context(|| format!("failed to resolve input {}", video.display()))?;
    let output = out.to_path_buf();

    if is_image(&input) {
        fs::copy(&input, &output).with_context(|| {
            format!(
                "failed to copy image thumbnail from {} to {}",
                input.display(),
                output.display()
            )
        })?;
        println!("ok: copied image thumbnail to {}", output.display());
        return Ok(());
    }

    let ffmpeg = find_ffmpeg()?;
    let status = Command::new(ffmpeg)
        .args([
            "-y",
            "-i",
            input.to_string_lossy().as_ref(),
            "-vf",
            "thumbnail,scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-frames:v",
            "1",
            output.to_string_lossy().as_ref(),
        ])
        .status()
        .context("failed to run ffmpeg")?;

    if !status.success() {
        bail!("ffmpeg failed to extract a thumbnail from {}", input.display());
    }

    println!("ok: wrote thumbnail to {}", output.display());
    Ok(())
}

fn is_image(path: &Path) -> bool {
    matches!(
        path.extension().and_then(OsStr::to_str).map(|ext| ext.to_ascii_lowercase()),
        Some(ext) if matches!(ext.as_str(), "png" | "jpg" | "jpeg" | "webp" | "bmp" | "gif")
    )
}

fn find_ffmpeg() -> anyhow::Result<PathBuf> {
    let candidate = if cfg!(windows) { "ffmpeg.exe" } else { "ffmpeg" };
    which::which(candidate).map_err(|_| anyhow!("ffmpeg was not found on PATH"))
}
