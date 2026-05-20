using System;
using System.Runtime.InteropServices;

namespace Scarl.UI
{
    public static class CoreEngine
    {
        private const string DllName = "scarl_core.dll";

        [DllImport("scarl_core", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int upscale_image(string inputPath, string outputPath, string modelName, int scale, float vibrancy, float sharpness, float depixelate, int presetMode);

        public static bool RunUpscale(string input, string output, string modelName, int scale, float vibrancy, float sharpness, float depixelate, int presetMode)
        {
            try
            {
                int result = upscale_image(input, output, modelName, scale, vibrancy, sharpness, depixelate, presetMode);
                return result == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical failure calling Rust core: {ex.Message}");
                return false;
            }
        }
    }
}
