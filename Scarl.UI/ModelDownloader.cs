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
            
            // Vision Models from public Hugging Face sources
            { "characters.txt", "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/selected_tags.csv" },
            { "clip_merges.txt", "https://huggingface.co/xplato/clip-vit-large-patch14-text-onnx/resolve/main/merges.txt" },
            { "clip_text.onnx", "https://huggingface.co/xplato/clip-vit-large-patch14-text-onnx/resolve/main/model.onnx" },
            { "clip_vision.onnx", "https://huggingface.co/xplato/clip-vit-large-patch14-vision-onnx/resolve/main/model.onnx" },
            { "clip_vocab.json", "https://huggingface.co/xplato/clip-vit-large-patch14-text-onnx/resolve/main/vocab.json" }
        };
        
        public static readonly string[] CoreModels = {
            "realesrgan-x4.onnx", "RealESRGAN_x4.onnx"
        };

        public static readonly string[] QualityModels = {
            "hat-x4.onnx", "realesrgan-x2.onnx", "realesrgan-x8.onnx", 
            "RealESRGAN_x2_fp16.onnx", "RealESRGAN_x8_fp16.onnx"
        };

        public static readonly string[] VisionModels = {
            "characters.txt", "clip_merges.txt", "clip_text.onnx",
            "clip_vision.onnx", "clip_vocab.json"
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

        public static async Task DownloadModels(IEnumerable<string> fileList, Action<double, string> progressCallback, System.Threading.CancellationToken cancellationToken = default)
        {
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(60);

            var files = new List<string>(fileList);
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = files[i];
                string filePath = Path.Combine(modelDir, fileName);
                if (File.Exists(filePath)) continue;

                string tempFilePath = filePath + ".tmp";
                string url = ModelUrls.TryGetValue(fileName, out var matchedUrl) ? matchedUrl : (ModelBaseUrl + fileName);
                
                int maxRetries = 3;
                int attempt = 0;
                bool success = false;

                while (attempt < maxRetries && !success)
                {
                    attempt++;
                    try
                    {
                        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength;
                        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int read;
                        double lastReportedFileProgress = -1;

                        while (true)
                        {
                            // Enforce a 30-second timeout for each read operation to prevent hung sockets
                            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readCts.Token);

                            read = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);
                            if (read <= 0) break;

                            await fs.WriteAsync(buffer, 0, read, cancellationToken);
                            totalRead += read;
                            if (totalBytes.HasValue)
                            {
                                double fileProgress = (double)totalRead / totalBytes.Value * 100;
                                if (fileProgress - lastReportedFileProgress >= 0.5 || fileProgress >= 100.0)
                                {
                                    lastReportedFileProgress = fileProgress;
                                    double overallProgress = ((double)i / files.Count * 100) + (fileProgress / files.Count);
                                    progressCallback(overallProgress, $"Downloading {fileName} ({fileProgress:F1}%)");
                                }
                            }
                            else
                            {
                                progressCallback((double)i / files.Count * 100, $"Downloading {fileName}...");
                            }
                        }

                        fs.Close();

                        if (fileName == "characters.txt")
                        {
                            var csvLines = File.ReadAllLines(tempFilePath);
                            var parsedChars = new List<string>();
                            foreach (var line in csvLines)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                var parts = line.Split(',');
                                // Column indices: tag_id (0), name (1), category (2), count (3)
                                // We filter for category '4' which contains Danbooru character tags
                                if (parts.Length >= 3 && parts[2].Trim() == "4")
                                {
                                    parsedChars.Add(parts[1].Trim());
                                }
                            }

                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                            File.WriteAllLines(filePath, parsedChars);
                            File.Delete(tempFilePath);
                        }
                        else
                        {
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                            File.Move(tempFilePath, filePath);
                        }

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        if (File.Exists(tempFilePath))
                        {
                            try { File.Delete(tempFilePath); } catch { }
                        }

                        if (attempt >= maxRetries)
                        {
                            throw new Exception($"Failed to download {fileName} after {maxRetries} attempts. Last error: {ex.Message}", ex);
                        }

                        progressCallback((double)i / files.Count * 100, $"Download of {fileName} stalled. Retrying attempt {attempt + 1}/{maxRetries}...");
                        await Task.Delay(2000, cancellationToken);
                    }
                }
            }
            progressCallback(100, "Setup ready!");
        }
    }
}
