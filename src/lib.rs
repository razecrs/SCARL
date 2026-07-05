pub mod upscaler;
pub mod utils;

use std::ffi::CStr;
use std::os::raw::c_char;
use crate::upscaler::UpscaleEngine;
use crate::upscaler::tiler::{Tiler, UpscaledTile};
use crate::utils::{image_to_array, array_to_image};
use image::GenericImageView;

#[no_mangle]
pub extern "C" fn upscale_image(
    input_path: *const c_char,
    output_path: *const c_char,
    model_name: *const c_char,
    target_width: i32,
    target_height: i32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32,
    preset_mode: i32
) -> i32 {
    let input = unsafe { CStr::from_ptr(input_path).to_string_lossy().into_owned() };
    let output = unsafe { CStr::from_ptr(output_path).to_string_lossy().into_owned() };
    let model = unsafe { CStr::from_ptr(model_name).to_string_lossy().into_owned() };

    match process_upscale(&input, &output, &model, target_width as u32, target_height as u32, vibrancy, sharpness, depixelate, preset_mode) {
        Ok(_) => 0,
        Err(e) => {
            eprintln!("Error during upscale: {}", e);
            -1
        }
    }
}

fn process_upscale(
    input_path: &str, 
    output_path: &str, 
    model_name: &str,
    target_w: u32,
    target_h: u32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32,
    preset_mode: i32
) -> anyhow::Result<()> {
    let result = if preset_mode == 2 || input_path.to_lowercase().ends_with(".gif") {
        process_upscale_gif(input_path, output_path, model_name, target_w, target_h, vibrancy, sharpness, depixelate)
    } else {
        process_upscale_inner(input_path, output_path, model_name, target_w, target_h, vibrancy, sharpness, depixelate, preset_mode)
    };
    
    match result {
        Ok(_) => Ok(()),
        Err(e) => {
            use std::io::Write;
            if let Ok(mut log) = std::fs::OpenOptions::new().append(true).create(true).open("scarl_debug.log") {
                let _ = writeln!(log, "Error: {}", e);
            }
            Err(e)
        }
    }
}

fn process_upscale_inner(
    input_path: &str, 
    output_path: &str, 
    model_name: &str,
    target_w: u32,
    target_h: u32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32,
    preset_mode: i32
) -> anyhow::Result<()> {
    use std::io::Write;
    let mut log = std::fs::File::create("scarl_debug.log")?;
    writeln!(log, "Starting upscale: input={}, output={}, target={}x{}, preset={}", input_path, output_path, target_w, target_h, preset_mode)?;

    // 1. Load Engine
    let mut engine = match UpscaleEngine::new(model_name) {
        Ok(e) => e,
        Err(e) => {
            writeln!(log, "Engine load failed: {}", e)?;
            return Err(e);
        }
    };
    
    // 2. Load Image
    let mut img = match crate::utils::load_image_from_path(input_path) {
        Ok(i) => i,
        Err(e) => {
            writeln!(log, "Image load failed: {}", e)?;
            return Err(e.into());
        }
    };

    // Pre-processing: De-pixelate / Smoothing
    if depixelate > 0.0 {
        let rgb_img = img.to_rgb8();
        let blurred = image::imageops::blur(&rgb_img, depixelate);
        img = image::DynamicImage::ImageRgb8(blurred);
    }

    // 3. Multi-Pass AI Upscaling
    let mut current_img = img;
    let tiler = Tiler::new(256, 16);

    while current_img.width() < target_w || current_img.height() < target_h {
        let (w, h) = current_img.dimensions();
        writeln!(log, "  [Pass] Growing {}x{} -> {}x{}", w, h, w * 4, h * 4)?;
        
        let tiles = tiler.split(&current_img);
        let mut upscaled_tiles = Vec::new();
        
        for (i, tile) in tiles.into_iter().enumerate() {
            let (input_tensor, alpha_mask) = image_to_array(&tile.data);
            let output_tensor = engine.predict(input_tensor)?;
            let mut upscaled_img = array_to_image(output_tensor);
            
            if let Some(mask) = alpha_mask {
                let scaled_mask = image::imageops::resize(&mask, upscaled_img.width(), upscaled_img.height(), image::imageops::FilterType::CatmullRom);
                let mut upscaled_rgba = upscaled_img.to_rgba8();
                for y in 0..upscaled_rgba.height() {
                    for x in 0..upscaled_rgba.width() {
                        let m = scaled_mask.get_pixel(x, y)[0];
                        upscaled_rgba.get_pixel_mut(x, y)[3] = m;
                    }
                }
                upscaled_img = image::DynamicImage::ImageRgba8(upscaled_rgba);
            }
            
            if i == 0 {
                writeln!(log, "  [Tile] Sample Tile size upscaled to {}x{}", upscaled_img.width(), upscaled_img.height())?;
            }

            upscaled_tiles.push(UpscaledTile {
                x: tile.x,
                y: tile.y,
                data: upscaled_img,
            });
        }
        
        // The merge MUST use exactly w*4 to grow correctly
        current_img = tiler.merge(upscaled_tiles, 4, w * 4, h * 4);
        
        if current_img.width() > 60000 { break; } // Safety
    }

    // 4. Precision Resize to Target
    let mut final_img = if current_img.width() != target_w || current_img.height() != target_h {
        writeln!(log, "  [Final] Precision resize {}x{} to {}x{}", current_img.width(), current_img.height(), target_w, target_h)?;
        current_img.resize_exact(target_w, target_h, image::imageops::FilterType::CatmullRom)
    } else {
        current_img
    };
    
    // 5. Post-Processing (Colours)
    crate::utils::apply_colours(&mut final_img, vibrancy, sharpness);
    
    // 6. Sticker Mode
    if preset_mode == 1 || preset_mode == 3 {
        crate::utils::apply_sticker_stroke(&mut final_img);
    }
    
    // 7. Save
    if preset_mode == 1 || preset_mode == 3 {
        let max_size = if preset_mode == 3 { 512 * 1024 } else { 5 * 1024 * 1024 };
        let min_width = if preset_mode == 3 { 320 } else { 128 };
        
        let mut cur_img = final_img;
        loop {
            let mut buf = std::io::Cursor::new(Vec::new());
            cur_img.write_to(&mut buf, image::ImageFormat::Png)?;
            let size = buf.get_ref().len();
            
            if size <= max_size || cur_img.width() <= min_width {
                std::fs::write(output_path, buf.into_inner())?;
                break;
            } else {
                let n_w = (cur_img.width() as f32 * 0.9) as u32;
                let n_h = (cur_img.height() as f32 * 0.9) as u32;
                cur_img = cur_img.resize_exact(n_w, n_h, image::imageops::FilterType::CatmullRom);
            }
        }
    } else {
        final_img.save(output_path)?;
        
        let max_file_size = 10 * 1024 * 1024; // 10 MB
        if let Ok(metadata) = std::fs::metadata(output_path) {
            if metadata.len() > max_file_size {
                let jpeg_path = std::path::Path::new(output_path).with_extension("jpg");
                if jpeg_path != std::path::Path::new(output_path) {
                    let mut quality = 90;
                    loop {
                        let file = std::fs::File::create(&jpeg_path)?;
                        let mut encoder = image::codecs::jpeg::JpegEncoder::new_with_quality(file, quality);
                        if encoder.encode_image(&final_img).is_ok() {
                            if let Ok(meta) = std::fs::metadata(&jpeg_path) {
                                if meta.len() <= max_file_size || quality <= 50 {
                                    // Delete the original oversized file and keep the jpeg
                                    let _ = std::fs::remove_file(output_path);
                                    break;
                                }
                            }
                        }
                        quality -= 5;
                    }
                } else {
                    // It is already a jpeg, so just adjust quality of the existing file in place
                    let mut quality = 85;
                    loop {
                        let file = std::fs::File::create(output_path)?;
                        let mut encoder = image::codecs::jpeg::JpegEncoder::new_with_quality(file, quality);
                        if encoder.encode_image(&final_img).is_ok() {
                            if let Ok(meta) = std::fs::metadata(output_path) {
                                if meta.len() <= max_file_size || quality <= 50 {
                                    break;
                                }
                            }
                        }
                        quality -= 5;
                    }
                }
            }
        }
    }
    
    Ok(())
}

fn process_upscale_gif(
    input_path: &str, 
    output_path: &str, 
    model_name: &str,
    target_w: u32,
    target_h: u32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32
) -> anyhow::Result<()> {
    use std::io::Write;
    use image::codecs::gif::{GifDecoder, GifEncoder, Repeat};
    use image::{AnimationDecoder, Frame};
    use std::fs::File;

    let mut log = File::create("scarl_debug.log")?;
    writeln!(log, "Starting GIF upscale: input={}, target={}x{}", input_path, target_w, target_h)?;

    let file = File::open(input_path)?;
    let decoder = GifDecoder::new(file)?;
    let frames = decoder.into_frames();
    
    let mut engine = UpscaleEngine::new(model_name)?;
    
    let out_file = File::create(output_path)?;
    let mut encoder = GifEncoder::new(out_file);
    encoder.set_repeat(Repeat::Infinite)?;

    let tiler = Tiler::new(256, 16);

    for (i, frame_result) in frames.enumerate() {
        let frame = frame_result?;
        let delay = frame.delay();
        let buffer = frame.into_buffer();
        let mut img = image::DynamicImage::ImageRgba8(buffer);
        
        if depixelate > 0.0 {
            let rgb_img = img.to_rgba8();
            let blurred = image::imageops::blur(&rgb_img, depixelate);
            img = image::DynamicImage::ImageRgba8(blurred);
        }

        // Recursive Upscale Loop for GIF Frame
        let mut current_img = img;
        
        while current_img.width() < target_w || current_img.height() < target_h {
            let (w, h) = current_img.dimensions();
            let tiles = tiler.split(&current_img);
            let mut upscaled_tiles = Vec::new();
            
            for tile in tiles {
                let (input_tensor, alpha_mask) = image_to_array(&tile.data);
                let output_tensor = engine.predict(input_tensor)?;
                let mut upscaled_img = array_to_image(output_tensor);
                
                if let Some(mask) = alpha_mask {
                    let scaled_mask = image::imageops::resize(&mask, upscaled_img.width(), upscaled_img.height(), image::imageops::FilterType::CatmullRom);
                    let mut upscaled_rgba = upscaled_img.to_rgba8();
                    for y in 0..upscaled_rgba.height() {
                        for x in 0..upscaled_rgba.width() {
                            upscaled_rgba.get_pixel_mut(x, y)[3] = scaled_mask.get_pixel(x, y)[0];
                        }
                    }
                    upscaled_img = image::DynamicImage::ImageRgba8(upscaled_rgba);
                }
                
                upscaled_tiles.push(UpscaledTile {
                    x: tile.x,
                    y: tile.y,
                    data: upscaled_img,
                });
            }
            
            current_img = tiler.merge(upscaled_tiles, 4, w * 4, h * 4);
            if current_img.width() > 40000 { break; }
        }
        
        let mut final_img = if current_img.width() != target_w || current_img.height() != target_h {
            current_img.resize_exact(target_w, target_h, image::imageops::FilterType::CatmullRom)
        } else {
            current_img
        };
        
        crate::utils::apply_colours(&mut final_img, vibrancy, sharpness);
        
        let final_rgba = final_img.into_rgba8();
        let out_frame = Frame::from_parts(final_rgba, 0, 0, delay);
        encoder.encode_frame(out_frame)?;
        writeln!(log, "Encoded frame {}", i)?;
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_upscale_precise_resolutions() {
        let input_path = "R:\\thingy.png";
        let model_path = "models/realesrgan-x4plus-anime.onnx";
        if !std::path::Path::new(input_path).exists() || !std::path::Path::new(model_path).exists() {
            println!("Skipping test: test files not found.");
            return;
        }
        
        let resolutions = vec![
            (1000, "1k"),
            (2000, "2k"),
            (3000, "3k"),
            (4000, "4k"),
            (5000, "5k"),
            (6000, "6k"),
            (7000, "7k"),
            (8000, "8k"),
            (10000, "10k"),
        ];

        for (res, name) in resolutions {
            let output_path = format!("R:\\thingy_{}.png", name);
            println!("Testing upscale to {} ({}x{})", name, res, res);
            
            let result = process_upscale(
                input_path,
                &output_path,
                model_path,
                res,
                res,
                1.0, // vibrancy
                0.0, // sharpness
                0.0, // depixelate
                0    // preset_mode
            );
            
            assert!(result.is_ok(), "Upscaling failed for {}", name);
            
            // Verify output dimensions and size limit
            let mut actual_path = output_path.clone();
            if !std::path::Path::new(&actual_path).exists() {
                let jpg_path = format!("R:\\thingy_{}.jpg", name);
                if std::path::Path::new(&jpg_path).exists() {
                    actual_path = jpg_path;
                }
            }
            
            let metadata = std::fs::metadata(&actual_path).expect("Failed to read metadata");
            assert!(metadata.len() <= 10 * 1024 * 1024, "File size for {} ({} bytes) exceeds 10MB limit!", name, metadata.len());
            
            let out_img = image::open(&actual_path).expect("Failed to open output image");
            assert_eq!(out_img.width(), res, "Width mismatch for {}", name);
            assert_eq!(out_img.height(), res, "Height mismatch for {}", name);
            println!("Success: generated {} at exactly {}x{} (size: {} bytes)", name, out_img.width(), out_img.height(), metadata.len());
        }
    }
}
