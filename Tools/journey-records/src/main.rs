#![deny(warnings)]

use std::path::PathBuf;

use clap::{Parser, Subcommand};

mod thumbnail;
mod validate;

#[derive(Parser)]
#[command(name = "journey-records", version, about = "Journey-record tooling")]
struct Cli {
    #[command(subcommand)]
    command: Commands,
}

#[derive(Subcommand)]
enum Commands {
    /// Validate a manifest using the JSON schema.
    Validate { manifest: PathBuf },
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
        Commands::Validate { manifest } => validate::run(&manifest),
        Commands::Thumbnail { video, out } => thumbnail::run(&video, &out),
    }
}

