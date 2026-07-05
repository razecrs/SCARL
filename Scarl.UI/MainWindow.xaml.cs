using System;
using System.Windows;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;

using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Scarl.UI
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

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
            var windowHelper = new WindowInteropHelper(this);
            
            // ABGR format: Alpha Blue Green Red
            // Higher intensity = lower alpha for the tint (more glassy)
            uint alpha = (uint)(255 - (_settings.GlassIntensity / 100.0 * 225));
            uint color = (alpha << 24) | 0x00111111; 

            var accent = new AccentPolicy 
            { 
                AccentState = _settings.GlassIntensity > 0 ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND : AccentState.ACCENT_DISABLED,
                GradientColor = (int)color
            };

            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
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
                    ResetButton.Visibility = Visibility.Collapsed;
                    VibrancySlider.IsEnabled = true;
                    SharpnessSlider.IsEnabled = true;
                    DepixelateSlider.IsEnabled = true;
                    StatusText.Text = "READY";
                    StatusText.Foreground = _settings.Theme == "Red" ? new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00)) : (SolidColorBrush)StatusText.Foreground;
                    HelpText.Text = "READY TO ENHANCE";
                    PlaceholderText.Visibility = Visibility.Visible;
                    ImagePreview.Source = null;
                    ProcessingBar.Visibility = Visibility.Collapsed;
                    break;

                case AppState.ReadyToUpscale:
                    ActionButton.Content = "START RECONSTRUCTION";
                    ActionButton.IsEnabled = true;
                    ResetButton.Content = "RESET";
                    ResetButton.Visibility = Visibility.Visible;
                    VibrancySlider.IsEnabled = true;
                    SharpnessSlider.IsEnabled = true;
                    DepixelateSlider.IsEnabled = true;
                    StatusText.Text = "IMAGE LOADED";
                    StatusText.Foreground = Brushes.White;
                    HelpText.Text = "ADJUST SETTINGS AND START RECONSTRUCTION";
                    PlaceholderText.Visibility = Visibility.Collapsed;
                    ProcessingBar.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(_selectedImagePath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(_selectedImagePath);
                        bitmap.EndInit();
                        ImagePreview.Source = bitmap;
                    }
                    break;

                case AppState.Upscaling:
                    ActionButton.IsEnabled = false;
                    ResetButton.Visibility = Visibility.Collapsed;
                    VibrancySlider.IsEnabled = false;
                    SharpnessSlider.IsEnabled = false;
                    DepixelateSlider.IsEnabled = false;
                    StatusText.Text = "RECONSTRUCTING...";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00));
                    HelpText.Text = "PROCESSING TILES ON DIRECTML GPU ENGINE...";
                    ProcessingBar.Visibility = Visibility.Visible;
                    ProcessingBar.IsIndeterminate = true;
                    break;

                case AppState.Completed:
                    ActionButton.Content = "OPEN IN EXPLORER";
                    ActionButton.IsEnabled = true;
                    ResetButton.Content = "NEW IMAGE";
                    ResetButton.Visibility = Visibility.Visible;
                    VibrancySlider.IsEnabled = true;
                    SharpnessSlider.IsEnabled = true;
                    DepixelateSlider.IsEnabled = true;
                    StatusText.Text = "RECONSTRUCTION COMPLETE";
                    StatusText.Foreground = Brushes.LimeGreen;
                    HelpText.Text = "IMAGE SUCCESSFULLY UPSCALED!";
                    PlaceholderText.Visibility = Visibility.Collapsed;
                    ProcessingBar.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(_outputPath);
                        bitmap.DecodePixelWidth = 1200; // Limit preview resolution for 40K stability
                        bitmap.EndInit();
                        ImagePreview.Source = bitmap;
                    }
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _settings.WindowWidth = this.ActualWidth;
            _settings.WindowHeight = this.ActualHeight;
            _settings.Save();
            base.OnClosed(e);
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Visible;
        private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;

        private void Models_Click(object sender, RoutedEventArgs e)
        {
            UpdateModelButtonsState();
            ModelsOverlay.Visibility = Visibility.Visible;
        }

        private void CloseModels_Click(object sender, RoutedEventArgs e)
        {
            ModelsOverlay.Visibility = Visibility.Collapsed;
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
            ModelProgress.Visibility = Visibility.Visible;
            ModelStatusText.Visibility = Visibility.Visible;
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
                    Dispatcher.Invoke(() => {
                        ModelProgress.Value = progress;
                        ModelStatusText.Text = msg.ToUpper();
                    });
                });
                MessageBox.Show("Model downloaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ModelProgress.Visibility = Visibility.Collapsed;
                ModelStatusText.Visibility = Visibility.Collapsed;
                UpdateModelButtonsState();
            }
        }

        private void GlassSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || GlassText == null) return;
            _settings.GlassIntensity = e.NewValue;
            GlassText.Text = $"{(int)e.NewValue}%";
            EnableBlur();
            ApplyTheme();
            _settings.Save();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                _settings.DefaultSaveFolder = dialog.FolderName;
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
                
                // WPF Layer Opacity: Higher intensity = Lower Opacity (More transparent)
                if (_settings.GlassIntensity <= 0)
                {
                    MainWindowBorder.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
                }
                else
                {
                    // Map 0-100% intensity to 1.0 - 0.2 opacity (lower opacity = more glassy)
                    double wpfOpacity = 1.0 - (_settings.GlassIntensity / 100.0 * 0.8); 
                    MainWindowBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(wpfOpacity * 255), 0x11, 0x11, 0x11));
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
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedPath = openFileDialog.FileName;
                    try
                    {
                        using (var stream = new FileStream(selectedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                            if (decoder.Frames.Count > 0)
                            {
                                var frame = decoder.Frames[0];
                                int targetScale = GetTargetScale(frame.PixelWidth, frame.PixelHeight);
                                int finalWidth = frame.PixelWidth * targetScale;
                                int finalHeight = frame.PixelHeight * targetScale;

                                // Boundary check for extreme resolutions
                                if (finalWidth > 50000 || finalHeight > 50000)
                                {
                                    MessageBox.Show(
                                        $"The requested resolution of {finalWidth}x{finalHeight} exceeds the absolute safety ceiling (50,000 px).\n\nPlease choose a lower resolution target.",
                                        "Ceiling Reached",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning
                                    );
                                    return;
                                }
                            }
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
                        MessageBox.Show($"Failed to load image metadata: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                string modelName = ModelSelector.SelectedIndex == 1 ? "models/hat-x4.onnx" : "models/realesrgan-x4.onnx";
                string fullModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelName);
                if (!File.Exists(fullModelPath))
                {
                    MessageBox.Show(
                        $"The selected model ({Path.GetFileName(modelName)}) is not downloaded yet.\n\nPlease open the AI Model Manager (click the 🧠 icon in the title bar) to download it.",
                        "Model Missing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
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
                    using (var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        if (decoder.Frames.Count > 0)
                        {
                            int index = (int)Math.Round(MultiplierSlider.Value);
                            int targetRes = _resolutionSteps[index];
                            int origW = decoder.Frames[0].PixelWidth;
                            int origH = decoder.Frames[0].PixelHeight;
                            
                            float scale = (float)targetRes / Math.Max(origW, origH);
                            targetW = (int)(origW * scale);
                            targetH = (int)(origH * scale);
                        }
                    }
                } catch {}

                bool success = await Task.Run(() => CoreEngine.RunUpscale(input, output, modelName, targetW, targetH, vibrancy, sharpness, depixelate, presetMode));

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
                    StatusText.Foreground = Brushes.Red;
                    MessageBox.Show($"Upscaling failed.\n\nDebug Log:\n{debugLog}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                UpdateUiState();
            }
            else if (_currentState == AppState.Completed)
            {
                if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_outputPath}\"");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open explorer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task RunAutoDetectAsync(string imagePath)
        {
            // Show "ANALYZING" in status briefly
            StatusText.Text = "ANALYZING...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));

            AnalysisResult result;
            try
            {
                result = await ImageAnalyzer.AnalyzeAsync(imagePath);
            }
            catch
            {
                // If analysis fails just silently restore status
                StatusText.Text = "IMAGE LOADED";
                StatusText.Foreground = Brushes.White;
                return;
            }

            // Restore status
            StatusText.Text = "IMAGE LOADED";
            StatusText.Foreground = Brushes.White;

            // Always show dialog: if pixel art detected, or if a face was found in a non-pixel image
            if (!result.IsPixelArt && !result.HasCharacter) return;

            // Build a nice detection dialog
            var dialog = new Window
            {
                Title = "Scarl — Image Analysis",
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize
            };

            var border = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00)),
                BorderThickness = new Thickness(1),
                CornerRadius = new System.Windows.CornerRadius(10),
                Padding = new Thickness(30)
            };

            var stack = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // Title changes based on what was detected
            string dialogTitle = result.IsPixelArt && result.HasCharacter
                ? "⚠  PIXEL ART + CHARACTER DETECTED"
                : result.IsPixelArt
                    ? "⚠  PIXEL ART DETECTED"
                    : "👤  CHARACTER DETECTED";

            var icon = new System.Windows.Controls.TextBlock
            {
                Text = dialogTitle,
                Foreground = result.IsPixelArt
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x7C, 0xD9, 0x7C)),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var desc = new System.Windows.Controls.TextBlock
            {
                Text = result.Description,
                Foreground = Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stats = new System.Windows.Controls.TextBlock
            {
                Text = $"Blockiness: {result.BlockinessScore:P0}  ·  Colours: {result.UniqueColors}  ·  Suggested DE-PIXELATE: {result.RecommendedDepixelate}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var charInfo = new System.Windows.Controls.TextBlock
            {
                Text = !string.IsNullOrEmpty(result.CharacterInfo)
                    ? (result.HasCharacter ? "👤  " : "○  ") + result.CharacterInfo
                    : "",
                Foreground = result.HasCharacter
                    ? new SolidColorBrush(Color.FromRgb(0x7C, 0xD9, 0x7C))
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };

            var question = new System.Windows.Controls.TextBlock
            {
                Text = "Apply De-Pixelate enhancement for best results?",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            bool? userChoice = null;

            var yesBtn = new System.Windows.Controls.Button
            {
                Content = "YES — APPLY FIX",
                Width = 160,
                Height = 38,
                Margin = new Thickness(0, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            yesBtn.Template = CreateRoundedButtonTemplate(new SolidColorBrush(Color.FromRgb(0xFF, 0x24, 0x00)));
            yesBtn.Click += (s, e) => { userChoice = true; dialog.Close(); };

            var noBtn = new System.Windows.Controls.Button
            {
                Content = "NO — NORMAL UPSCALE",
                Width = 160,
                Height = 38,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            noBtn.Template = CreateRoundedButtonTemplate(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)));
            noBtn.Click += (s, e) => { userChoice = false; dialog.Close(); };

            btnRow.Children.Add(yesBtn);
            btnRow.Children.Add(noBtn);

            stack.Children.Add(icon);
            stack.Children.Add(desc);
            stack.Children.Add(stats);
            if (!string.IsNullOrEmpty(result.CharacterInfo))
                stack.Children.Add(charInfo);
            stack.Children.Add(question);
            stack.Children.Add(btnRow);

            border.Child = stack;
            dialog.Content = border;

            // Allow drag
            border.MouseLeftButtonDown += (s, e) => dialog.DragMove();

            dialog.ShowDialog();

            if (userChoice == true)
            {
                DepixelateSlider.Value = result.RecommendedDepixelate;
                HelpText.Text = $"DE-PIXELATE set to {result.RecommendedDepixelate} — ready to reconstruct!";
                StatusText.Text = "PIXEL FIX READY";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
            }
        }

        private static System.Windows.Controls.ControlTemplate CreateRoundedButtonTemplate(SolidColorBrush bg)
        {
            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, bg);
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(7));
            border.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(12, 8, 12, 8));
            var content = new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            content.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            return template;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedImagePath = null;
            _outputPath = null;
            _currentState = AppState.Select;
            UpdateUiState();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void DepixelateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Future real-time preview logic
        }

        private void PresetSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

        private void MultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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
            
            // We scale based on the largest dimension to hit the "K" target
            int maxDim = Math.Max(originalWidth, originalHeight);
            float scale = (float)targetRes / maxDim;
            
            // Minimum scale is 1
            return (int)Math.Max(1, Math.Ceiling(scale));
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}