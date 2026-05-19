use anyhow::{anyhow, Context, Result};
use image::{imageops::FilterType, Rgba, RgbaImage};
use nalgebra::Vector3;
use rayon::prelude::*;
use std::fs;
use std::path::{Path, PathBuf};

use crate::{fixtures, render, voxelize};

fn ensure_dir(path: &Path) -> Result<()> {
    fs::create_dir_all(path).with_context(|| format!("failed to create dir: {}", path.display()))
}

fn write_image(path: &Path, image: &RgbaImage) -> Result<()> {
    image.save(path).with_context(|| format!("failed to write image: {}", path.display()))
}

fn fallback_image(path: Option<&Path>, fallback: impl Fn() -> RgbaImage) -> RgbaImage {
    match path {
        Some(p) => image::open(p).map(|i| i.to_rgba8()).unwrap_or_else(|_| fallback()),
        None => fallback(),
    }
}

fn resized(input: &RgbaImage, side: u32) -> RgbaImage {
    image::imageops::resize(input, side, side, FilterType::Nearest)
}

fn downsample(input: &RgbaImage) -> RgbaImage {
    image::imageops::resize(input, (input.width() / 2).max(1), (input.height() / 2).max(1), FilterType::Triangle)
}

fn blend_pixel(dst: &mut Rgba<u8>, src: Rgba<u8>) {
    let a = src[3] as f32 / 255.0;
    let inv = 1.0 - a;
    dst.0 = [
        (src[0] as f32 * a + dst[0] as f32 * inv) as u8,
        (src[1] as f32 * a + dst[1] as f32 * inv) as u8,
        (src[2] as f32 * a + dst[2] as f32 * inv) as u8,
        (src[3] as f32 * a + dst[3] as f32 * inv) as u8,
    ];
}

fn draw_rect(image: &mut RgbaImage, x0: i32, y0: i32, x1: i32, y1: i32, color: Rgba<u8>) {
    let x0 = x0.max(0).min(image.width() as i32 - 1);
    let y0 = y0.max(0).min(image.height() as i32 - 1);
    let x1 = x1.max(0).min(image.width() as i32 - 1);
    let y1 = y1.max(0).min(image.height() as i32 - 1);
    for y in y0..=y1 {
        for x in x0..=x1 {
            if x < 0 || y < 0 || x >= image.width() as i32 || y >= image.height() as i32 {
                continue;
            }
            let px = image.get_pixel_mut(x as u32, y as u32);
            blend_pixel(px, color);
        }
    }
}

fn draw_minus_five(image: &mut RgbaImage, x: i32, y: i32, color: Rgba<u8>) {
    const DASH: [&str; 7] = [".....", ".....", "#####", ".....", ".....", ".....", "....."];
    const FIVE: [&str; 7] = ["#####", "#....", "####.", "...##", ".####", "....#", "#####"];
    for (py, row) in DASH.iter().enumerate() {
        for (px, c) in row.bytes().enumerate() {
            if c == b'#' {
                draw_rect(image, x + px as i32 * 4, y + py as i32 * 4, x + px as i32 * 4 + 3, y + py as i32 * 4 + 3, color);
            }
        }
    }
    for (py, row) in FIVE.iter().enumerate() {
        for (px, c) in row.bytes().enumerate() {
            if c == b'#' {
                draw_rect(image, x + (px as i32 + 8) * 4, y + py as i32 * 4, x + (px as i32 + 8) * 4 + 3, y + py as i32 * 4 + 3, color);
            }
        }
    }
}

fn merge_meshes(meshes: &[render::Mesh]) -> render::Mesh {
    let mut out_v = Vec::new();
    let mut out_c = Vec::new();
    let mut out_i = Vec::new();
    let mut base = 0u32;
    for mesh in meshes {
        out_v.extend(mesh.positions.iter().copied());
        out_c.extend(mesh.colors.iter().copied());
        for idx in &mesh.indices {
            out_i.push(*idx + base);
        }
        base += mesh.positions.len() as u32;
    }
    render::Mesh {
        positions: out_v,
        colors: out_c,
        indices: out_i,
    }
}

fn add_cube_batch(positions: &[(Vector3<f32>, f32, Rgba<u8>)]) -> render::Mesh {
    let mut cubes = Vec::new();
    for (pos, size, col) in positions {
        cubes.push(voxelize::build_cube(*pos, *size, *col));
    }
    merge_meshes(&cubes)
}

fn average_color(image: &RgbaImage) -> Rgba<u8> {
    let mut acc = [0u64; 4];
    let mut count = 0u64;
    for p in image.pixels() {
        if p.0[3] == 0 {
            continue;
        }
        acc[0] += p.0[0] as u64;
        acc[1] += p.0[1] as u64;
        acc[2] += p.0[2] as u64;
        acc[3] += p.0[3] as u64;
        count += 1;
    }
    if count == 0 {
        return Rgba([0, 0, 0, 0]);
    }
    Rgba([
        (acc[0] / count) as u8,
        (acc[1] / count) as u8,
        (acc[2] / count) as u8,
        (acc[3] / count) as u8,
    ])
}

fn mesh_from_image_box(image: &RgbaImage, scale: f32, y_offset: f32) -> render::Mesh {
    let mesh = voxelize::build_mesh_from_image(image, 1, 16);
    let mut out = mesh.clone();
    for v in &mut out.positions {
        v.x *= scale;
        v.y *= scale;
        v.z *= scale;
        v.y += y_offset;
    }
    out
}

fn make_flat(side: u32, color: Rgba<u8>) -> RgbaImage {
    RgbaImage::from_pixel(side, side, color)
}

fn camera_default() -> render::Camera {
    render::Camera {
        eye: Vector3::new(1.2, 1.4, 1.8),
        target: Vector3::new(0.0, 0.0, 0.0),
        up_hint: Vector3::new(0.0, 1.0, 0.0),
        scale: 0.8,
    }
}

fn light_default() -> render::Light {
    render::Light {
        direction: Vector3::new(-0.4, 1.0, 0.3).normalize(),
        color: Vector3::new(1.0, 1.0, 1.0),
        intensity: 1.2,
        ambient: 0.24,
    }
}

pub fn run_phase1(input: Option<PathBuf>, out: PathBuf, side: u32, depth: usize) -> Result<()> {
    ensure_dir(&out)?;
    let source = fallback_image(input.as_deref(), fixtures::voxel_actor_sprite);
    let before = resized(&source, side);
    let mesh = voxelize::build_mesh_from_image(&source, depth.max(1), 16);
    let after = render::render_from_mesh(&mesh, side, &render::iso_camera(), &light_default());
    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase2(footprint: Option<PathBuf>, stories: u32, out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let footprint_img = fallback_image(footprint.as_deref(), fixtures::building_footprint);
    let before = resized(&footprint_img, side);
    let mesh = build_building_mesh(&footprint_img, stories.max(1));
    let after = render::render_from_mesh(&mesh, side, &camera_default(), &light_default());
    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase3(sprite: Option<PathBuf>, out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let source = fallback_image(sprite.as_deref(), fixtures::foliage_sprite);
    let before = resized(&source, side);
    let mesh = build_crossed_foliage_mesh(&source);
    let after = render::render_from_mesh(&mesh, side, &render::iso_camera(), &light_default());
    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase4(out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let (before, mesh) = build_water_mesh(32);
    let after = render::render_from_mesh(
        &mesh,
        side,
        &render::Camera {
            eye: Vector3::new(2.0, 1.4, 2.0),
            target: Vector3::new(0.0, -0.1, 0.0),
            up_hint: Vector3::new(0.0, 1.0, 0.0),
            scale: 0.58,
        },
        &render::Light {
            direction: Vector3::new(0.1, 1.0, 0.2).normalize(),
            color: Vector3::new(0.35, 0.7, 1.0),
            intensity: 1.0,
            ambient: 0.3,
        },
    );
    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase5(input: Option<PathBuf>, out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let source = fallback_image(input.as_deref(), fixtures::voxel_actor_sprite);
    let before = resized(&source, side);
    let actor = voxelize::build_mesh_from_image(&source, 2, 16);
    let shifted_actor = render::translated_copy(&actor, Vector3::new(-0.1, 0.1, 0.0));
    let shadow = build_shadow_mesh(&actor, &render::Light { direction: Vector3::new(-0.5, 1.0, 0.2).normalize(), color: Vector3::new(0.5, 0.5, 0.5), intensity: 1.0, ambient: 0.2 });
    let ground = render::Mesh {
        positions: vec![
            Vector3::new(-2.0, -0.5, -2.0),
            Vector3::new(2.0, -0.5, -2.0),
            Vector3::new(2.0, -0.5, 2.0),
            Vector3::new(-2.0, -0.5, 2.0),
        ],
        colors: vec![Rgba([70, 68, 68, 255]); 4],
        indices: vec![0, 1, 2, 0, 2, 3],
    };
    let all = [
        &ground,
        &shadow,
        &shifted_actor,
    ];
    let after = render::render_from_meshes(&all, side, &camera_default(), &render::Light {
        direction: Vector3::new(-0.4, 1.1, 0.3).normalize(),
        color: Vector3::new(1.0, 1.0, 0.95),
        intensity: 1.05,
        ambient: 0.22,
    });
    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase6(input: Option<PathBuf>, out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let source = fallback_image(input.as_deref(), fixtures::humanoid_sprite);
    let before = resized(&source, side);

    let base = voxelize::build_mesh_from_image(&source, 2, 16);
    let bones = assign_bones(&base);
    let poses = [
        "idle",
        "walk-a",
        "walk-b",
        "death",
    ];
    let pose_data = build_pose_offsets();

    let mut pose_frames = Vec::new();
    for (name, pose) in poses.iter().zip(pose_data.iter()) {
        let mesh = apply_pose(&base, &bones, pose);
        let img = render::render_from_mesh(&mesh, side / 2, &render::Camera {
            eye: Vector3::new(1.3, 1.8, 1.3),
            target: Vector3::new(0.0, 0.0, 0.0),
            up_hint: Vector3::new(0.0, 1.0, 0.0),
            scale: 0.8,
        }, &render::Light {
            direction: Vector3::new(-0.4, 0.9, 0.25).normalize(),
            color: Vector3::new(1.0, 1.0, 1.0),
            intensity: 1.1,
            ambient: 0.2,
        });
        write_image(&out.join(format!("after-{name}.png")), &img)?;
        pose_frames.push(img);
    }

    let tile = mosaic4(&pose_frames, side)?;
    write_image(&out.join("after.png"), &tile)?;
    write_image(&out.join("before.png"), &before)?;
    Ok(())
}

pub fn run_phase7(input: Option<PathBuf>, out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let source = fallback_image(input.as_deref(), fixtures::voxel_actor_sprite);
    let before = resized(&source, side);
    let mesh = voxelize::build_mesh_from_image(&source, 1, 16);
    let mut after = render::render_from_mesh(&mesh, side, &render::iso_camera(), &light_default());

    let w = side as i32;
    let h = side as i32;
    draw_rect(&mut after, w / 6, 20, w * 5 / 6, 44, Rgba([0, 0, 0, 220]));
    draw_rect(&mut after, w / 6 + 16, 28, w * 5 / 6 - 40, 36, Rgba([60, 170, 70, 200]));
    draw_rect(&mut after, w / 6 + 16, 28, w / 2, 36, Rgba([30, 255, 40, 200]));
    draw_rect(&mut after, w / 2 + 60, h - 90, w / 2 + 110, h - 72, Rgba([0, 0, 0, 190]));
    draw_rect(&mut after, w / 2 + 66, h - 86, w / 2 + 80, h - 80, Rgba([230, 50, 40, 230]));
    draw_minus_five(&mut after, w / 2 + 82, h - 88, Rgba([255, 255, 255, 230]));

    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase8(out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let terrain = build_terrain_horizon_mesh();

    let base = sky_gradient(side, 0.0);
    write_image(&out.join("before.png"), &base)?;
    let labels = [0.0, 0.25, 0.5, 0.75];
    let mut first: Option<RgbaImage> = None;
    for (idx, time) in labels.iter().enumerate() {
        let mut backdrop = sky_gradient(side, *time);
        let terrain_img = render::render_from_mesh(
            &terrain,
            side,
            &render::Camera {
                eye: Vector3::new(0.8, 2.3, 1.1),
                target: Vector3::new(0.0, 0.2, 0.0),
                up_hint: Vector3::new(0.0, 1.0, 0.0),
                scale: 0.52,
            },
            &light_for_time(*time),
        );
        render::composite(&mut backdrop, &terrain_img, 0, 0)?;
        let name = format!("after-{idx}.png");
        write_image(&out.join(&name), &backdrop)?;
        if first.is_none() {
            first = Some(backdrop);
        }
    }
    if let Some(first_after) = first {
        write_image(&out.join("after.png"), &first_after)?;
    }
    Ok(())
}

pub fn run_phase9(out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let before = make_flat(side, Rgba([10, 12, 22, 0]));
    let mut cubes = Vec::new();
    for i in 0..30usize {
        let t = i as f32 / 29.0;
        let angle = t * std::f32::consts::PI * 1.1;
        let r = 0.2 + 1.4 * t;
        let y = (0.6 - t * 0.55) * (if i % 2 == 0 { 1.0 } else { -1.0 });
        let pos = Vector3::new(r * angle.cos(), y, r * angle.sin());
        let col = Rgba([
            (120.0 + t * 120.0) as u8,
            (170.0 - t * 110.0) as u8,
            255,
            (180.0 * (1.0 - t * 0.5)) as u8,
        ]);
        cubes.push(voxelize::build_cube(pos, 0.22, col));
    }
    let merged = merge_meshes(&cubes);
    let after = render::render_from_mesh(&merged, side, &render::Camera {
        eye: Vector3::new(1.7, 1.2, 2.0),
        target: Vector3::new(0.0, 0.0, 0.0),
        up_hint: Vector3::new(0.0, 1.0, 0.0),
        scale: 0.78,
    }, &render::Light {
        direction: Vector3::new(0.0, 1.0, 0.2).normalize(),
        color: Vector3::new(0.8, 0.9, 1.0),
        intensity: 1.15,
        ambient: 0.2,
    });
    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_phase10(input: Option<PathBuf>, out: PathBuf, side: u32) -> Result<()> {
    ensure_dir(&out)?;
    let source = fallback_image(input.as_deref(), fixtures::voxel_actor_sprite);
    let before = resized(&source, side);

    let full = voxelize::build_mesh_from_image(&source, 2, 16);
    let proxy_source = downsample(&source);
    let proxy = voxelize::build_mesh_from_image(&proxy_source, 2, 16);
    let impostor = build_impostor_mesh(&source);

    let full = render::translated_copy(&full, Vector3::new(-1.2, 0.0, 0.0));
    let proxy = render::translated_copy(&proxy, Vector3::new(0.0, 0.0, 0.0));
    let impostor = render::translated_copy(&impostor, Vector3::new(1.2, 0.0, 0.0));
    let meshes = [
        &full,
        &proxy,
        &impostor,
    ];
    let after = render::render_from_meshes(&meshes, side, &render::Camera {
        eye: Vector3::new(0.6, 1.6, 2.0),
        target: Vector3::new(0.0, 0.3, 0.0),
        up_hint: Vector3::new(0.0, 1.0, 0.0),
        scale: 0.68,
    }, &render::Light {
        direction: Vector3::new(-0.15, 1.0, 0.4).normalize(),
        color: Vector3::new(1.0, 0.95, 0.9),
        intensity: 1.1,
        ambient: 0.28,
    });

    write_image(&out.join("before.png"), &before)?;
    write_image(&out.join("after.png"), &after)?;
    Ok(())
}

pub fn run_all(out: &Path, side: u32) -> Result<()> {
    let roots = [
        out.join("phase-1-voxel-actors"),
        out.join("phase-2-mesh-buildings"),
        out.join("phase-3-crossed-foliage"),
        out.join("phase-4-mesh-water"),
        out.join("phase-5-shadows"),
        out.join("phase-6-skeletal"),
        out.join("phase-7-worldspace-ui"),
        out.join("phase-8-day-night"),
        out.join("phase-9-particles"),
        out.join("phase-10-lod"),
    ];

    let results: Vec<_> = roots
        .par_iter()
        .map(|dir| -> Result<()> {
            match dir.file_name().and_then(|f| f.to_str()).unwrap_or("") {
                "phase-1-voxel-actors" => run_phase1(None, dir.clone(), side, 2),
                "phase-2-mesh-buildings" => run_phase2(None, 3, dir.clone(), side),
                "phase-3-crossed-foliage" => run_phase3(None, dir.clone(), side),
                "phase-4-mesh-water" => run_phase4(dir.clone(), side),
                "phase-5-shadows" => run_phase5(None, dir.clone(), side),
                "phase-6-skeletal" => run_phase6(None, dir.clone(), side),
                "phase-7-worldspace-ui" => run_phase7(None, dir.clone(), side),
                "phase-8-day-night" => run_phase8(dir.clone(), side),
                "phase-9-particles" => run_phase9(dir.clone(), side),
                "phase-10-lod" => run_phase10(None, dir.clone(), side),
                _ => Ok(()),
            }
        })
        .collect();

    let failures: Vec<String> = results.into_iter().filter_map(|r| r.err().map(|e| e.to_string())).collect();
    if !failures.is_empty() {
        return Err(anyhow!("all failed: {}", failures.join(", ")));
    }
    Ok(())
}

fn build_building_mesh(footprint: &RgbaImage, stories: u32) -> render::Mesh {
    let mut base_blocks = Vec::new();
    let mut footprint_cells = vec![false; (footprint.width() * footprint.height()) as usize];
    let mut has_any = false;
    let mut min_x = f32::INFINITY;
    let mut max_x = -f32::INFINITY;
    let mut min_z = f32::INFINITY;
    let mut max_z = -f32::INFINITY;

    for z in 0..footprint.height() {
        for x in 0..footprint.width() {
            if footprint.get_pixel(x, z).0[3] <= 16 {
                continue;
            }
            footprint_cells[(z * footprint.width() + x) as usize] = true;
            has_any = true;
            min_x = min_x.min(x as f32);
            max_x = max_x.max(x as f32 + 1.0);
            min_z = min_z.min(z as f32);
            max_z = max_z.max(z as f32 + 1.0);
        }
    }

    if !has_any {
        return mesh_from_image_box(&fixtures::building_footprint(), 1.0, 0.0);
    }

    let w = footprint.width() as f32;
    let h = footprint.height() as f32;
    let off_x = -w * 0.5;
    let off_z = -h * 0.5;
    let stories = stories.max(1) as f32;

    for y in 0..stories as u32 {
        for z in 0..footprint.height() {
            for x in 0..footprint.width() {
                if !footprint_cells[(z * footprint.width() + x) as usize] {
                    continue;
                }
                let pos = Vector3::new((x as f32 + 0.5 + off_x), y as f32 * 0.55 - 0.25, (z as f32 + 0.5 + off_z));
                base_blocks.push((pos, 0.95f32, Rgba([201, 190, 172, 255])));
            }
        }
    }

    let mut out = add_cube_batch(&base_blocks);
    let roof = build_simple_gable_roof(
        off_x,
        off_z,
        w,
        h,
        stories,
        min_x,
        max_x,
        min_z,
        max_z,
        &footprint_cells,
        footprint.width(),
        Rgba([188, 102, 88, 230]),
    );
    // Capture the offset BEFORE moving roof.positions so we can rebase indices.
    let roof_pos_count = roof.positions.len();
    let index_base = out.positions.len() as u32;
    out.positions.extend(roof.positions);
    out.colors.extend(roof.colors);
    debug_assert_eq!(out.positions.len() - roof_pos_count, index_base as usize);
    out.indices.extend(roof.indices.iter().map(|i| i + index_base));
    out
}

fn build_simple_gable_roof(
    off_x: f32,
    off_z: f32,
    width: f32,
    height: f32,
    stories: f32,
    min_x: f32,
    max_x: f32,
    min_z: f32,
    max_z: f32,
    fill: &[bool],
    stride: u32,
    color: Rgba<u8>,
) -> render::Mesh {
    let mut verts = Vec::new();
    let mut cols = Vec::new();
    let mut idx = Vec::new();
    let span_x = (max_x - min_x).max(1.0);
    let center_x = (min_x + max_x) * 0.5;
    for z in min_z as i32..max_z as i32 {
        for x in min_x as i32..max_x as i32 {
            if x < 0 || z < 0 || (z as u32) >= fill.len() as u32 / stride || (x as u32) >= stride {
                continue;
            }
            let source_idx = (z as u32 * stride + x as u32) as usize;
            if source_idx >= fill.len() || !fill[source_idx] {
                continue;
            }

            let xf = x as f32 + off_x;
            let zf = z as f32 + off_z;
            let t0 = 1.0 - ((xf + 0.5 - center_x) / span_x).abs();
            let t1 = 1.0 - (((xf + 1.5 - center_x) / span_x).abs());
            let y0 = stories + 0.4 * t0;
            let y1 = stories + 0.4 * t1;
            let p0 = Vector3::new(xf + 0.15, y0, zf + 0.2);
            let p1 = Vector3::new(xf + 1.0, y1, zf + 0.2);
            let p2 = Vector3::new(xf + 1.0, y1, zf + 0.9);
            let p3 = Vector3::new(xf + 0.15, y0, zf + 0.9);

            let base = verts.len() as u32;
            verts.extend_from_slice(&[p0, p1, p2, p3]);
            cols.extend_from_slice(&[color; 4]);
            idx.extend_from_slice(&[base, base + 1, base + 2, base, base + 2, base + 3]);
        }
    }

    render::Mesh { positions: verts, colors: cols, indices: idx }
}

fn build_crossed_foliage_mesh(source: &RgbaImage) -> render::Mesh {
    let tint = average_color(source);
    let mut q1 = make_textured_quad(Vector3::new(-0.8, 0.0, -0.0), 1.2, 1.2, tint, 0.0);
    let mut q2 = make_textured_quad(Vector3::new(0.0, 0.0, -0.0), 1.2, 1.2, tint, std::f32::consts::FRAC_PI_2);
    q2.positions.iter_mut().for_each(|v| {
        let x = v.x;
        let z = v.z;
        v.x = x * 0.0 + z * 1.0;
        v.z = z * 0.0 + x * 1.0;
    });
    let mut out = merge_meshes(&[q1, q2]);
    for c in &mut out.colors {
        c[3] = tint[3];
    }
    out
}

fn make_textured_quad(center: Vector3<f32>, w: f32, h: f32, color: Rgba<u8>, rot_y: f32) -> render::Mesh {
    let hw = w * 0.5;
    let hh = h * 0.5;
    let mut verts = vec![
        Vector3::new(-hw, -hh, 0.0) + center,
        Vector3::new(hw, -hh, 0.0) + center,
        Vector3::new(hw, hh, 0.0) + center,
        Vector3::new(-hw, hh, 0.0) + center,
    ];
    if rot_y.abs() > 0.0001 {
        for v in &mut verts {
            let translated = *v - center;
            let x = translated.x * rot_y.cos() - translated.z * rot_y.sin();
            let z = translated.x * rot_y.sin() + translated.z * rot_y.cos();
            *v = Vector3::new(x, translated.y, z) + center;
        }
    }
    render::Mesh {
        positions: verts,
        colors: vec![color; 4],
        indices: vec![0, 1, 2, 0, 2, 3],
    }
}

fn build_water_mesh(size: usize) -> (RgbaImage, render::Mesh) {
    let side = 32u32;
    let mut before = RgbaImage::from_pixel(side, side, Rgba([18, 40, 75, 255]));
    let mut min = f32::INFINITY;
    let mut max = -f32::INFINITY;
    let mut heights = vec![0.0f32; (size + 1) * (size + 1)];
    for z in 0..=size {
        for x in 0..=size {
            let fx = x as f32 / size as f32 * std::f32::consts::TAU;
            let fz = z as f32 / size as f32 * std::f32::consts::TAU;
            let h = 0.32 * fx.sin()
                + 0.23 * (fz * 1.4).sin()
                + 0.17 * ((fx + fz) * 0.6).sin()
                + 0.12 * ((fx * 0.7 - fz * 0.2).sin());
            heights[z * (size + 1) + x] = h;
            min = min.min(h);
            max = max.max(h);
        }
    }

    let mut verts = Vec::new();
    let mut cols = Vec::new();
    for z in 0..=size {
        for x in 0..=size {
            let h = heights[z * (size + 1) + x];
            let shade = ((h - min) / (max - min + 1e-5) * 170.0) as u8;
            let xx = x as f32 / size as f32 * 4.0;
            let zz = z as f32 / size as f32 * 4.0;
            verts.push(Vector3::new(xx - 2.0, h * 0.7, zz - 2.0));
            cols.push(Rgba([20 + shade / 3, 80 + shade / 4, 170 + shade, 255]));
            before.put_pixel((x as u32) % side, (z as u32) % side, Rgba([10 + shade / 5, 60 + shade / 4, 120 + shade / 3, 255]));
        }
    }
    let mut tris = Vec::new();
    for z in 0..size {
        for x in 0..size {
            let i = z * (size + 1) + x;
            let i0 = i as u32;
            let i1 = (i + 1) as u32;
            let i2 = (i + size + 1) as u32;
            let i3 = (i + size + 2) as u32;
            tris.extend_from_slice(&[i0, i1, i3, i0, i3, i2]);
        }
    }
    (before, render::Mesh { positions: verts, colors: cols, indices: tris })
}

fn build_shadow_mesh(source: &render::Mesh, light: &render::Light) -> render::Mesh {
    let mut positions = Vec::new();
    let mut colors = Vec::new();
    let mut indices = Vec::new();
    for tri in source.indices.chunks(3) {
        if tri.len() != 3 {
            continue;
        }
        let p0 = project_shadow(source.positions[tri[0] as usize], light.direction, -0.45);
        let p1 = project_shadow(source.positions[tri[1] as usize], light.direction, -0.45);
        let p2 = project_shadow(source.positions[tri[2] as usize], light.direction, -0.45);
        let base = positions.len() as u32;
        positions.push(p0);
        positions.push(p1);
        positions.push(p2);
        let c = source.colors[tri[0] as usize];
        colors.push(Rgba([10, 10, 10, c[3] / 5]));
        colors.push(Rgba([10, 10, 10, c[3] / 5]));
        colors.push(Rgba([10, 10, 10, c[3] / 5]));
        indices.extend_from_slice(&[base, base + 1, base + 2]);
    }
    render::Mesh { positions, colors, indices }
}

fn project_shadow(point: Vector3<f32>, dir: Vector3<f32>, y: f32) -> Vector3<f32> {
    if dir.y >= -1e-4 {
        return point;
    }
    let t = (point.y - y) / (-dir.y);
    point - dir * t
}

fn assign_bones(mesh: &render::Mesh) -> Vec<usize> {
    let mut min_x = f32::INFINITY;
    let mut max_x = -f32::INFINITY;
    let mut min_y = f32::INFINITY;
    let mut max_y = -f32::INFINITY;
    for p in &mesh.positions {
        min_x = min_x.min(p.x);
        max_x = max_x.max(p.x);
        min_y = min_y.min(p.y);
        max_y = max_y.max(p.y);
    }
    let mid_y = (min_y + max_y) * 0.5;

    mesh.positions
        .iter()
        .map(|p| {
            if p.y < mid_y - 0.2 {
                if p.x < min_x + (max_x - min_x) * 0.35 {
                    8
                } else {
                    9
                }
            } else if p.y > mid_y + 0.2 {
                if p.x < min_x + (max_x - min_x) * 0.35 {
                    4
                } else {
                    5
                }
            } else if p.x < 0.0 {
                6
            } else {
                7
            }
        })
        .collect()
}

fn build_pose_offsets() -> [[Vector3<f32>; 12]; 4] {
    [
        [
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
        ],
        [
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.05),
            Vector3::new(0.0, 0.0, 0.12),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.03, 0.0, 0.0),
            Vector3::new(-0.03, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.06),
            Vector3::new(0.0, 0.0, -0.06),
            Vector3::new(0.02, 0.0, 0.0),
            Vector3::new(-0.02, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
        ],
        [
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, -0.05),
            Vector3::new(0.0, 0.0, -0.12),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(-0.03, 0.0, 0.0),
            Vector3::new(0.03, 0.0, 0.0),
            Vector3::new(0.0, 0.0, -0.06),
            Vector3::new(0.0, 0.0, 0.06),
            Vector3::new(-0.02, 0.0, 0.0),
            Vector3::new(0.02, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
        ],
        [
            Vector3::new(0.0, -0.35, 0.0),
            Vector3::new(0.0, -0.2, 0.0),
            Vector3::new(0.0, -0.15, 0.0),
            Vector3::new(0.0, -0.25, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.05, 0.0, 0.0),
            Vector3::new(-0.05, 0.0, 0.0),
            Vector3::new(0.0, 0.0, -0.02),
            Vector3::new(0.0, 0.0, 0.02),
            Vector3::new(0.0, 0.0, -0.02),
            Vector3::new(0.0, 0.0, 0.02),
        ],
    ]
}

fn apply_pose(base: &render::Mesh, bones: &[usize], pose: &[Vector3<f32>; 12]) -> render::Mesh {
    let mut out = base.clone();
    for (idx, p) in out.positions.iter_mut().enumerate() {
        let bone = bones[idx % bones.len()] % 12;
        *p += pose[bone];
    }
    out
}

fn mosaic4(images: &[RgbaImage], side: u32) -> Result<RgbaImage> {
    if images.is_empty() {
        return Ok(make_flat(side, Rgba([0, 0, 0, 0])));
    }
    let cell = side / 2;
    let mut out = make_flat(side, Rgba([0, 0, 0, 0]));
    let positions = [(0, 0), (cell as i32, 0), (0, cell as i32), (cell as i32, cell as i32)];
    let slots = [0usize, 1, 2, 3];
    for (slot, (px, py)) in slots.iter().zip(positions.iter()) {
        let img = images.get(*slot).unwrap_or(&images[0]);
        let resized = image::imageops::resize(img, cell, cell, FilterType::Nearest);
        for y in 0..cell {
            for x in 0..cell {
                let dst_x = x as i32 + px;
                let dst_y = y as i32 + py;
                if dst_x < 0 || dst_y < 0 || dst_x >= side as i32 || dst_y >= side as i32 {
                    continue;
                }
                let p = out.get_pixel_mut(dst_x as u32, dst_y as u32);
                blend_pixel(p, *resized.get_pixel(x, y));
            }
        }
    }
    Ok(out)
}

fn build_terrain_horizon_mesh() -> render::Mesh {
    let mut verts = Vec::new();
    let mut cols = Vec::new();
    verts.push(Vector3::new(-2.0, -0.1, -2.0));
    verts.push(Vector3::new(2.0, -0.1, -2.0));
    verts.push(Vector3::new(2.0, -0.4, 2.0));
    verts.push(Vector3::new(-2.0, -0.4, 2.0));
    verts.push(Vector3::new(-2.0, 0.65, -0.2));
    verts.push(Vector3::new(2.0, 0.65, -0.2));
    verts.push(Vector3::new(2.0, -0.4, 1.5));
    verts.push(Vector3::new(-2.0, -0.4, 1.5));
    let c = [Rgba([130, 150, 70, 220]), Rgba([130, 150, 70, 220]), Rgba([95, 120, 70, 220]), Rgba([95, 120, 70, 220]), Rgba([170, 150, 120, 220]), Rgba([170, 150, 120, 220]), Rgba([120, 120, 90, 220]), Rgba([120, 120, 90, 220])];
    cols.extend_from_slice(&c);
    let idx = vec![0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
    render::Mesh { positions: verts, colors: cols, indices: idx }
}

fn sky_gradient(side: u32, time: f32) -> RgbaImage {
    let mut img = RgbaImage::from_pixel(side, side, Rgba([0, 0, 0, 255]));
    let t = time % 1.0;
    let (top, mid, bottom) = if t < 0.25 {
        (Rgba([252, 188, 128, 255]), Rgba([244, 150, 108, 255]), Rgba([74, 48, 34, 255]))
    } else if t < 0.5 {
        (Rgba([150, 205, 255, 255]), Rgba([100, 175, 245, 255]), Rgba([70, 150, 95, 255]))
    } else if t < 0.75 {
        (Rgba([240, 130, 90, 255]), Rgba([188, 90, 70, 255]), Rgba([46, 30, 52, 255]))
    } else {
        (Rgba([70, 58, 104, 255]), Rgba([40, 28, 68, 255]), Rgba([14, 9, 30, 255]))
    };
    for y in 0..side {
        let f = y as f32 / (side.max(1) as f32);
        for x in 0..side {
            let r = top[0] as f32 * (1.0 - f) + bottom[0] as f32 * f;
            let g = top[1] as f32 * (1.0 - f) + bottom[1] as f32 * f;
            let b = top[2] as f32 * (1.0 - f) + bottom[2] as f32 * f;
            img.put_pixel(x, y, Rgba([r as u8, g as u8, b as u8, 255]));
            let _ = mid;
        }
    }
    img
}

fn light_for_time(time: f32) -> render::Light {
    let t = time.min(1.0);
    let mut warm = 0.8;
    if t < 0.25 {
        warm = 0.95 + 0.2 * (1.0 - (t * 4.0));
    } else if t < 0.5 {
        warm = 0.75;
    } else if t < 0.75 {
        warm = 0.9;
    } else {
        warm = 1.05;
    }
    render::Light {
        direction: Vector3::new(-0.6, 1.0, 0.25).normalize(),
        color: Vector3::new(warm, 0.8 + (1.0 - warm) * 0.3, 1.0),
        intensity: 1.1,
        ambient: 0.25,
    }
}

fn build_impostor_mesh(source: &RgbaImage) -> render::Mesh {
    let color = average_color(source);
    voxelize::build_cube(Vector3::new(0.0, 0.35, 0.0), 1.45, Rgba([color[0], color[1], color[2], 220]))
}

