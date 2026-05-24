use std::{fs, path::Path};

use super::{thumbnail, validate};

fn write_manifest(dir: &Path, body: &str) -> std::path::PathBuf {
    fs::write(dir.join("manifest.json"), body).unwrap();
    fs::write(dir.join("frame-000.png"), tiny_png()).unwrap();
    dir.join("manifest.json")
}

fn tiny_png() -> &'static [u8] {
    &[
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d, 0x49, 0x48,
        0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00,
        0x00, 0x90, 0x77, 0x53, 0xde, 0x00, 0x00, 0x00, 0x0c, 0x49, 0x44, 0x41, 0x54, 0x08,
        0xd7, 0x63, 0xf8, 0xcf, 0xc0, 0x00, 0x00, 0x04, 0x7f, 0x01, 0x7f, 0xa7, 0xf6, 0x81,
        0x8f, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82,
    ]
}

#[test]
fn validate_accepts_manifest_with_existing_screenshot() {
    let dir = tempfile::tempdir().unwrap();
    let manifest = write_manifest(
        dir.path(),
        r#"{
            "id": "us-wsm-phase-1-voxel-actors",
            "intent": "Validate Phase 1.",
            "steps": [
                {
                    "index": 0,
                    "slug": "baseline",
                    "intent": "Baseline.",
                    "screenshot_path": "frame-000.png"
                }
            ]
        }"#,
    );

    assert!(validate::run(&manifest, false).is_ok());
}

#[test]
fn validate_rejects_missing_screenshot_in_strict_mode() {
    let dir = tempfile::tempdir().unwrap();
    let manifest = write_manifest(
        dir.path(),
        r#"{
            "id": "us-wsm-phase-1-voxel-actors",
            "intent": "Validate Phase 1.",
            "steps": [
                {
                    "index": 0,
                    "slug": "baseline",
                    "intent": "Baseline.",
                    "screenshot_path": "missing.png"
                }
            ]
        }"#,
    );

    assert!(validate::run(&manifest, true).is_err());
}

#[test]
fn validate_allows_placeholder_manifest_without_assets_in_offline_mode() {
    let dir = tempfile::tempdir().unwrap();
    let manifest = write_manifest(
        dir.path(),
        r#"{
            "id": "us-wsm-phase-1-voxel-actors",
            "intent": "Validate Phase 1.",
            "recording_gif": null,
            "steps": [
                {
                    "index": 0,
                    "slug": "baseline",
                    "intent": "Baseline.",
                    "screenshot_path": "missing.png"
                }
            ]
        }"#,
    );

    assert!(validate::run(&manifest, false).is_ok());
    assert!(validate::run(&manifest, true).is_err());
}

#[test]
fn validate_rejects_missing_embedded_docs_path() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("WorldSphereMod.sln"), "").unwrap();
    let manifest = write_manifest(
        dir.path(),
        r#"{
            "id": "us-wsm-phase-1-voxel-actors",
            "intent": "See docs/journeys/scratch/does-not-exist.md for details.",
            "steps": [
                {
                    "index": 0,
                    "slug": "baseline",
                    "intent": "Baseline.",
                    "screenshot_path": "frame-000.png"
                }
            ]
        }"#,
    );

    assert!(validate::run(&manifest, false).is_err());
}

#[test]
fn thumbnail_copies_image_inputs() {
    let dir = tempfile::tempdir().unwrap();
    let input = dir.path().join("frame.png");
    let output = dir.path().join("thumb.png");
    fs::write(&input, tiny_png()).unwrap();

    thumbnail::run(&input, &output).unwrap();

    assert_eq!(fs::read(&input).unwrap(), fs::read(&output).unwrap());
}
