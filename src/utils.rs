use image::{DynamicImage, GenericImageView, ImageBuffer, Rgba};
use ndarray::{Array4};

pub fn image_to_array(img: &DynamicImage) -> (Array4<f32>, Option<ImageBuffer<image::Luma<u8>, Vec<u8>>>) {
    let (width, height) = img.dimensions();
    let mut array = Array4::zeros((1, 3, height as usize, width as usize));
    let mut has_alpha = false;

    if img.color().has_alpha() {
        has_alpha = true;
    }

    let mut alpha_mask = if has_alpha {
        Some(ImageBuffer::new(width, height))
    } else {
        None
    };

    let rgba_img = img.to_rgba8();
    for (x, y, pixel) in rgba_img.enumerate_pixels() {
        let Rgba([r, g, b, a]) = pixel;
        array[[0, 0, y as usize, x as usize]] = *r as f32 / 255.0;
        array[[0, 1, y as usize, x as usize]] = *g as f32 / 255.0;
        array[[0, 2, y as usize, x as usize]] = *b as f32 / 255.0;
        
        if let Some(mask) = &mut alpha_mask {
            mask.put_pixel(x, y, image::Luma([*a]));
        }
    }

    (array, alpha_mask)
}

pub fn array_to_image(array: Array4<f32>) -> DynamicImage {
    let (_, _, height, width) = array.dim();
    let mut img = ImageBuffer::new(width as u32, height as u32);

    for (x, y, pixel) in img.enumerate_pixels_mut() {
        let r = (array[[0, 0, y as usize, x as usize]].clamp(0.0, 1.0) * 255.0) as u8;
        let g = (array[[0, 1, y as usize, x as usize]].clamp(0.0, 1.0) * 255.0) as u8;
        let b = (array[[0, 2, y as usize, x as usize]].clamp(0.0, 1.0) * 255.0) as u8;
        *pixel = Rgba([r, g, b, 255]);
    }

    DynamicImage::ImageRgba8(img)
}

pub fn apply_sticker_stroke(img: &mut DynamicImage) {
    // Simple 4-pass dilation on alpha channel to create a white stroke
    let (w, h) = img.dimensions();
    let rgba_img = img.to_rgba8();
    let mut stroked = rgba_img.clone();
    
    let stroke_size = 4;
    
    for y in 0..h {
        for x in 0..w {
            let p = rgba_img.get_pixel(x, y);
            if p[3] < 128 { // If transparent
                // Check neighbors
                let mut near_opaque = false;
                'outer: for dy in -(stroke_size as i32)..=(stroke_size as i32) {
                    for dx in -(stroke_size as i32)..=(stroke_size as i32) {
                        let nx = x as i32 + dx;
                        let ny = y as i32 + dy;
                        if nx >= 0 && nx < w as i32 && ny >= 0 && ny < h as i32 {
                            if dx*dx + dy*dy <= stroke_size*stroke_size {
                                if rgba_img.get_pixel(nx as u32, ny as u32)[3] > 128 {
                                    near_opaque = true;
                                    break 'outer;
                                }
                            }
                        }
                    }
                }
                if near_opaque {
                    stroked.put_pixel(x, y, Rgba([255, 255, 255, 255]));
                }
            }
        }
    }
    *img = DynamicImage::ImageRgba8(stroked);
}

pub fn apply_colours(img: &mut DynamicImage, vibrancy: f32, sharpness: f32) {
    // 1. Vibrancy (Saturation increase)
    if vibrancy != 1.0 {
        *img = img.huerotate(0); // Placeholder for saturation if needed, 
                                 // actually we can use adjust_contrast or similar.
        // For now let's just use what's available in 'image' crate
    }
    
    // 2. Sharpness (Unsharp mask)
    if sharpness > 0.0 {
        *img = img.unsharpen(sharpness, 1);
    }
}

pub fn load_image_from_path(path: &str) -> anyhow::Result<DynamicImage> {
    let file = std::fs::File::open(path)?;
    let reader = std::io::BufReader::new(file);
    let img = image::io::Reader::new(reader)
        .with_guessed_format()?
        .decode()?;
    Ok(img)
}

#[test]
fn convert_logo_to_ico() {
    let img = load_image_from_path("Scarl.UI/Assets/logo.png").unwrap();
    // Resize to standard 256x256 icon size for best Explorer rendering
    let resized = img.resize(256, 256, image::imageops::FilterType::Lanczos3);
    resized.save_with_format("Scarl.UI/Assets/logo.ico", image::ImageFormat::Ico).unwrap();
}
