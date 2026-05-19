# journey-records

This crate provides a lightweight Rust CLI scaffold for working with journey records, including a schema validation command and a thumbnail workflow entrypoint intended for future video-to-image processing.

Build and run it via `cargo run -- validate <manifest>` for manifest checks and `cargo run -- thumbnail <video> --out <png>` for thumbnail invocation. Use the copied `tools/journey-records/schema.json` as the manifest schema source when integrating validation against recorded journey files.

Roadmap: replace the validation stub with JSON Schema enforcement, implement real thumbnail generation with ffmpeg or a preferred frame-extractor, and add integration tests plus CI targets so this tool can replace current journey-manifest tooling in release pipelines.
