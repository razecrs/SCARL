using System;
using System.Runtime.InteropServices;

namespace Scarl.UI
{
    public static class CoreEngine
    {
        private const string DllName = "scarl_core.dll";

        [DllImport("scarl_core", CallingConvention = CallingConvention.Cdecl)]
        public static extern int upscale_image(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string inputPath, 
            [MarshalAs(UnmanagedType.LPUTF8Str)] string outputPath, 
            [MarshalAs(UnmanagedType.LPUTF8Str)] string modelName, 
            int targetWidth, 
            int targetHeight, 
            float vibrancy, 
            float sharpness, 
            float depixelate, 
            int presetMode);

        public static bool RunUpscale(string input, string output, string modelName, int targetWidth, int targetHeight, float vibrancy, float sharpness, float depixelate, int presetMode)
        {
            try
            {
                int result = upscale_image(input, output, modelName, targetWidth, targetHeight, vibrancy, sharpness, depixelate, presetMode);
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
