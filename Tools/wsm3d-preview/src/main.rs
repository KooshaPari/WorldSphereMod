use anyhow::Result;
use clap::{Parser, Subcommand};
use std::path::PathBuf;

mod fixtures;
mod phases;
mod render;
mod voxelize;

#[derive(Parser)]
#[command(name = "wsm3d-preview", version, about = "Generate preview renders for 10 WSM3D phases without WorldBox.")]
struct Cli {
    #[command(subcommand)]
    phase: PhaseCommand,
}

#[derive(Subcommand)]
enum PhaseCommand {
    #[command(name = "voxel-actors")]
    VoxelActors {
        #[arg(value_name = "input", value_parser)]
        input: Option<PathBuf>,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
        #[arg(long, default_value_t = 1)]
        depth: usize,
    },

    #[command(name = "mesh-buildings")]
    MeshBuildings {
        #[arg(value_name = "footprint", value_parser)]
        footprint: Option<PathBuf>,
        #[arg(long, default_value_t = 3)]
        stories: u32,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "crossed-foliage")]
    CrossedFoliage {
        #[arg(value_name = "sprite", value_parser)]
        sprite: Option<PathBuf>,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "mesh-water")]
    MeshWater {
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "shadows")]
    Shadows {
        #[arg(value_name = "input", value_parser)]
        input: Option<PathBuf>,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "skeletal")]
    Skeletal {
        #[arg(value_name = "humanoid", value_parser)]
        input: Option<PathBuf>,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "worldspace-ui")]
    WorldspaceUi {
        #[arg(value_name = "input", value_parser)]
        input: Option<PathBuf>,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "day-night")]
    DayNight {
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "particles")]
    Particles {
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "lod")]
    Lod {
        #[arg(value_name = "input", value_parser)]
        input: Option<PathBuf>,
        #[arg(short, long)]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },

    #[command(name = "all")]
    All {
        #[arg(short, long, default_value = "docs/journeys/phase-previews")]
        out: PathBuf,
        #[arg(long, default_value_t = 384)]
        side: u32,
    },
}

fn main() -> Result<()> {
    let cli = Cli::parse();
    match cli.phase {
        PhaseCommand::VoxelActors { input, out, side, depth } => phases::run_phase1(input, out, side, depth),
        PhaseCommand::MeshBuildings {
            footprint,
            stories,
            out,
            side,
        } => phases::run_phase2(footprint, stories, out, side),
        PhaseCommand::CrossedFoliage { sprite, out, side } => phases::run_phase3(sprite, out, side),
        PhaseCommand::MeshWater { out, side } => phases::run_phase4(out, side),
        PhaseCommand::Shadows { input, out, side } => phases::run_phase5(input, out, side),
        PhaseCommand::Skeletal { input, out, side } => phases::run_phase6(input, out, side),
        PhaseCommand::WorldspaceUi { input, out, side } => phases::run_phase7(input, out, side),
        PhaseCommand::DayNight { out, side } => phases::run_phase8(out, side),
        PhaseCommand::Particles { out, side } => phases::run_phase9(out, side),
        PhaseCommand::Lod { input, out, side } => phases::run_phase10(input, out, side),
        PhaseCommand::All { out, side } => phases::run_all(&out, side),
    }
}

