using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Scarl.UI
{
    public partial class MainWindow : Window
    {
        private enum AppState
        {
            Select,
            ReadyToUpscale,
            Upscaling,
            Completed
        }

        private AppState _currentState = AppState.Select;
        private string? _selectedImagePath;
        private string? _outputPath;
        private AppSettings _settings;
        private System.Threading.CancellationTokenSource? _downloadCts;
        private TaskCompletionSource<bool>? _dialogTcs;

        public MainWindow()
        {
            InitializeComponent();
            try {
                _settings = AppSettings.Load();
            } catch {
                _settings = new AppSettings();
            }
            ApplySettings();
            UpdateUiState();
            this.Loaded += (s, e) => {
                try { EnableBlur(); } catch { }
            };
        }

        private void EnableBlur()
        {
            this.TransparencyLevelHint = _settings.GlassIntensity > 0 
                ? new[] { WindowTransparencyLevel.AcrylicBlur } 
                : new[] { WindowTransparencyLevel.None };
        }

        private void ApplySettings()
        {
            this.Width = _settings.WindowWidth;
            this.Height = _settings.WindowHeight;
            SaveFolderBox.Text = _settings.DefaultSaveFolder ?? "Original Folder";
            
            GlassSlider.Value = _settings.GlassIntensity;
            GlassText.Text = $"{(int)_settings.GlassIntensity}%";

            // Set index based on theme name
            ThemeSelector.SelectedIndex = _settings.Theme switch
            {
                "Blue" => 1,
                "Green" => 2,
                "Gold" => 3,
                _ => 0
            };
            ApplyTheme();
        }

        private void UpdateUiState()
        {
            switch (_currentState)
            {
                case AppState.Select:
                    ActionButton.Content = "SELECT IMAGE";
                    ActionButton.IsEnabled = true;
                    ResetButton.IsVisible = false;
                    VibrancySlider.IsEnabled = true;
                    SharpnessSlider.IsEnabled = true;
                    DepixelateSlider.IsEnabled = true;
                    StatusText.Text = "READY";
                    if (_settings.Theme == "Red")
                    {
                        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00));
                    }
                    HelpText.Text = "READY TO ENHANCE";
                    PlaceholderText.IsVisible = true;
                    ImagePreview.Source = null;
                    ProcessingBar.IsVisible = false;
                    break;

                case AppState.ReadyToUpscale:
                    ActionButton.Content = "START RECONSTRUCTION";
                    ActionButton.IsEnabled = true;
                    ResetButton.Content = "RESET";
                    ResetButton.IsVisible = true;
                    VibrancySlider.IsEnabled = true;
                    SharpnessSlider.IsEnabled = true;
                    DepixelateSlider.IsEnabled = true;
                    StatusText.Text = "IMAGE LOADED";
                    StatusText.Foreground = new SolidColorBrush(Colors.White);
                    HelpText.Text = "ADJUST SETTINGS AND START RECONSTRUCTION";
                    PlaceholderText.IsVisible = false;
                    ProcessingBar.IsVisible = false;
                    if (!string.IsNullOrEmpty(_selectedImagePath) && File.Exists(_selectedImagePath))
                    {
                        try
                        {
                            ImagePreview.Source = LoadPreviewImage(_selectedImagePath, 1200);
                        }
                        catch (Exception ex)
                        {
                            _ = ShowDialogAsync("Preview Error", $"Failed to load preview image: {ex.Message}");
                        }
                    }
                    break;

                case AppState.Upscaling:
                    ActionButton.IsEnabled = false;
                    ResetButton.IsVisible = false;
                    VibrancySlider.IsEnabled = false;
                    SharpnessSlider.IsEnabled = false;
                    DepixelateSlider.IsEnabled = false;
                    StatusText.Text = "RECONSTRUCTING...";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00));
                    HelpText.Text = "PROCESSING TILES ON DIRECTML GPU ENGINE...";
                    ProcessingBar.IsVisible = true;
                    ProcessingBar.IsIndeterminate = true;
                    break;

                case AppState.Completed:
                    ActionButton.Content = "OPEN IN EXPLORER";
                    ActionButton.IsEnabled = true;
                    ResetButton.Content = "NEW IMAGE";
                    ResetButton.IsVisible = true;
                    VibrancySlider.IsEnabled = true;
                    SharpnessSlider.IsEnabled = true;
                    DepixelateSlider.IsEnabled = true;
                    StatusText.Text = "RECONSTRUCTION COMPLETE";
                    StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
                    HelpText.Text = "IMAGE SUCCESSFULLY UPSCALED!";
                    PlaceholderText.IsVisible = false;
                    ProcessingBar.IsVisible = false;
                    if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
                    {
                        try
                        {
                            ImagePreview.Source = LoadPreviewImage(_outputPath, 1200);
                        }
                        catch (Exception ex)
                        {
                            _ = ShowDialogAsync("Preview Error", $"Failed to load result preview: {ex.Message}");
                        }
                    }
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _settings.WindowWidth = this.Bounds.Width;
            _settings.WindowHeight = this.Bounds.Height;
            _settings.Save();
            base.OnClosed(e);
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.IsVisible = true;
        private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.IsVisible = false;

        private void Models_Click(object sender, RoutedEventArgs e)
        {
            UpdateModelButtonsState();
            ModelsOverlay.IsVisible = true;
        }

        private void CloseModels_Click(object sender, RoutedEventArgs e)
        {
            _downloadCts?.Cancel();
            ModelsOverlay.IsVisible = false;
        }

        private void UpdateModelButtonsState()
        {
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            
            // Check RealESRGAN x4
            bool esrgan4Exists = File.Exists(Path.Combine(modelDir, "realesrgan-x4.onnx"));
            BtnDownloadEsrgan4.Content = esrgan4Exists ? "DOWNLOADED" : "DOWNLOAD";
            BtnDownloadEsrgan4.IsEnabled = !esrgan4Exists;

            // Check HAT x4
            bool hatExists = File.Exists(Path.Combine(modelDir, "hat-x4.onnx"));
            BtnDownloadHat.Content = hatExists ? "DOWNLOADED" : "DOWNLOAD";
            BtnDownloadHat.IsEnabled = !hatExists;

            // Check RealESRGAN x2
            bool esrgan2Exists = File.Exists(Path.Combine(modelDir, "realesrgan-x2.onnx"));
            BtnDownloadEsrgan2.Content = esrgan2Exists ? "DOWNLOADED" : "DOWNLOAD";
            BtnDownloadEsrgan2.IsEnabled = !esrgan2Exists;

            // Check RealESRGAN x8
            bool esrgan8Exists = File.Exists(Path.Combine(modelDir, "realesrgan-x8.onnx"));
            BtnDownloadEsrgan8.Content = esrgan8Exists ? "DOWNLOADED" : "DOWNLOAD";
            BtnDownloadEsrgan8.IsEnabled = !esrgan8Exists;

            // Check Vision
            bool visionExists = ModelDownloader.ModelsExist(ModelDownloader.VisionModels);
            BtnDownloadVision.Content = visionExists ? "DOWNLOADED" : "DOWNLOAD";
            BtnDownloadVision.IsEnabled = !visionExists;
        }

        private async void DownloadEsrgan4_Click(object sender, RoutedEventArgs e)
        {
            await RunOverlayDownload(new[] { "realesrgan-x4.onnx", "RealESRGAN_x4.onnx" });
        }

        private async void DownloadHat_Click(object sender, RoutedEventArgs e)
        {
            await RunOverlayDownload(new[] { "hat-x4.onnx" });
        }

        private async void DownloadEsrgan2_Click(object sender, RoutedEventArgs e)
        {
            await RunOverlayDownload(new[] { "realesrgan-x2.onnx", "RealESRGAN_x2_fp16.onnx" });
        }

        private async void DownloadEsrgan8_Click(object sender, RoutedEventArgs e)
        {
            await RunOverlayDownload(new[] { "realesrgan-x8.onnx", "RealESRGAN_x8_fp16.onnx" });
        }

        private async void DownloadVision_Click(object sender, RoutedEventArgs e)
        {
            await RunOverlayDownload(ModelDownloader.VisionModels);
        }

        private async Task RunOverlayDownload(string[] files)
        {
            _downloadCts?.Cancel();
            _downloadCts = new System.Threading.CancellationTokenSource();
            var cts = _downloadCts;

            ModelProgress.IsVisible = true;
            ModelStatusText.IsVisible = true;
            ModelProgress.Value = 0;
            ModelStatusText.Text = "Starting download...";

            // Disable all download buttons during download
            BtnDownloadEsrgan4.IsEnabled = false;
            BtnDownloadHat.IsEnabled = false;
            BtnDownloadEsrgan2.IsEnabled = false;
            BtnDownloadEsrgan8.IsEnabled = false;
            BtnDownloadVision.IsEnabled = false;

            try
            {
                await ModelDownloader.DownloadModels(files, (progress, msg) => {
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(() => {
                        ModelProgress.Value = progress;
                        ModelStatusText.Text = msg.ToUpper();
                    });
                }, cts.Token);
                await ShowDialogAsync("Success", "Model downloaded successfully!");
            }
            catch (OperationCanceledException)
            {
                await ShowDialogAsync("Cancelled", "Download cancelled.");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("Error", $"Download failed: {ex.Message}");
            }
            finally
            {
                if (_downloadCts == cts)
                {
                    _downloadCts = null;
                }
                cts.Dispose();
                ModelProgress.IsVisible = false;
                ModelStatusText.IsVisible = false;
                UpdateModelButtonsState();
            }
        }

        private static Bitmap LoadPreviewImage(string filePath, int decodeWidth)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var original = new Bitmap(stream);
                if (decodeWidth > 0 && original.PixelSize.Width > decodeWidth)
                {
                    int decodeHeight = (int)((double)original.PixelSize.Height / original.PixelSize.Width * decodeWidth);
                    return original.CreateScaledBitmap(new PixelSize(decodeWidth, decodeHeight), BitmapInterpolationMode.LowQuality);
                }
                return original;
            }
        }

        private void GlassSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_settings == null || GlassText == null) return;
            _settings.GlassIntensity = e.NewValue;
            GlassText.Text = $"{(int)e.NewValue}%";
            EnableBlur();
            ApplyTheme();
            _settings.Save();
        }

        private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Default Save Folder",
                AllowMultiple = false
            });

            if (folders != null && folders.Count > 0)
            {
                _settings.DefaultSaveFolder = folders[0].Path.LocalPath;
                SaveFolderBox.Text = _settings.DefaultSaveFolder;
                _settings.Save();
            }
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null || ThemeSelector == null) return;
            _settings.Theme = ThemeSelector.SelectedIndex switch
            {
                1 => "Blue",
                2 => "Green",
                3 => "Gold",
                _ => "Red"
            };
            ApplyTheme();
            _settings.Save();
        }

        private void ApplyTheme()
        {
            var color = _settings.Theme switch
            {
                "Blue" => Color.FromRgb(0x00, 0x80, 0xFF),
                "Green" => Color.FromRgb(0x32, 0xCD, 0x32),
                "Gold" => Color.FromRgb(0xFF, 0xD7, 0x00),
                _ => Color.FromRgb(0xFF, 0x24, 0x00)
            };

            var brush = new SolidColorBrush(color);

            // Apply to UI elements
            if (StatusText != null) StatusText.Foreground = brush;
            if (ProcessingBar != null) ProcessingBar.Foreground = brush;
            if (VibrancySlider != null) VibrancySlider.Foreground = brush;
            if (SharpnessSlider != null) SharpnessSlider.Foreground = brush;
            if (DepixelateSlider != null) DepixelateSlider.Foreground = brush;
            if (MultiplierSlider != null) MultiplierSlider.Foreground = brush;
            if (GlassSlider != null) GlassSlider.Foreground = brush;

            if (MainWindowBorder != null) 
            {
                MainWindowBorder.BorderBrush = brush;
                
                if (_settings.GlassIntensity <= 0)
                {
                    MainWindowBorder.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
                }
                else
                {
                    double opacity = 1.0 - (_settings.GlassIntensity / 100.0 * 0.8); 
                    MainWindowBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0x11, 0x11, 0x11));
                }
            }

            if (MainTitle != null) MainTitle.Foreground = brush;
            if (TitleBarText != null) TitleBarText.Foreground = brush;

            if (ActionButton != null) ActionButton.Background = brush;
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == AppState.Select)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Image File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Image files")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg" }
                        },
                        FilePickerFileTypes.All
                    }
                });

                if (files != null && files.Count > 0)
                {
                    string selectedPath = files[0].Path.LocalPath;
                    try
                    {
                        if (ImageHeaderHelper.TryGetDimensions(selectedPath, out int origW, out int origH))
                        {
                            int finalWidth, finalHeight;
                            if (UseCustomResolutionCheckbox.IsChecked == true)
                            {
                                if (!int.TryParse(CustomWidthInput.Text, out finalWidth) || finalWidth <= 0) finalWidth = 2000;
                                if (!int.TryParse(CustomHeightInput.Text, out finalHeight) || finalHeight <= 0) finalHeight = 3000;
                            }
                            else
                            {
                                int targetScale = GetTargetScale(origW, origH);
                                finalWidth = origW * targetScale;
                                finalHeight = origH * targetScale;
                            }

                            // Boundary check for extreme resolutions
                            if (finalWidth > 50000 || finalHeight > 50000)
                            {
                                await ShowDialogAsync(
                                    "Ceiling Reached",
                                    $"The requested resolution of {finalWidth}x{finalHeight} exceeds the absolute safety ceiling (50,000 px).\n\nPlease choose a lower resolution target."
                                );
                                return;
                            }
                        }
                        else
                        {
                            await ShowDialogAsync("Error", "Unsupported image format or invalid image file.");
                            return;
                        }

                        _selectedImagePath = selectedPath;
                        string saveDir = _settings.DefaultSaveFolder ?? Path.GetDirectoryName(_selectedImagePath) ?? "";
                        _outputPath = Path.Combine(saveDir, "scarl_" + Path.GetFileName(_selectedImagePath));
                        _currentState = AppState.ReadyToUpscale;
                        UpdateUiState();

                        // Run auto-detection in background
                        _ = RunAutoDetectAsync(selectedPath);
                    }
                    catch (Exception ex)
                    {
                        await ShowDialogAsync("Error", $"Failed to load image metadata: {ex.Message}");
                    }
                }
            }
            else if (_currentState == AppState.ReadyToUpscale)
            {
                if (string.IsNullOrEmpty(_selectedImagePath) || string.IsNullOrEmpty(_outputPath)) return;

                _currentState = AppState.Upscaling;
                UpdateUiState();

                float vibrancy = (float)VibrancySlider.Value / 50f; // Scale 0-100 to 0-2 (1.0 is default)
                float sharpness = (float)SharpnessSlider.Value / 20f; // Scale 0-100 to 0-5
                float depixelate = (float)DepixelateSlider.Value / 50f; // Scale 0-100 to 0-2 (Gaussian blur sigma)

                string input = _selectedImagePath;
                string output = _outputPath;

                string relativeModelPath;
                switch (ModelSelector.SelectedIndex)
                {
                    case 1:
                        relativeModelPath = "models/hat-x4.onnx";
                        break;
                    case 2:
                        relativeModelPath = "models/realesrgan-x2.onnx";
                        break;
                    case 3:
                        relativeModelPath = "models/realesrgan-x8.onnx";
                        break;
                    default:
                        relativeModelPath = "models/realesrgan-x4.onnx";
                        break;
                }

                string fullModelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeModelPath));
                if (!File.Exists(fullModelPath))
                {
                    await ShowDialogAsync(
                        "Model Missing",
                        $"The selected model ({Path.GetFileName(relativeModelPath)}) is not downloaded yet.\n\nPlease open the AI Model Manager (click the 🧠 icon in the title bar) to download it."
                    );
                    _currentState = AppState.ReadyToUpscale;
                    UpdateUiState();
                    return;
                }

                int presetMode = PresetSelector.SelectedIndex; // 0=Default, 1=Sticker, 2=GIF
                
                // If GIF mode is selected, ensure output has .gif extension
                if (presetMode == 2 && !output.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    output = System.IO.Path.ChangeExtension(output, ".gif");
                }
                // If Sticker or Discord Sticker mode is selected, ensure output has .png extension (required for alpha)
                else if ((presetMode == 1 || presetMode == 3) && !output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    output = System.IO.Path.ChangeExtension(output, ".png");
                }

                int targetW = 0, targetH = 0;
                try {
                    if (UseCustomResolutionCheckbox.IsChecked == true)
                    {
                        if (!int.TryParse(CustomWidthInput.Text, out targetW) || targetW <= 0) targetW = 2000;
                        if (!int.TryParse(CustomHeightInput.Text, out targetH) || targetH <= 0) targetH = 3000;
                    }
                    else
                    {
                        if (ImageHeaderHelper.TryGetDimensions(input, out int origW, out int origH))
                        {
                            int index = (int)Math.Round(MultiplierSlider.Value);
                            int targetRes = _resolutionSteps[index];
                            
                            float scale = (float)targetRes / Math.Max(origW, origH);
                            targetW = (int)(origW * scale);
                            targetH = (int)(origH * scale);
                        }
                    }
                } catch {}

                bool success = await Task.Run(() => CoreEngine.RunUpscale(input, output, fullModelPath, targetW, targetH, vibrancy, sharpness, depixelate, presetMode));

                if (success)
                {
                    if (!File.Exists(_outputPath))
                    {
                        string jpgPath = Path.ChangeExtension(_outputPath, ".jpg");
                        if (File.Exists(jpgPath))
                        {
                            _outputPath = jpgPath;
                        }
                    }
                    _currentState = AppState.Completed;
                }
                else
                {
                    string debugLog = "No debug log found.";
                    if (File.Exists("scarl_debug.log"))
                    {
                        debugLog = File.ReadAllText("scarl_debug.log");
                    }
                    if (File.Exists("scarl_inference.log"))
                    {
                        debugLog += "\n--- Inference Log ---\n" + File.ReadAllText("scarl_inference.log");
                    }

                    _currentState = AppState.ReadyToUpscale;
                    UpdateUiState();
                    
                    StatusText.Text = "RECONSTRUCTION FAILED";
                    StatusText.Foreground = new SolidColorBrush(Colors.Red);
                    await ShowDialogAsync("Error", $"Upscaling failed.\n\nDebug Log:\n{debugLog}");
                }
                UpdateUiState();
            }
            else if (_currentState == AppState.Completed)
            {
                if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
                {
                    try
                    {
                        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_outputPath}\"");
                        }
                        else
                        {
                            // On Linux we can open the directory
                            string? dir = Path.GetDirectoryName(_outputPath);
                            if (dir != null)
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "xdg-open",
                                    Arguments = $"\"{dir}\"",
                                    UseShellExecute = true
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await ShowDialogAsync("Error", $"Failed to open directory: {ex.Message}");
                    }
                }
            }
        }

        private async Task RunAutoDetectAsync(string imagePath)
        {
            StatusText.Text = "ANALYZING...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));

            AnalysisResult result;
            try
            {
                result = await ImageAnalyzer.AnalyzeAsync(imagePath);
            }
            catch
            {
                StatusText.Text = "IMAGE LOADED";
                StatusText.Foreground = new SolidColorBrush(Colors.White);
                return;
            }

            StatusText.Text = "IMAGE LOADED";
            StatusText.Foreground = new SolidColorBrush(Colors.White);

            if (!result.IsPixelArt && !result.HasCharacter) return;

            string dialogTitle = result.IsPixelArt && result.HasCharacter
                ? "⚠  PIXEL ART + CHARACTER DETECTED"
                : result.IsPixelArt
                    ? "⚠  PIXEL ART DETECTED"
                    : "👤  CHARACTER DETECTED";

            bool userChoice = await ShowDialogAsync(
                dialogTitle,
                result.Description,
                $"Blockiness: {result.BlockinessScore:P0}  ·  Colours: {result.UniqueColors}  ·  Suggested DE-PIXELATE: {result.RecommendedDepixelate}",
                !string.IsNullOrEmpty(result.CharacterInfo) ? (result.HasCharacter ? "👤  " : "○  ") + result.CharacterInfo : null,
                isConfirm: true,
                yesText: "YES — APPLY FIX",
                noText: "NO — NORMAL"
            );

            if (userChoice)
            {
                DepixelateSlider.Value = result.RecommendedDepixelate;
                HelpText.Text = $"DE-PIXELATE set to {result.RecommendedDepixelate} — ready to reconstruct!";
                StatusText.Text = "PIXEL FIX READY";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
            }
        }

        private Task<bool> ShowDialogAsync(string title, string message, string? stats = null, string? charInfo = null, bool isConfirm = false, string yesText = "YES", string noText = "NO", string okText = "OK")
        {
            _dialogTcs = new TaskCompletionSource<bool>();

            DialogTitleText.Text = title.ToUpper();
            DialogMessageText.Text = message;
            
            if (!string.IsNullOrEmpty(stats))
            {
                DialogStatsText.Text = stats;
                DialogStatsText.IsVisible = true;
            }
            else
            {
                DialogStatsText.IsVisible = false;
            }

            if (!string.IsNullOrEmpty(charInfo))
            {
                DialogCharInfoText.Text = charInfo;
                DialogCharInfoText.IsVisible = true;
            }
            else
            {
                DialogCharInfoText.IsVisible = false;
            }

            if (isConfirm)
            {
                BtnDialogYes.Content = yesText.ToUpper();
                BtnDialogNo.Content = noText.ToUpper();
                BtnDialogYes.IsVisible = true;
                BtnDialogNo.IsVisible = true;
                BtnDialogOk.IsVisible = false;
            }
            else
            {
                BtnDialogOk.Content = okText.ToUpper();
                BtnDialogYes.IsVisible = false;
                BtnDialogNo.IsVisible = false;
                BtnDialogOk.IsVisible = true;
            }

            DialogOverlay.IsVisible = true;
            return _dialogTcs.Task;
        }

        private void DialogOk_Click(object sender, RoutedEventArgs e)
        {
            DialogOverlay.IsVisible = false;
            _dialogTcs?.TrySetResult(true);
        }

        private void DialogYes_Click(object sender, RoutedEventArgs e)
        {
            DialogOverlay.IsVisible = false;
            _dialogTcs?.TrySetResult(true);
        }

        private void DialogNo_Click(object sender, RoutedEventArgs e)
        {
            DialogOverlay.IsVisible = false;
            _dialogTcs?.TrySetResult(false);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedImagePath = null;
            _outputPath = null;
            _currentState = AppState.Select;
            UpdateUiState();
        }

        private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            this.BeginMoveDrag(e);
        }

        private void DepixelateSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Future real-time preview logic
        }

        private void PresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetSelector == null) return;
            
            if (PresetSelector.SelectedIndex == 1 || PresetSelector.SelectedIndex == 3) // Sticker Mode or Discord Sticker Mode
            {
                VibrancySlider.Value = 80;
                SharpnessSlider.Value = 70;
                DepixelateSlider.Value = 10;
            }
        }

        private readonly int[] _resolutionSteps = { 2000, 4000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 25000, 30000, 35000, 40000 };

        private void MultiplierSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (MultiplierText != null && _resolutionSteps != null)
            {
                int index = (int)Math.Round(e.NewValue);
                if (index >= 0 && index < _resolutionSteps.Length)
                {
                    int res = _resolutionSteps[index];
                    MultiplierText.Text = res >= 1000 ? $"{res / 1000}K" : res.ToString();
                }
            }
        }

        private int GetTargetScale(int originalWidth, int originalHeight)
        {
            int index = (int)Math.Round(MultiplierSlider.Value);
            int targetRes = _resolutionSteps[index];
            
            int maxDim = Math.Max(originalWidth, originalHeight);
            float scale = (float)targetRes / maxDim;
            
            return (int)Math.Max(1, Math.Ceiling(scale));
        }

        private void UseCustomResolutionCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (CustomResolutionPanel != null)
            {
                CustomResolutionPanel.IsVisible = UseCustomResolutionCheckbox.IsChecked == true;
            }
        }

        private void NumberValidationTextBox(object sender, TextInputEventArgs e)
        {
            if (e.Text == null) return;
            var regex = new System.Text.RegularExpressions.Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
