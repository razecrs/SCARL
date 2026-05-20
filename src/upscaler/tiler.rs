use image::{DynamicImage, GenericImageView, ImageBuffer, Rgba, Rgb};


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
                
                // Crop the tile
                let tile_img = img.crop_imm(x, y, x_end - x, y_end - y);
                
                tiles.push(Tile {
                    x,
                    y,
                    width: x_end - x,
                    height: y_end - y,
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
            let x_scaled = tile.x * scale;
            let y_scaled = tile.y * scale;
            
            let tile_rgba = tile.data.to_rgba8();
            for (px, py, pixel) in tile_rgba.enumerate_pixels() {
                let target_x = x_scaled + px;
                let target_y = y_scaled + py;
                
                if target_x < target_width && target_y < target_height {
                    let mut w_new = 1.0;
                    
                    if overlap_scaled > 0 {
                        let wl = if tile.x > 0 && px < overlap_scaled {
                            let t = px as f32 / overlap_scaled as f32;
                            t * t * (3.0 - 2.0 * t)
                        } else {
                            1.0
                        };
                        
                        let wt = if tile.y > 0 && py < overlap_scaled {
                            let t = py as f32 / overlap_scaled as f32;
                            t * t * (3.0 - 2.0 * t)
                        } else {
                            1.0
                        };
                        
                        w_new = wl * wt;
                    }
                    
                    if w_new < 1.0 {
                        let existing = output.get_pixel(target_x, target_y);
                        // If existing pixel is empty (alpha 0), don't blend, just take the new pixel
                        if existing[3] == 0 {
                            output.put_pixel(target_x, target_y, *pixel);
                        } else {
                            let r = (existing[0] as f32 * (1.0 - w_new) + pixel[0] as f32 * w_new).clamp(0.0, 255.0) as u8;
                            let g = (existing[1] as f32 * (1.0 - w_new) + pixel[1] as f32 * w_new).clamp(0.0, 255.0) as u8;
                            let b = (existing[2] as f32 * (1.0 - w_new) + pixel[2] as f32 * w_new).clamp(0.0, 255.0) as u8;
                            let a = (existing[3] as f32 * (1.0 - w_new) + pixel[3] as f32 * w_new).clamp(0.0, 255.0) as u8;
                            output.put_pixel(target_x, target_y, Rgba([r, g, b, a]));
                        }
                    } else {
                        output.put_pixel(target_x, target_y, *pixel);
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
