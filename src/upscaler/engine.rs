use ort::session::Session;
use image::DynamicImage;
use ndarray::Array4;
use std::io::Write;

pub struct UpscaleEngine {
    session: Session,
    output_name: String,
    input_name: String,
    is_f16: bool,
    model_path: String,
    use_cpu: bool,
}

impl UpscaleEngine {
    pub fn new(model_path: &str) -> anyhow::Result<Self> {
        let (session, use_cpu) = match Session::builder()
            .map_err(|e| anyhow::anyhow!("Failed to create session builder: {}", e))?
            .with_execution_providers([ort::execution_providers::DirectMLExecutionProvider::default().build()])
            .map_err(|e| anyhow::anyhow!("Failed to configure DML: {}", e))?
            .commit_from_file(model_path)
        {
            Ok(s) => (s, false),
            Err(e) => {
                if let Ok(mut log) = std::fs::OpenOptions::new().append(true).create(true).open("scarl_inference.log") {
                    let _ = writeln!(log, "DirectML session creation failed, falling back to CPU: {}", e);
                }
                let s = Session::builder()
                    .map_err(|e| anyhow::anyhow!("Failed to create session builder: {}", e))?
                    .commit_from_file(model_path)
                    .map_err(|e| anyhow::anyhow!("Failed to load model on CPU {}: {}", model_path, e))?;
                (s, true)
            }
        };

        let output_name = session.outputs()[0].name().to_string();
        let input_name = session.inputs()[0].name().to_string();

        let is_f16 = match session.inputs()[0].dtype() {
            ort::value::ValueType::Tensor { ty, .. } => *ty == ort::value::TensorElementType::Float16,
            _ => false,
        };

        Ok(Self {
            session,
            output_name,
            input_name,
            is_f16,
            model_path: model_path.to_string(),
            use_cpu,
        })
    }

    fn fallback_to_cpu(&mut self) -> anyhow::Result<()> {
        if let Ok(mut log) = std::fs::OpenOptions::new().append(true).create(true).open("scarl_inference.log") {
            let _ = writeln!(log, "Inference failed or device reset. Falling back to CPU session...");
        }
        self.session = Session::builder()
            .map_err(|e| anyhow::anyhow!("Failed to create session builder: {}", e))?
            .commit_from_file(&self.model_path)
            .map_err(|e| anyhow::anyhow!("Failed to load model on CPU {}: {}", self.model_path, e))?;
        self.use_cpu = true;
        Ok(())
    }

    pub fn upscale(&mut self, img: DynamicImage, _scale: u32) -> anyhow::Result<DynamicImage> {
        let mut log = std::fs::OpenOptions::new().append(true).create(true).open("scarl_inference.log")?;
        
        // 1. Convert to Array
        writeln!(log, "  Converting image to array...")?;
        let (input_array, _alpha) = crate::utils::image_to_array(&img);
        
        let array_f32 = self.predict(input_array)?;
        
        // 4. Convert back to Image
        let upscaled_img = crate::utils::array_to_image(array_f32);
        
        Ok(upscaled_img)
    }

    pub fn predict(&mut self, input_array: Array4<f32>) -> anyhow::Result<Array4<f32>> {
        let mut log = std::fs::OpenOptions::new().append(true).create(true).open("scarl_inference.log")?;
        
        if self.is_f16 {
            let input_array_f16 = input_array.mapv(half::f16::from_f32);
            writeln!(log, "  Creating f16 tensor from array (shape: {:?})...", input_array_f16.dim())?;
            let tensor = ort::value::Tensor::from_array(input_array_f16.clone())?;
            
            writeln!(log, "  Running inference for tile (f16)...")?;
            let inputs = ort::inputs![self.input_name.as_str() => tensor];
            let mut run_result = self.session.run(inputs);
            if run_result.is_err() {
                if !self.use_cpu {
                    drop(run_result);
                    self.fallback_to_cpu()?;
                    let tensor = ort::value::Tensor::from_array(input_array_f16)?;
                    let inputs = ort::inputs![self.input_name.as_str() => tensor];
                    run_result = self.session.run(inputs);
                }
            }
            let outputs = run_result?;
            writeln!(log, "  Inference successful")?;
            
            let (shape, data) = outputs[self.output_name.as_str()].try_extract_tensor::<half::f16>()?;
            let shape_vec: Vec<usize> = shape.iter().map(|&x| x as usize).collect();
            writeln!(log, "  Output shape: {:?}", shape_vec)?;
            
            let array_f16 = Array4::from_shape_vec((shape_vec[0], shape_vec[1], shape_vec[2], shape_vec[3]), data.to_vec())
                .map_err(|e| anyhow::anyhow!("Failed to reconstruct array: {}", e))?;
            let array_f32 = array_f16.mapv(|x| x.to_f32());
                
            Ok(array_f32)
        } else {
            writeln!(log, "  Creating f32 tensor from array (shape: {:?})...", input_array.dim())?;
            let tensor = ort::value::Tensor::from_array(input_array.clone())?;
            
            writeln!(log, "  Running inference for tile (f32)...")?;
            let inputs = ort::inputs![self.input_name.as_str() => tensor];
            let mut run_result = self.session.run(inputs);
            if run_result.is_err() {
                if !self.use_cpu {
                    drop(run_result);
                    self.fallback_to_cpu()?;
                    let tensor = ort::value::Tensor::from_array(input_array)?;
                    let inputs = ort::inputs![self.input_name.as_str() => tensor];
                    run_result = self.session.run(inputs);
                }
            }
            let outputs = run_result?;
            writeln!(log, "  Inference successful")?;
            
            let (shape, data) = outputs[self.output_name.as_str()].try_extract_tensor::<f32>()?;
            let shape_vec: Vec<usize> = shape.iter().map(|&x| x as usize).collect();
            writeln!(log, "  Output shape: {:?}", shape_vec)?;
            
            let array_f32 = Array4::from_shape_vec((shape_vec[0], shape_vec[1], shape_vec[2], shape_vec[3]), data.to_vec())
                .map_err(|e| anyhow::anyhow!("Failed to reconstruct array: {}", e))?;
                
            Ok(array_f32)
        }
    }
}
