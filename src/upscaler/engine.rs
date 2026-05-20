use ort::session::Session;
use image::DynamicImage;
use ndarray::Array4;

pub struct UpscaleEngine {
    session: Session,
    output_name: String,
    input_name: String,
}

impl UpscaleEngine {
    pub fn new(model_path: &str) -> anyhow::Result<Self> {
        let session = Session::builder()
            .map_err(|e| anyhow::anyhow!("Failed to create session builder: {}", e))?
            .with_execution_providers([ort::execution_providers::DirectMLExecutionProvider::default().build()])
            .map_err(|e| anyhow::anyhow!("Failed to configure DML: {}", e))?
            .commit_from_file(model_path)
            .map_err(|e| anyhow::anyhow!("Failed to load model {}: {}", model_path, e))?;

        let output_name = session.outputs()[0].name().to_string();
        let input_name = session.inputs()[0].name().to_string();

        Ok(Self { session, output_name, input_name })
    }

    pub fn upscale(&mut self, img: DynamicImage, _scale: u32) -> anyhow::Result<DynamicImage> {
        use std::io::Write;
        let mut log = std::fs::OpenOptions::new().append(true).create(true).open("scarl_inference.log")?;
        
        // 1. Convert to Array
        writeln!(log, "  Converting image to array...")?;
        let (input_array, _alpha) = crate::utils::image_to_array(&img);
        let input_array_f16 = input_array.mapv(half::f16::from_f32);
        writeln!(log, "  Creating f16 tensor from array (shape: {:?})...", input_array_f16.dim())?;
        let tensor = ort::value::Tensor::from_array(input_array_f16)?;
        
        // 2. Run Inference
        writeln!(log, "  Running inference for tile...")?;
        let inputs = ort::inputs![self.input_name.as_str() => tensor];
        let outputs = self.session.run(inputs)?;
        writeln!(log, "  Inference successful")?;
        
        // 3. Extract Result
        let (shape, data) = outputs[self.output_name.as_str()].try_extract_tensor::<half::f16>()?;
        let shape_vec: Vec<usize> = shape.iter().map(|&x| x as usize).collect();
        writeln!(log, "  Output shape: {:?}", shape_vec)?;
        
        let array_f16 = Array4::from_shape_vec((shape_vec[0], shape_vec[1], shape_vec[2], shape_vec[3]), data.to_vec())
            .map_err(|e| anyhow::anyhow!("Failed to reconstruct array: {}", e))?;
        let array_f32 = array_f16.mapv(|x| x.to_f32());
        
        // 4. Convert back to Image
        let upscaled_img = crate::utils::array_to_image(array_f32);
        
        Ok(upscaled_img)
    }

    pub fn predict(&mut self, input_array: Array4<f32>) -> anyhow::Result<Array4<f32>> {
        use std::io::Write;
        let mut log = std::fs::OpenOptions::new().append(true).create(true).open("scarl_inference.log")?;
        
        let input_array_f16 = input_array.mapv(half::f16::from_f32);
        writeln!(log, "  Creating f16 tensor from array (shape: {:?})...", input_array_f16.dim())?;
        let tensor = ort::value::Tensor::from_array(input_array_f16)?;
        
        writeln!(log, "  Running inference for tile...")?;
        let inputs = ort::inputs![self.input_name.as_str() => tensor];
        let outputs = self.session.run(inputs)?;
        writeln!(log, "  Inference successful")?;
        
        let (shape, data) = outputs[self.output_name.as_str()].try_extract_tensor::<half::f16>()?;
        let shape_vec: Vec<usize> = shape.iter().map(|&x| x as usize).collect();
        writeln!(log, "  Output shape: {:?}", shape_vec)?;
        
        let array_f16 = Array4::from_shape_vec((shape_vec[0], shape_vec[1], shape_vec[2], shape_vec[3]), data.to_vec())
            .map_err(|e| anyhow::anyhow!("Failed to reconstruct array: {}", e))?;
        let array_f32 = array_f16.mapv(|x| x.to_f32());
            
        Ok(array_f32)
    }
}
