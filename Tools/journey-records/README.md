# journey-records

This crate provides a lightweight Rust CLI for working with journey records, including schema validation and a thumbnail workflow for image pass-through or ffmpeg-based extraction.

Build and run it via `cargo run -- validate <manifest>` for offline manifest checks and `cargo run -- validate --strict-assets <manifest>` when you want screenshot and optional asset existence checks enforced. Use `cargo run -- thumbnail <video> --out <png>` for thumbnail invocation. Use the copied `tools/journey-records/schema.json` as the manifest schema source when integrating validation against recorded journey files.

Offline validation checks the copied JSON Schema plus journey-specific structure rules: step indices must stay contiguous, and `recording_gif: null` is accepted as a placeholder. Strict asset validation additionally requires each step screenshot to exist and resolves optional `recording` / `recording_gif` assets when present.
