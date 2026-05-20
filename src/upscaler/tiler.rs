use image::{DynamicImage, GenericImageView, ImageBuffer, Rgba};


pub struct Tiler {
    tile_size: u32,
    overlap: u32,
}

impl Tiler {
    pub fn new(tile_size: u32, overlap: u32) -> Self {
        Self { tile_size, overlap }
    }

    pub fn split(&self, img: &DynamicImage) -> Vec<Tile> {
        let (width, height) = img.dimensions();
        let mut tiles = Vec::new();

        let stride = self.tile_size - self.overlap;

        for y in (0..height).step_by(stride as usize) {
            for x in (0..width).step_by(stride as usize) {
                let x_end = (x + self.tile_size).min(width);
                let y_end = (y + self.tile_size).min(height);
                
                let tile_w = x_end - x;
                let tile_h = y_end - y;
                
                // Crop the tile
                let tile_img = img.crop_imm(x, y, tile_w, tile_h);
                
                tiles.push(Tile {
                    x,
                    y,
                    width: tile_w,
                    height: tile_h,
                    data: tile_img,
                });
            }
        }

        tiles
    }

    pub fn merge(&self, tiles: Vec<UpscaledTile>, scale: u32, target_width: u32, target_height: u32) -> DynamicImage {
        let mut output: ImageBuffer<Rgba<u8>, Vec<u8>> = ImageBuffer::new(target_width, target_height);
        let overlap_scaled = self.overlap * scale;

        for tile in tiles {
            let x_start_scaled = tile.x * scale;
            let y_start_scaled = tile.y * scale;
            
            let tile_rgba = tile.data.to_rgba8();
            for (px, py, pixel) in tile_rgba.enumerate_pixels() {
                let target_x = x_start_scaled + px;
                let target_y = y_start_scaled + py;
                
                if target_x < target_width && target_y < target_height {
                    // Simple logic for non-overlapping pixels
                    let is_overlap_x = tile.x > 0 && px < overlap_scaled;
                    let is_overlap_y = tile.y > 0 && py < overlap_scaled;
                    
                    if !is_overlap_x && !is_overlap_y {
                        output.put_pixel(target_x, target_y, *pixel);
                    } else {
                        let existing = output.get_pixel(target_x, target_y);
                        if existing[3] == 0 {
                            output.put_pixel(target_x, target_y, *pixel);
                        } else {
                            // Blending logic
                            let mut w = 1.0f32;
                            if is_overlap_x {
                                let t = px as f32 / overlap_scaled as f32;
                                w *= t * t * (3.0 - 2.0 * t);
                            }
                            if is_overlap_y {
                                let t = py as f32 / overlap_scaled as f32;
                                w *= t * t * (3.0 - 2.0 * t);
                            }
                            
                            let r = (existing[0] as f32 * (1.0 - w) + pixel[0] as f32 * w).clamp(0.0, 255.0) as u8;
                            let g = (existing[1] as f32 * (1.0 - w) + pixel[1] as f32 * w).clamp(0.0, 255.0) as u8;
                            let b = (existing[2] as f32 * (1.0 - w) + pixel[2] as f32 * w).clamp(0.0, 255.0) as u8;
                            let a = (existing[3] as f32 * (1.0 - w) + pixel[3] as f32 * w).clamp(0.0, 255.0) as u8;
                            output.put_pixel(target_x, target_y, Rgba([r, g, b, a]));
                        }
                    }
                }
            }
        }

        DynamicImage::ImageRgba8(output)
    }
}

pub struct Tile {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
    pub data: DynamicImage,
}

pub struct UpscaledTile {
    pub x: u32,
    pub y: u32,
    pub data: DynamicImage,
}
