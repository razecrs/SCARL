using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Scarl.UI
{
    public static class ModelDownloader
    {
        private static readonly string ModelBaseUrl = "https://github.com/razecrs/SCARL/releases/download/v1.0.0/";

        private static readonly Dictionary<string, string> ModelUrls = new()
        {
            { "realesrgan-x4.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/realesrgan-x4.onnx" },
            { "RealESRGAN_x4.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/RealESRGAN_x4.onnx" },
            { "hat-x4.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/hat-x4.onnx" },
            { "realesrgan-x2.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/realesrgan-x2.onnx" },
            { "realesrgan-x8.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/realesrgan-x8.onnx" },
            { "RealESRGAN_x2_fp16.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/RealESRGAN_x2_fp16.onnx" },
            { "RealESRGAN_x8_fp16.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/RealESRGAN_x8_fp16.onnx" },
            
            // Vision Models
            { "characters.txt", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/characters.txt" },
            { "classifier.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/classifier.onnx" },
            { "clip_merges.txt", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/clip_merges.txt" },
            { "clip_text.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/clip_text.onnx" },
            { "clip_vision.onnx", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/clip_vision.onnx" },
            { "clip_vocab.json", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/clip_vocab.json" },
            { "imagenet_labels.txt", "https://github.com/razecrs/SCARL/releases/download/v1.0.0/imagenet_labels.txt" }
        };
        
        public static readonly string[] CoreModels = {
            "realesrgan-x4.onnx", "RealESRGAN_x4.onnx"
        };

        public static readonly string[] QualityModels = {
            "hat-x4.onnx", "realesrgan-x2.onnx", "realesrgan-x8.onnx", 
            "RealESRGAN_x2_fp16.onnx", "RealESRGAN_x8_fp16.onnx"
        };

        public static readonly string[] VisionModels = {
            "characters.txt", "classifier.onnx", "clip_merges.txt", "clip_text.onnx",
            "clip_vision.onnx", "clip_vocab.json", "imagenet_labels.txt"
        };

        public static bool ModelsExist(string[] fileList)
        {
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (!Directory.Exists(modelDir)) return false;
            foreach (var file in fileList)
            {
                if (!File.Exists(Path.Combine(modelDir, file))) return false;
            }
            return true;
        }

        public static async Task DownloadModels(IEnumerable<string> fileList, Action<double, string> progressCallback)
        {
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(60);

            var files = new List<string>(fileList);
            for (int i = 0; i < files.Count; i++)
            {
                string fileName = files[i];
                string filePath = Path.Combine(modelDir, fileName);
                if (File.Exists(filePath)) continue;

                string url = ModelUrls.TryGetValue(fileName, out var matchedUrl) ? matchedUrl : (ModelBaseUrl + fileName);
                
                try
                {
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalBytes.HasValue)
                        {
                            double fileProgress = (double)totalRead / totalBytes.Value * 100;
                            double overallProgress = ((double)i / files.Count * 100) + (fileProgress / files.Count);
                            progressCallback(overallProgress, $"Downloading {fileName} ({fileProgress:F1}%)");
                        }
                        else
                        {
                            progressCallback((double)i / files.Count * 100, $"Downloading {fileName}...");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (File.Exists(filePath))
                    {
                        try { File.Delete(filePath); } catch { }
                    }
                    throw new Exception($"Failed to download {fileName}: {ex.Message}");
                }
            }
            progressCallback(100, "Setup ready!");
        }
    }
}
