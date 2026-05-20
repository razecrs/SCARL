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
    scale: i32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32,
    preset_mode: i32
) -> i32 {
    let input = unsafe { CStr::from_ptr(input_path).to_string_lossy().into_owned() };
    let output = unsafe { CStr::from_ptr(output_path).to_string_lossy().into_owned() };
    let model = unsafe { CStr::from_ptr(model_name).to_string_lossy().into_owned() };

    match process_upscale(&input, &output, &model, scale, vibrancy, sharpness, depixelate, preset_mode) {
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
    scale_factor: i32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32,
    preset_mode: i32
) -> anyhow::Result<()> {
    let result = if preset_mode == 2 || input_path.to_lowercase().ends_with(".gif") {
        process_upscale_gif(input_path, output_path, model_name, scale_factor, vibrancy, sharpness, depixelate)
    } else {
        process_upscale_inner(input_path, output_path, model_name, scale_factor, vibrancy, sharpness, depixelate, preset_mode)
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
    scale_factor: i32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32,
    preset_mode: i32
) -> anyhow::Result<()> {
    use std::io::Write;
    let mut log = std::fs::File::create("scarl_debug.log")?;
    writeln!(log, "Starting upscale: input={}, output={}, model={}, scale={}, preset={}", input_path, output_path, model_name, scale_factor, preset_mode)?;

    // 1. Load Engine
    writeln!(log, "Loading engine...")?;
    let mut engine = match UpscaleEngine::new(model_name) {
        Ok(e) => e,
        Err(e) => {
            writeln!(log, "Engine load failed: {}", e)?;
            return Err(e);
        }
    };
    
    // 2. Load Image
    writeln!(log, "Loading image from: {}...", input_path)?;
    let mut img = match crate::utils::load_image_from_path(input_path) {
        Ok(i) => i,
        Err(e) => {
            writeln!(log, "Image load failed: {}", e)?;
            return Err(e.into());
        }
    };

    // Pre-processing: De-pixelate / Smoothing
    if depixelate > 0.0 {
        writeln!(log, "Applying de-pixelate smoothing (sigma = {})...", depixelate)?;
        let rgb_img = img.to_rgb8();
        let blurred = image::imageops::blur(&rgb_img, depixelate);
        img = image::DynamicImage::ImageRgb8(blurred);
    }

    let (orig_w, orig_h) = img.dimensions();
    writeln!(log, "Image dimensions: {}x{}", orig_w, orig_h)?;
    
    // 3. Recursive Upscale Loop
    let mut current_img = img;
    let mut current_scale: u32 = 1;
    let target_scale = scale_factor as u32;
    let tiler = Tiler::new(512, 32);

    writeln!(log, "  [Start] Dimensions: {}x{}, Target Scale: {}x", current_img.width(), current_img.height(), target_scale)?;

    while current_scale < target_scale {
        let (w, h) = current_img.dimensions();
        let next_scale = current_scale * 4;
        writeln!(log, "  [Loop] Pass {}x -> {}x", current_scale, next_scale)?;
        
        let tiles = tiler.split(&current_img);
        writeln!(log, "  [Loop] Created {} tiles for {}x{} image", tiles.len(), w, h)?;
        
        let mut upscaled_tiles = Vec::new();
        for (i, tile) in tiles.into_iter().enumerate() {
            let (input_tensor, alpha_mask) = image_to_array(&tile.data);
            let output_tensor = engine.predict(input_tensor)?;
            let mut upscaled_img = array_to_image(output_tensor);
            
            // Re-apply alpha mask if it existed
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
                writeln!(log, "  [Loop] Tile 0 Upscale: {}x{} -> {}x{}", tile.width, tile.height, upscaled_img.width(), upscaled_img.height())?;
            }

            upscaled_tiles.push(UpscaledTile {
                x: tile.x,
                y: tile.y,
                data: upscaled_img,
            });
        }
        
        // The scale factor for merge is always 4 because the model is x4
        current_img = tiler.merge(upscaled_tiles, 4, w * 4, h * 4);
        current_scale = next_scale;
        
        writeln!(log, "  [Loop] Resulting dimensions: {}x{}", current_img.width(), current_img.height())?;

        if current_scale > 256 { break; } // Safety
    }

    let target_w = orig_w * target_scale;
    let target_h = orig_h * target_scale;
    
    let mut final_img = if current_img.width() != target_w || current_img.height() != target_h {
        writeln!(log, "  [Final] Resizing from {}x{} to target {}x{}", current_img.width(), current_img.height(), target_w, target_h)?;
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
    }
    
    Ok(())
}

fn process_upscale_gif(
    input_path: &str, 
    output_path: &str, 
    model_name: &str,
    scale_factor: i32,
    vibrancy: f32,
    sharpness: f32,
    depixelate: f32
) -> anyhow::Result<()> {
    use std::io::Write;
    use image::codecs::gif::{GifDecoder, GifEncoder, Repeat};
    use image::{AnimationDecoder, Frame};
    use std::fs::File;

    let mut log = File::create("scarl_debug.log")?;
    writeln!(log, "Starting GIF upscale: input={}", input_path)?;

    let file = File::open(input_path)?;
    let decoder = GifDecoder::new(file)?;
    let frames = decoder.into_frames();
    
    let mut engine = UpscaleEngine::new(model_name)?;
    
    let out_file = File::create(output_path)?;
    let mut encoder = GifEncoder::new(out_file);
    encoder.set_repeat(Repeat::Infinite)?;

    let tiler = Tiler::new(512, 32);

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

        let (orig_w, orig_h) = img.dimensions();
        
        // Recursive Upscale Loop for GIF Frame
        let mut current_img = img;
        let mut current_scale = 1;
        let target_scale = scale_factor as u32;
        
        while current_scale < target_scale {
            let (w, h) = current_img.dimensions();
            let tiles = tiler.split(&current_img);
            let mut upscaled_tiles = Vec::new();
            
            for tile in tiles {
                let (input_tensor, alpha_mask) = image_to_array(&tile.data);
                let output_tensor = engine.predict(input_tensor)?;
                let mut upscaled_img = array_to_image(output_tensor);
                
                if let Some(mask) = alpha_mask {
                    let scaled_mask = image::imageops::resize(&mask, upscaled_img.width(), upscaled_img.height(), image::imageops::FilterType::CatmullRom);
                    for y in 0..upscaled_img.height() {
                        for x in 0..upscaled_img.width() {
                            let mut p = upscaled_img.get_pixel(x, y).clone();
                            p[3] = scaled_mask.get_pixel(x, y)[0];
                            image::GenericImage::put_pixel(&mut upscaled_img, x, y, p);
                        }
                    }
                }
                
                upscaled_tiles.push(UpscaledTile {
                    x: tile.x,
                    y: tile.y,
                    data: upscaled_img,
                });
            }
            
            current_img = tiler.merge(upscaled_tiles, 4, w * 4, h * 4);
            current_scale *= 4;
        }
        
        let mut final_img = if current_scale != target_scale {
            current_img.resize_exact(orig_w * target_scale, orig_h * target_scale, image::imageops::FilterType::CatmullRom)
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
