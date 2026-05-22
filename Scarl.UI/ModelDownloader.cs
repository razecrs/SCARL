using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace Scarl.UI
{
    public static class ModelDownloader
    {
        private static readonly string ModelBaseUrl = "https://github.com/razecrs/SCARL/releases/download/v1.0.0/";
        
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

                progressCallback((double)i / files.Count * 100, $"Downloading {fileName}...");

                try
                {
                    var response = await client.GetAsync(ModelBaseUrl + fileName, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to download {fileName}: {ex.Message}");
                }
            }
            progressCallback(100, "Setup ready!");
        }
    }
}
