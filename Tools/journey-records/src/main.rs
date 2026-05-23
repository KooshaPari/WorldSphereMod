#![deny(warnings)]

use std::path::PathBuf;

use clap::{Parser, Subcommand};

mod thumbnail;
mod validate;

#[cfg(test)]
mod tests;

#[derive(Parser)]
#[command(name = "journey-records", version, about = "Journey-record tooling")]
struct Cli {
    #[command(subcommand)]
    command: Commands,
}

#[derive(Subcommand)]
enum Commands {
    /// Validate a manifest using the JSON schema.
    Validate {
        manifest: PathBuf,
        /// Require screenshot and optional asset files to exist on disk.
        #[arg(long)]
        strict_assets: bool,
    },
    /// Generate a thumbnail from a recorded video.
    Thumbnail {
        video: PathBuf,
        #[arg(short, long)]
        out: PathBuf,
    },
}

fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();

    match cli.command {
        Commands::Validate {
            manifest,
            strict_assets,
        } => validate::run(&manifest, strict_assets),
        Commands::Thumbnail { video, out } => thumbnail::run(&video, &out),
    }
}
