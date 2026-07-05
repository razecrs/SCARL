using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Scarl.UI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        public static void RunCliDirect(string[] args)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                AttachConsole(-1); // Attach to parent cmd/powershell process console
            }
            Console.WriteLine(); // Ensure print aligns nicely

            string input = "";
            string output = "";
            string model = "models/realesrgan-x4.onnx";
            int width = 0;
            int height = 0;
            float vibrancy = 0.0f;
            float sharpness = 0.0f;
            float depixelate = 0.0f;
            int preset = 0;

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-i" || args[i] == "--input") && i + 1 < args.Length) input = args[++i];
                else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length) output = args[++i];
                else if ((args[i] == "-m" || args[i] == "--model") && i + 1 < args.Length) model = args[++i];
                else if ((args[i] == "-w" || args[i] == "--width") && i + 1 < args.Length) int.TryParse(args[++i], out width);
                else if ((args[i] == "-h" || args[i] == "--height") && i + 1 < args.Length) int.TryParse(args[++i], out height);
                else if ((args[i] == "-v" || args[i] == "--vibrancy") && i + 1 < args.Length) float.TryParse(args[++i], out vibrancy);
                else if (args[i] == "--sharpness" && i + 1 < args.Length) float.TryParse(args[++i], out sharpness);
                else if (args[i] == "--depixelate" && i + 1 < args.Length) float.TryParse(args[++i], out depixelate);
                else if (args[i] == "--preset" && i + 1 < args.Length) int.TryParse(args[++i], out preset);
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("\n[SCARL Error] Input path is required. Use -i or --input.");
                Environment.Exit(1);
                return;
            }

            string fullModelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, model));
            if (!File.Exists(fullModelPath))
            {
                Console.WriteLine($"\n[SCARL Error] Model file not found at: {fullModelPath}");
                Environment.Exit(1);
                return;
            }

            // Check if input is a directory (Batch Mode)
            if (Directory.Exists(input))
            {
                RunDirectoryBatch(input, output, fullModelPath, width, height, vibrancy, sharpness, depixelate, preset);
                return;
            }

            // Otherwise, run in Single Image mode
            if (!File.Exists(input))
            {
                Console.WriteLine($"\n[SCARL Error] Input file not found: {input}");
                Environment.Exit(1);
                return;
            }

            if (string.IsNullOrEmpty(output))
            {
                string dir = Path.GetDirectoryName(input) ?? "";
                string name = Path.GetFileNameWithoutExtension(input);
                string ext = Path.GetExtension(input);
                output = Path.Combine(dir, $"{name}_upscaled{ext}");
            }

            Console.WriteLine($"\nSCARL CLI - Execution Triggered:");
            Console.WriteLine($"  Input Image:  {input}");
            Console.WriteLine($"  Output Image: {output}");
            Console.WriteLine($"  AI Model:     {model}");
            Console.WriteLine($"  Preset Mode:  {preset}");
            Console.WriteLine($"  Dimensions:   {(width > 0 ? width.ToString() : "Auto")}x{(height > 0 ? height.ToString() : "Auto")}");

            bool success = CoreEngine.RunUpscale(input, output, fullModelPath, width, height, vibrancy, sharpness, depixelate, preset);
            if (success)
            {
                Console.WriteLine("\n[SCARL Success] Image upscale completed!");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("\n[SCARL Error] Upscale run failed.");
                Environment.Exit(1);
            }
        }

        private static void RunDirectoryBatch(string inputDir, string outputDir, string modelPath, int width, int height, float vibrancy, float sharpness, float depixelate, int preset)
        {
            Console.WriteLine($"\nSCARL CLI - Directory Batch Mode Triggered:");
            Console.WriteLine($"  Input Directory:  {inputDir}");
            Console.WriteLine($"  AI Model:         {modelPath}");
            Console.WriteLine($"  Preset Mode:      {preset}");
            Console.WriteLine($"  Dimensions:       {(width > 0 ? width.ToString() : "Auto")}x{(height > 0 ? height.ToString() : "Auto")}");

            // Find all supported images
            var exts = new[] { "*.png", "*.jpg", "*.jpeg" };
            var files = new List<string>();
            foreach (var ext in exts)
            {
                files.AddRange(Directory.GetFiles(inputDir, ext, SearchOption.TopDirectoryOnly));
            }

            if (files.Count == 0)
            {
                Console.WriteLine("\n[SCARL Error] No supported images (*.png, *.jpg, *.jpeg) found in the input directory.");
                Environment.Exit(1);
                return;
            }

            Console.WriteLine($"  Found {files.Count} images to upscale.");

            // Resolve output directory
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(inputDir, "upscaled");
            }
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
            Console.WriteLine($"  Output Directory: {outputDir}\n");

            int successCount = 0;
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                string name = Path.GetFileName(file);
                string outFile = Path.Combine(outputDir, name);

                Console.WriteLine($"[{i + 1}/{files.Count}] Upscaling {name}...");
                bool ok = CoreEngine.RunUpscale(file, outFile, modelPath, width, height, vibrancy, sharpness, depixelate, preset);
                if (ok)
                {
                    Console.WriteLine($"  -> Success: {outFile}");
                    successCount++;
                }
                else
                {
                    Console.WriteLine($"  -> Failed to upscale {name}");
                }
            }

            Console.WriteLine($"\n[SCARL Batch Done] Upscaled {successCount}/{files.Count} images successfully.");
            Environment.Exit(successCount == files.Count ? 0 : 1);
        }
    }
}
