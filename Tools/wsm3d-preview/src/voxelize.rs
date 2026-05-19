use crate::render::Mesh;
use image::RgbaImage;
use nalgebra::Vector3;

pub fn build_mesh_from_image(image: &RgbaImage, depth: usize, alpha_min: u8) -> Mesh {
    let w = image.width() as usize;
    let h = image.height() as usize;
    let d = depth.max(1);

    let mut solid = vec![false; w * h * d];
    let mut colors = vec![[0u8; 4]; w * h * d];

    for y in 0..h {
        for x in 0..w {
            let px = image.get_pixel(x as u32, y as u32).0;
            if px[3] <= alpha_min {
                continue;
            }
            for z in 0..d {
                let i = (x * h + y) * d + z;
                solid[i] = true;
                colors[i] = px;
            }
        }
    }

    let mut vertices = Vec::new();
    let mut vertex_colors = Vec::new();
    let mut indices = Vec::new();

    let pivot = Vector3::new(-(w as f32 * 0.5), -(d as f32 * 0.5), -(h as f32 * 0.5));
    let cell = 0.9;
    greedy_mesh(
        &solid,
        &colors,
        w,
        h,
        d,
        pivot,
        cell,
        &mut vertices,
        &mut vertex_colors,
        &mut indices,
    );

    Mesh {
        positions: vertices,
        colors: vertex_colors,
        indices,
    }
}

fn idx(x: usize, y: usize, z: usize, h: usize, d: usize) -> usize {
    (x * h * d) + (y * d) + z
}

fn greedy_mesh(
    solid: &[bool],
    colors: &[[u8; 4]],
    w: usize,
    h: usize,
    d: usize,
    pivot: Vector3<f32>,
    cell: f32,
    verts: &mut Vec<Vector3<f32>>,
    cols: &mut Vec<image::Rgba<u8>>,
    tris: &mut Vec<u32>,
) {
    for dir in 0..6 {
        let axis = dir >> 1;
        let positive = (dir & 1) == 1;

        let (slice_count, u_count, v_count) = if axis == 0 {
            (w, h, d)
        } else if axis == 1 {
            (h, w, d)
        } else {
            (d, w, h)
        };

        let mut mask = vec![[0u8; 4]; u_count * v_count];
        let mut present = vec![false; u_count * v_count];

        for s in 0..slice_count {
            for u in 0..u_count {
                for v in 0..v_count {
                    let (cx, cy, cz) = if axis == 0 {
                        (s, u, v)
                    } else if axis == 1 {
                        (u, s, v)
                    } else {
                        (u, v, s)
                    };
                    if cx >= w || cy >= h || cz >= d {
                        continue;
                    }
                    let i = idx(cx, cy, cz, h, d);
                    if !solid[i] {
                        continue;
                    }

                    let nx = if axis == 0 {
                        if positive { cx + 1 } else { cx.saturating_sub(1) }
                    } else if axis == 1 {
                        if positive { cy + 1 } else { cy.saturating_sub(1) }
                    } else {
                        if positive { cz + 1 } else { cz.saturating_sub(1) }
                    };
                    let ny = if axis == 0 { cy } else { nx };
                    let nz = if axis == 0 { cz } else if axis == 1 { cz } else { cz };
                    let use_face = if axis == 0 {
                        !(nx < w)
                    } else if axis == 1 {
                        !(ny < h)
                    } else {
                        !(nz < d)
                    };
                    if !use_face {
                        let nidx = idx(nx.min(w.saturating_sub(1)), ny.min(h.saturating_sub(1)), nz.min(d.saturating_sub(1)), h, d);
                        if solid[nidx] {
                            continue;
                        }
                    }
                    let mi = v * u_count + u;
                    present[mi] = true;
                    mask[mi] = colors[i];
                }
            }

            let mut v = 0;
            while v < v_count {
                let mut u = 0;
                while u < u_count {
                    if !present[v * u_count + u] {
                        u += 1;
                        continue;
                    }
                    let sample = mask[v * u_count + u];
                    let mut u1 = u + 1;
                    while u1 < u_count && present[v * u_count + u1] && mask[v * u_count + u1] == sample {
                        u1 += 1;
                    }
                    let mut v1 = v + 1;
                    loop {
                        if v1 >= v_count {
                            break;
                        }
                        let mut ok = true;
                        for uu in u..u1 {
                            if !present[v1 * u_count + uu] || mask[v1 * u_count + uu] != sample {
                                ok = false;
                                break;
                            }
                        }
                        if !ok {
                            break;
                        }
                        v1 += 1;
                    }
                    let _ = sample;
                    for vv in v..v1 {
                        for uu in u..u1 {
                            present[vv * u_count + uu] = false;
                        }
                    }
                    emit_quad(axis, positive, s, u, v, u1 - u, v1 - v, pivot, cell, sample, verts, cols, tris);
                    u = u1;
                }
                v += 1;
            }
        }
    }
}

fn emit_quad(
    axis: usize,
    positive: bool,
    s: usize,
    u: usize,
    v: usize,
    w: usize,
    h: usize,
    pivot: Vector3<f32>,
    cell: f32,
    color: [u8; 4],
    verts: &mut Vec<Vector3<f32>>,
    cols: &mut Vec<image::Rgba<u8>>,
    tris: &mut Vec<u32>,
) {
    let a = s as f32 * cell;
    let b0 = u as f32 * cell;
    let b1 = (u + w) as f32 * cell;
    let c0 = v as f32 * cell;
    let c1 = (v + h) as f32 * cell;
    let mut quad = [
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(0.0, 0.0, 0.0),
    ];

    match (axis, positive) {
        (0, true) => {
            quad = [
                Vector3::new(a, b0, c0),
                Vector3::new(a, b1, c0),
                Vector3::new(a, b1, c1),
                Vector3::new(a, b0, c1),
            ];
        }
        (0, false) => {
            quad = [
                Vector3::new(a, b1, c0),
                Vector3::new(a, b0, c0),
                Vector3::new(a, b0, c1),
                Vector3::new(a, b1, c1),
            ];
        }
        (1, true) => {
            quad = [
                Vector3::new(b0, a, c0),
                Vector3::new(b1, a, c0),
                Vector3::new(b1, a, c1),
                Vector3::new(b0, a, c1),
            ];
        }
        (1, false) => {
            quad = [
                Vector3::new(b1, a, c0),
                Vector3::new(b0, a, c0),
                Vector3::new(b0, a, c1),
                Vector3::new(b1, a, c1),
            ];
        }
        (2, true) => {
            quad = [
                Vector3::new(b0, c0, a),
                Vector3::new(b1, c0, a),
                Vector3::new(b1, c1, a),
                Vector3::new(b0, c1, a),
            ];
        }
        (2, false) => {
            quad = [
                Vector3::new(b1, c0, a),
                Vector3::new(b0, c0, a),
                Vector3::new(b0, c1, a),
                Vector3::new(b1, c1, a),
            ];
        }
        _ => {}
    }

    let color = image::Rgba(color);
    let start = verts.len() as u32;
    for mut p in quad {
        p += pivot;
        verts.push(p);
        cols.push(color);
    }
    tris.extend_from_slice(&[start, start + 1, start + 2, start, start + 2, start + 3]);
}

pub fn build_cube(center: Vector3<f32>, size: f32, color: image::Rgba<u8>) -> Mesh {
    let h = size * 0.5;
    let mut verts = vec![
        center + Vector3::new(-h, -h, -h),
        center + Vector3::new(h, -h, -h),
        center + Vector3::new(h, h, -h),
        center + Vector3::new(-h, h, -h),
        center + Vector3::new(-h, -h, h),
        center + Vector3::new(h, -h, h),
        center + Vector3::new(h, h, h),
        center + Vector3::new(-h, h, h),
    ];
    let mut idx = Vec::new();
    let mut cols = Vec::new();

    let faces: [[usize; 4]; 6] = [
        [0, 1, 2, 3],
        [5, 4, 7, 6],
        [4, 0, 3, 7],
        [1, 5, 6, 2],
        [3, 2, 6, 7],
        [4, 5, 1, 0],
    ];
    for face in faces {
        let base = verts.len() as u32;
        cols.push(color);
        cols.push(color);
        cols.push(color);
        cols.push(color);
        verts.push(verts[face[0]]);
        verts.push(verts[face[1]]);
        verts.push(verts[face[2]]);
        verts.push(verts[face[3]]);
        idx.extend_from_slice(&[base, base + 1, base + 2, base, base + 2, base + 3]);
    }
    Mesh {
        positions: verts,
        colors: cols,
        indices: idx,
    }
}

