use std::{
    collections::BTreeSet,
    fs,
    path::{Path, PathBuf},
};

use anyhow::{anyhow, Context};
use jsonschema::validator_for;
use serde_json::Value;

pub fn run(manifest: &Path, strict_assets: bool) -> anyhow::Result<()> {
    let manifest_text = fs::read_to_string(manifest)
        .with_context(|| format!("failed to read manifest {}", manifest.display()))?;
    let manifest_json: Value = serde_json::from_str(&manifest_text)
        .with_context(|| format!("failed to parse JSON in {}", manifest.display()))?;

    let schema_path = schema_path()?;
    let schema_text = fs::read_to_string(&schema_path)
        .with_context(|| format!("failed to read schema {}", schema_path.display()))?;
    let schema_json: Value = serde_json::from_str(&schema_text)
        .with_context(|| format!("failed to parse JSON in {}", schema_path.display()))?;
    let schema = validator_for(&schema_json).context("failed to compile journey schema")?;

    let mut issues = Vec::new();
    if let Err(errors) = schema.validate(&manifest_json) {
        for error in errors {
            issues.push(error.to_string());
        }
    }

    issues.extend(validate_journey_assets(
        &manifest_json,
        manifest.parent(),
        strict_assets,
    )?);

    if !issues.is_empty() {
        return Err(anyhow!("manifest validation failed:\n- {}", issues.join("\n- ")));
    }

    if strict_assets {
        println!("ok: manifest matches schema and strict asset checks passed");
    } else {
        println!("ok: manifest matches schema (offline validation)");
    }
    Ok(())
}

fn schema_path() -> anyhow::Result<PathBuf> {
    let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    Ok(manifest_dir.join("schema.json"))
}

fn validate_journey_assets(
    manifest: &Value,
    manifest_dir: Option<&Path>,
    strict_assets: bool,
) -> anyhow::Result<Vec<String>> {
    let mut issues = Vec::new();

    let Some(manifest_dir) = manifest_dir else {
        if strict_assets {
            return Ok(vec!["manifest has no parent directory".to_string()]);
        }
        return Ok(issues);
    };

    let Some(steps) = manifest.get("steps").and_then(Value::as_array) else {
        return Ok(issues);
    };

    let mut seen_indices = BTreeSet::new();
    for (expected_index, step) in steps.iter().enumerate() {
        let Some(step_obj) = step.as_object() else {
            issues.push(format!("step {expected_index} is not an object"));
            continue;
        };

        match step_obj.get("index").and_then(Value::as_i64) {
            Some(index) if index == expected_index as i64 => {
                seen_indices.insert(index);
            }
            Some(index) => issues.push(format!(
                "step {} has index {}, expected {}",
                expected_index, index, expected_index
            )),
            None => issues.push(format!("step {} is missing an integer index", expected_index)),
        }

        match step_obj.get("screenshot_path").and_then(Value::as_str) {
            Some(path) if !path.trim().is_empty() => {
                if strict_assets {
                    let resolved = manifest_dir.join(path);
                    if !resolved.is_file() {
                        issues.push(format!(
                            "step {} screenshot_path {} does not exist",
                            expected_index,
                            resolved.display()
                        ));
                    }
                }
            }
            _ if strict_assets => issues.push(format!(
                "step {} has an empty screenshot_path",
                expected_index
            )),
            _ => {}
        }
    }

    if seen_indices.len() != steps.len() {
        issues.push("step indices must be unique and contiguous from 0".to_string());
    }

    if let Some(recording) = manifest.get("recording").and_then(Value::as_str) {
        if strict_assets && !recording.trim().is_empty() {
            validate_optional_asset(manifest_dir, "recording", recording, &mut issues)?;
        }
    }

    if let Some(recording_gif) = manifest.get("recording_gif").and_then(Value::as_str) {
        if strict_assets && !recording_gif.trim().is_empty() {
            validate_optional_asset(manifest_dir, "recording_gif", recording_gif, &mut issues)?;
        }
    }

    Ok(issues)
}

fn validate_optional_asset(
    manifest_dir: &Path,
    field: &str,
    value: &str,
    issues: &mut Vec<String>,
) -> anyhow::Result<()> {
    let resolved = manifest_dir.join(value);
    if !resolved.is_file() {
        issues.push(format!("{field} path {} does not exist", resolved.display()));
    }
    Ok(())
}
