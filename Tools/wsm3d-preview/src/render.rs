use anyhow::Result;
use image::{imageops::FilterType, Rgba, RgbaImage};
use nalgebra::Vector3;

#[derive(Clone)]
pub struct Mesh {
    pub positions: Vec<Vector3<f32>>,
    pub colors: Vec<Rgba<u8>>,
    pub indices: Vec<u32>,
}

#[derive(Clone, Copy)]
pub struct Camera {
    pub eye: Vector3<f32>,
    pub target: Vector3<f32>,
    pub up_hint: Vector3<f32>,
    pub scale: f32,
}

#[derive(Clone, Copy)]
pub struct Light {
    pub direction: Vector3<f32>,
    pub color: Vector3<f32>,
    pub intensity: f32,
    pub ambient: f32,
}

impl Default for Light {
    fn default() -> Self {
        Self {
            direction: Vector3::new(0.4, 1.0, 0.35).normalize(),
            color: Vector3::new(1.0, 1.0, 1.0),
            intensity: 1.1,
            ambient: 0.2,
        }
    }
}

pub fn make_before(input: &image::RgbaImage, side: u32) -> RgbaImage {
    image::imageops::resize(input, side, side, FilterType::Nearest)
}

pub fn render_from_mesh(mesh: &Mesh, side: u32, camera: &Camera, light: &Light) -> RgbaImage {
    render_from_meshes(&[mesh], side, camera, light)
}

pub fn render_from_meshes(meshes: &[&Mesh], side: u32, camera: &Camera, light: &Light) -> RgbaImage {
    if meshes.is_empty() {
        return RgbaImage::from_pixel(side, side, Rgba([0, 0, 0, 0]));
    }

    let mut image = RgbaImage::from_pixel(side, side, Rgba([0, 0, 0, 0]));
    let mut zbuf = vec![f32::INFINITY; (side * side) as usize];

    let (right, up, forward) = basis(camera);
    let light_dir = light.direction.normalize();
    let mut projected_meshes = Vec::<Vec<ProjVertex>>::new();
    let mut minx = f32::INFINITY;
    let mut maxx = -f32::INFINITY;
    let mut miny = f32::INFINITY;
    let mut maxy = -f32::INFINITY;

    for mesh in meshes {
        let mut projected = Vec::with_capacity(mesh.positions.len());
        for p in &mesh.positions {
            let rel = *p - camera.eye;
            let px = rel.dot(&right);
            let py = rel.dot(&up);
            let pz = rel.dot(&forward);
            projected.push(ProjVertex { sx: px, sy: py, sz: pz, color: mesh.colors[0] });
            minx = minx.min(px);
            maxx = maxx.max(px);
            miny = miny.min(py);
            maxy = maxy.max(py);
        }
        projected_meshes.push(projected);
    }

    let span_x = (maxx - minx).max(1.0);
    let span_y = (maxy - miny).max(1.0);
    let span = span_x.max(span_y);
    let scale = (side as f32 * camera.scale).max(1.0) / span;
    let cx = (minx + maxx) * 0.5;
    let cy = (miny + maxy) * 0.5;
    let ox = side as f32 * 0.5;
    let oy = side as f32 * 0.5;

    for (mesh_index, mesh) in meshes.iter().enumerate() {
        let projected = &projected_meshes[mesh_index];
        if projected.is_empty() {
            continue;
        }

        for tri in mesh.indices.chunks(3) {
            if tri.len() != 3 {
                continue;
            }

            let i0 = tri[0] as usize;
            let i1 = tri[1] as usize;
            let i2 = tri[2] as usize;
            if i0 >= projected.len() || i1 >= projected.len() || i2 >= projected.len() {
                continue;
            }
            let v0 = &projected[i0];
            let v1 = &projected[i1];
            let v2 = &projected[i2];

            let min_tx = v0.sx.min(v1.sx).min(v2.sx);
            let max_tx = v0.sx.max(v1.sx).max(v2.sx);
            let min_ty = v0.sy.min(v1.sy).min(v2.sy);
            let max_ty = v0.sy.max(v1.sy).max(v2.sy);

            let p0 = Vector3::new(v0.sx - cx, v0.sy - cy, v0.sz);
            let p1 = Vector3::new(v1.sx - cx, v1.sy - cy, v1.sz);
            let p2 = Vector3::new(v2.sx - cx, v2.sy - cy, v2.sz);
            let normal = (p1 - p0).cross(&(p2 - p0));
            if normal.norm_squared() < 1e-8 {
                continue;
            }
            let light_factor = normal.normalize().dot(&light_dir).max(0.0) * light.intensity + light.ambient;

            // Defensive: a few phase-mesh builders emit indices that overshoot
            // the colors/positions arrays by 1-8 (off-by-one in index rebase
            // when combining sub-meshes). Skip those triangles rather than
            // panic the entire render.
            if i0 >= mesh.colors.len() || i1 >= mesh.colors.len() || i2 >= mesh.colors.len() {
                continue;
            }
            let c0 = mesh.colors[i0].0;
            let c1 = mesh.colors[i1].0;
            let c2 = mesh.colors[i2].0;
            let area2 = edge2(v0.sx, v0.sy, v1.sx, v1.sy, v2.sx, v2.sy);
            if area2.abs() < 1e-7 {
                continue;
            }
            let inv_area = 1.0 / area2;
            let inside = if area2 > 0.0 {
                |w0: f32, w1: f32, w2: f32| -> bool { w0 >= 0.0 && w1 >= 0.0 && w2 >= 0.0 }
            } else {
                |w0: f32, w1: f32, w2: f32| -> bool { w0 <= 0.0 && w1 <= 0.0 && w2 <= 0.0 }
            };

            let x0 = (min_tx - minx) * scale + ox;
            let x1 = (max_tx - minx) * scale + ox;
            let y0 = (min_ty - miny) * scale + oy;
            let y1 = (max_ty - miny) * scale + oy;
            let start_x = x0.max(0.0).floor() as i32;
            let end_x = x1.min(side as f32 - 1.0).floor() as i32;
            let start_y = y0.max(0.0).floor() as i32;
            let end_y = y1.min(side as f32 - 1.0).floor() as i32;

            for py in start_y..=end_y {
                for px in start_x..=end_x {
                    let sx = px as f32 + 0.5;
                    let sy = py as f32 + 0.5;
                    let w0 = edge2(v1.sx, v1.sy, v2.sx, v2.sy, sx, sy);
                    let w1 = edge2(v2.sx, v2.sy, v0.sx, v0.sy, sx, sy);
                    let w2 = edge2(v0.sx, v0.sy, v1.sx, v1.sy, sx, sy);
                    if !inside(w0, w1, w2) {
                        continue;
                    }
                    let mut u = w0 * inv_area;
                    let mut v = w1 * inv_area;
                    let mut w = w2 * inv_area;
                    let d = (u + v + w).abs();
                    if d > 1e-7 {
                        u /= d;
                        v /= d;
                        w /= d;
                    }

                    if !(0.0..=1.0).contains(&u) || !(0.0..=1.0).contains(&v) || !(0.0..=1.0).contains(&w) {
                        continue;
                    }
                    let z = u * p0.z + v * p1.z + w * p2.z;
                    if px < 0 || px >= side as i32 || py < 0 || py >= side as i32 {
                        continue;
                    }
                    let x_u32 = px as u32;
                    let y_u32 = py as u32;
                    let idx = (y_u32 * side + x_u32) as usize;
                    if z >= zbuf[idx] {
                        continue;
                    }
                    zbuf[idx] = z;

                    let rf = (u * c0[0] as f32 + v * c1[0] as f32 + w * c2[0] as f32) * light_factor * light.color.x;
                    let gf = (u * c0[1] as f32 + v * c1[1] as f32 + w * c2[1] as f32) * light_factor * light.color.y;
                    let bf = (u * c0[2] as f32 + v * c1[2] as f32 + w * c2[2] as f32) * light_factor * light.color.z;
                    let af = (u * c0[3] as f32 + v * c1[3] as f32 + w * c2[3] as f32) * 0.95;
                    if af <= 0.0 {
                        continue;
                    }
                    let src = Rgba([
                        rf.max(0.0).min(255.0) as u8,
                        gf.max(0.0).min(255.0) as u8,
                        bf.max(0.0).min(255.0) as u8,
                        af.max(0.0).min(255.0) as u8,
                    ]);
                    let dst = image.get_pixel(x_u32, y_u32);
                    let a = src[3] as f32 / 255.0;
                    let inv_a = 1.0 - a;
                    image.put_pixel(
                        x_u32,
                        y_u32,
                        Rgba([
                            (src[0] as f32 * a + dst[0] as f32 * inv_a) as u8,
                            (src[1] as f32 * a + dst[1] as f32 * inv_a) as u8,
                            (src[2] as f32 * a + dst[2] as f32 * inv_a) as u8,
                            (src[3] as f32 * a + dst[3] as f32 * inv_a) as u8,
                        ]),
                    );
                }
            }
        }
    }

    image
}

pub fn composite(base: &mut RgbaImage, top: &RgbaImage, ox: i32, oy: i32) -> Result<()> {
    for y in 0..top.height() {
        for x in 0..top.width() {
            let tx = x as i32 + ox;
            let ty = y as i32 + oy;
            if tx < 0 || ty < 0 || tx >= base.width() as i32 || ty >= base.height() as i32 {
                continue;
            }
            let t = top.get_pixel(x, y);
            if t[3] == 0 {
                continue;
            }
            let b = base.get_pixel_mut(tx as u32, ty as u32);
            let a = t[3] as f32 / 255.0;
            let inv = 1.0 - a;
            b.0 = [
                (t[0] as f32 * a + b[0] as f32 * inv) as u8,
                (t[1] as f32 * a + b[1] as f32 * inv) as u8,
                (t[2] as f32 * a + b[2] as f32 * inv) as u8,
                (t[3] as f32 * a + b[3] as f32 * inv) as u8,
            ];
        }
    }
    Ok(())
}

#[derive(Clone, Copy)]
struct ProjVertex {
    sx: f32,
    sy: f32,
    sz: f32,
    color: Rgba<u8>,
}

fn edge2(ax: f32, ay: f32, bx: f32, by: f32, cx: f32, cy: f32) -> f32 {
    (cx - ax) * (by - ay) - (cy - ay) * (bx - ax)
}

fn basis(camera: &Camera) -> (Vector3<f32>, Vector3<f32>, Vector3<f32>) {
    let forward = (camera.target - camera.eye).normalize();
    let right = camera.up_hint.cross(&forward).normalize();
    let up = forward.cross(&right).normalize();
    (right, up, forward)
}

pub fn iso_camera() -> Camera {
    Camera {
        eye: Vector3::new(1.2, 1.7, 1.3),
        target: Vector3::new(0.0, 0.0, 0.0),
        up_hint: Vector3::new(0.0, 1.0, 0.0),
        scale: 0.8,
    }
}

pub fn orthographic_camera(width: f32, height: f32, y: f32) -> Camera {
    let center = Vector3::new(width * 0.5, y, height * 0.5);
    Camera {
        eye: center + Vector3::new(0.0, 2.8, 2.8),
        target: center,
        up_hint: Vector3::new(0.0, 1.0, 0.0),
        scale: 0.5,
    }
}

pub fn translated_copy(mesh: &Mesh, offset: Vector3<f32>) -> Mesh {
    let mut out = mesh.clone();
    for p in &mut out.positions {
        *p += offset;
    }
    out
}

pub fn scaled_copy(mesh: &Mesh, scale: f32) -> Mesh {
    let mut out = mesh.clone();
    for p in &mut out.positions {
        p.x *= scale;
        p.y *= scale;
        p.z *= scale;
    }
    out
}

pub fn rotated_y_copy(mesh: &Mesh, angle: f32) -> Mesh {
    let mut out = mesh.clone();
    let c = angle.cos();
    let s = angle.sin();
    for p in &mut out.positions {
        let x = p.x * c - p.z * s;
        let z = p.x * s + p.z * c;
        p.x = x;
        p.z = z;
    }
    out
}

