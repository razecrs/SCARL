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
        private static readonly string ModelBaseUrl = "https://huggingface.co/skytnt/anime-tagger/resolve/main/";
        private static readonly string[] ModelFiles = {
            "characters.txt", "classifier.onnx", "clip_merges.txt", "clip_text.onnx",
            "clip_vision.onnx", "clip_vocab.json", "hat-x4.onnx", "imagenet_labels.txt",
            "RealESRGAN_x2_fp16.onnx", "RealESRGAN_x4.onnx", "RealESRGAN_x8_fp16.onnx",
            "realesrgan-x2.onnx", "realesrgan-x4.onnx", "realesrgan-x8.onnx"
        };

        public static bool ModelsExist()
        {
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (!Directory.Exists(modelDir)) return false;
            foreach (var file in ModelFiles)
            {
                if (!File.Exists(Path.Combine(modelDir, file))) return false;
            }
            return true;
        }

        public static async Task DownloadModels(Action<double, string> progressCallback)
        {
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(60); // 1 hour timeout for massive models

            for (int i = 0; i < ModelFiles.Length; i++)
            {
                string fileName = ModelFiles[i];
                string filePath = Path.Combine(modelDir, fileName);
                if (File.Exists(filePath)) continue;

                progressCallback((double)i / ModelFiles.Length * 100, $"Downloading {fileName}...");

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
            progressCallback(100, "All models ready!");
        }
    }
}
