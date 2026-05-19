use std::fmt::{self, Display};
use std::fs::File;
use std::io::{self, BufRead, BufReader, Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::thread;
use std::time::Duration;

use anyhow::{Context, Result};
use clap::{Parser, Subcommand};
use regex::Regex;
use serde::Serialize;

#[derive(Parser)]
#[command(name = "wsm-log-parser")]
#[command(version = "0.1.0")]
#[command(about = "Parse [WSM3D] events from Player.log")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    /// Extract and sort [WSM3D] InitProfiler entries by duration.
    InitProfile {
        /// Player.log path
        log: PathBuf,
    },
    /// Extract [WSM3D] material/phase tagging lines.
    PhaseToggles {
        /// Player.log path
        log: PathBuf,
    },
    /// Stream [WSM3D] lines from a log with optional regex filter.
    Tail {
        /// Player.log path; streams stdin when omitted
        log: Option<PathBuf>,
        /// Additional regex pattern to filter matched lines
        #[arg(long)]
        filter: Option<String>,
    },
}

#[derive(Debug, Serialize)]
struct InitProfileEntry {
    label: String,
    #[serde(rename = "duration_ms")]
    duration_ms: f64,
}

#[derive(Debug)]
struct DurationParseError {
    line: String,
    reason: String,
}

impl Display for DurationParseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{} (line: {})", self.reason, self.line)
    }
}

fn main() -> Result<()> {
    let cli = Cli::parse();

    match cli.command {
        Command::InitProfile { log } => command_init_profile(&log)?,
        Command::PhaseToggles { log } => command_phase_toggles(&log)?,
        Command::Tail { log, filter } => {
            let filter_re = match filter {
                Some(pattern) => Some(Regex::new(&pattern)?),
                None => None,
            };
            if let Some(log_file) = log {
                command_tail_file(&log_file, filter_re)?
            } else {
                command_tail_stdin(filter_re)?
            }
        }
    }

    Ok(())
}

fn command_init_profile(log: &Path) -> Result<()> {
    let re = Regex::new(
        r"(?m)^\[WSM3D\]\s*InitProfiler\s+(?P<label>.+?)\s*=\s*(?P<value>-?\d+(?:\.\d+)?)(?P<unit>ms|s|us|ns|μs)?\s*$",
    )?;
    let mut entries: Vec<InitProfileEntry> = Vec::new();

    process_file_lines(log, |raw| {
        if let Some(caps) = re.captures(&raw) {
            let label = caps
                .name("label")
                .map(|m| m.as_str().trim().to_string())
                .ok_or_else(|| {
                    anyhow::anyhow!(DurationParseError {
                        line: raw.clone(),
                        reason: "Missing profiler label".to_string(),
                    })
                })?;
            let value = parse_duration_to_ms(&caps)?;
            entries.push(InitProfileEntry {
                label,
                duration_ms: value,
            });
        }
        Ok(())
    })?;

    entries.sort_by(|a, b| b.duration_ms.partial_cmp(&a.duration_ms).unwrap_or(std::cmp::Ordering::Equal));

    if entries.is_empty() {
        println!("No [WSM3D] InitProfiler lines found in {}", log.display());
        return Ok(());
    }

    let name_width = entries
        .iter()
        .map(|entry| entry.label.len())
        .max()
        .unwrap_or(6)
        .max("Label".len());

    println!(
        "{:<name_width$} {:>14}",
        "Label",
        "Duration (ms)",
        name_width = name_width
    );
    println!("{:-<1$} {:->14}", "", name_width + 15);
    for entry in &entries {
        println!(
            "{:<name_width$} {:>14.3}",
            entry.label,
            entry.duration_ms,
            name_width = name_width
        );
    }

    Ok(())
}

fn parse_duration_to_ms(caps: &regex::Captures<'_>) -> Result<f64> {
    let value = caps
        .name("value")
        .ok_or_else(|| anyhow::anyhow!(DurationParseError {
            line: String::from(""),
            reason: "Missing duration value".to_string(),
        }))?
        .as_str()
        .parse::<f64>()?;

    let duration_ms = match caps.name("unit").map(|m| m.as_str()) {
        Some("s") => value * 1000.0,
        Some("us") | Some("μs") => value / 1000.0,
        Some("ns") => value / 1_000_000.0,
        _ => value,
    };

    Ok(duration_ms)
}

fn command_phase_toggles(log: &Path) -> Result<()> {
    let mut voxel_material = Vec::new();
    let mut phase_tagging = Vec::new();

    process_file_lines(log, |line| {
        if line.contains("[WSM3D]") && line.contains("Voxel material resolved") {
            voxel_material.push(line);
        }
        if line.contains("[WSM3D]") && line.contains("Phase tagging output") {
            phase_tagging.push(line);
        }
        Ok(())
    })?;

    print_matches("Voxel material resolved", &voxel_material);
    print_matches("Phase tagging output", &phase_tagging);

    if voxel_material.is_empty() && phase_tagging.is_empty() {
        println!("No matching [WSM3D] phase-toggle lines found in {}", log.display());
    }

    Ok(())
}

fn print_matches(title: &str, lines: &[String]) {
    println!("\n== {} ==\n", title);
    println!("{} lines", lines.len());
    for line in lines {
        println!("{line}");
    }
}

fn command_tail_file(path: &Path, filter_re: Option<Regex>) -> Result<()> {
    let mut file = File::open(path).context("unable to open log file")?;
    file.seek(SeekFrom::End(0))?;
    let mut reader = BufReader::new(file);
    let mut line = String::new();

    println!("Following {}", path.display());
    loop {
        let bytes = read_next_line(&mut reader, &mut line)?;
        if bytes == 0 {
            thread::sleep(Duration::from_millis(250));
            continue;
        }

        if should_print_wsm_line(&line, filter_re.as_ref()) {
            print!("{}", line);
        }
    }
}

fn command_tail_stdin(filter_re: Option<Regex>) -> Result<()> {
    let stdin = io::stdin();
    let mut input = BufReader::new(stdin.lock());
    let mut line = String::new();

    loop {
        let bytes = read_next_line(&mut input, &mut line)?;
        if bytes == 0 {
            break;
        }

        if should_print_wsm_line(&line, filter_re.as_ref()) {
            print!("{}", line);
        }
    }

    Ok(())
}

fn should_print_wsm_line(line: &str, filter: Option<&Regex>) -> bool {
    if !line.contains("[WSM3D]") {
        return false;
    }
    match filter {
        Some(re) => re.is_match(line),
        None => true,
    }
}

fn read_next_line<R: BufRead>(reader: &mut R, buffer: &mut String) -> io::Result<usize> {
    buffer.clear();
    let mut tmp = String::new();
    let bytes = reader.read_line(&mut tmp)?;
    if bytes > 0 {
        *buffer = tmp;
    }
    Ok(bytes)
}

fn process_file_lines(path: &Path, mut on_line: impl FnMut(String) -> Result<()>) -> Result<()> {
    let file = File::open(path).context("unable to open log file")?;
    let reader = BufReader::new(file);

    for line in reader.lines() {
        on_line(line?)?;
    }

    Ok(())
}
