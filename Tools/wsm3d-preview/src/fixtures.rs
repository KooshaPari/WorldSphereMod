use image::{Rgba, RgbaImage};

pub fn voxel_actor_sprite() -> RgbaImage {
    let mut img = RgbaImage::from_pixel(48, 48, Rgba([0, 0, 0, 0]));
    for y in 18..34 {
        for x in 14..34 {
            img.put_pixel(x, y, Rgba([60, 150, 240, 255]));
        }
    }
    for y in 10..18 {
        for x in 19..29 {
            img.put_pixel(x, y, Rgba([230, 200, 140, 255]));
        }
    }
    for y in 0..10 {
        let yy = y;
        if yy == 2 || yy == 8 {
            continue;
        }
        img.put_pixel(22, yy, Rgba([250, 200, 170, 255]));
        img.put_pixel(24, yy, Rgba([250, 200, 170, 255]));
    }
    img
}

pub fn building_footprint() -> RgbaImage {
    let mut img = RgbaImage::from_pixel(40, 30, Rgba([0, 0, 0, 0]));
    for y in 4..26 {
        for x in 6..34 {
            if x < 12 || x > 30 || y < 9 || y > 21 {
                continue;
            }
            if ((x - 6) % 2 == 0) || ((y - 4) % 2 == 0) {
                continue;
            }
            img.put_pixel(x, y, Rgba([145, 130, 110, 255]));
        }
    }
    for y in 0..2 {
        for x in 17..23 {
            img.put_pixel(x, y + 12, Rgba([170, 160, 150, 255]));
        }
    }
    img
}

pub fn foliage_sprite() -> RgbaImage {
    let mut img = RgbaImage::from_pixel(32, 32, Rgba([0, 0, 0, 0]));
    let cx = 16usize;
    let cy = 14usize;
    for y in 0..32 {
        for x in 0..32 {
            let dx = (x as i32 - cx as i32).abs();
            let dy = (y as i32 - cy as i32).abs();
            if dx < 4 && dy < 10 || dy < 4 && dx < 10 {
                let alpha = (255 - dx.min(dy) * 16).max(0);
                img.put_pixel(x, y, Rgba([40, 190, 80, alpha as u8]));
            }
        }
    }
    img
}

pub fn humanoid_sprite() -> RgbaImage {
    let mut img = RgbaImage::from_pixel(48, 64, Rgba([0, 0, 0, 0]));
    for y in 8..28 {
        for x in 18..30 {
            img.put_pixel(x, y, Rgba([220, 200, 180, 255]));
        }
    }
    for y in 28..56 {
        for x in 20..28 {
            img.put_pixel(x, y, Rgba([70, 140, 250, 255]));
        }
    }
    for y in 28..56 {
        img.put_pixel(18, y, Rgba([255, 230, 180, 255]));
        img.put_pixel(29, y, Rgba([255, 230, 180, 255]));
    }
    for y in 56..60 {
        for x in 19..29 {
            img.put_pixel(x, y, Rgba([200, 50, 30, 255]));
        }
    }
    img
}

