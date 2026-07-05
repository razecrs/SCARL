using System;
using System.IO;

namespace Scarl.UI
{
    public static class ImageHeaderHelper
    {
        public static bool TryGetDimensions(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return TryGetDimensions(fs, out width, out height);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;
            
            // Read first 8 bytes for signature
            byte[] signature = new byte[8];
            if (stream.Read(signature, 0, 8) != 8) return false;
            
            // PNG Check
            if (signature[0] == 0x89 && signature[1] == 0x50 && signature[2] == 0x4E && signature[3] == 0x47 &&
                signature[4] == 0x0D && signature[5] == 0x0A && signature[6] == 0x1A && signature[7] == 0x0A)
            {
                byte[] ihdr = new byte[16];
                if (stream.Read(ihdr, 0, 16) != 16) return false;
                
                // Big-endian 32-bit integers
                width = (ihdr[8] << 24) | (ihdr[9] << 16) | (ihdr[10] << 8) | ihdr[11];
                height = (ihdr[12] << 24) | (ihdr[13] << 16) | (ihdr[14] << 8) | ihdr[15];
                return true;
            }
            
            // JPEG Check
            if (signature[0] == 0xFF && signature[1] == 0xD8)
            {
                stream.Position = 2;
                while (true)
                {
                    int markerPrefix = stream.ReadByte();
                    if (markerPrefix == -1) return false;
                    if (markerPrefix != 0xFF) continue;
                    
                    int marker = stream.ReadByte();
                    if (marker == -1) return false;
                    while (marker == 0xFF)
                    {
                        marker = stream.ReadByte();
                    }
                    
                    if (marker == 0xD9 || marker == 0xDA) // EOI or SOS
                        return false;
                        
                    int lenHigh = stream.ReadByte();
                    int lenLow = stream.ReadByte();
                    if (lenHigh == -1 || lenLow == -1) return false;
                    int segmentLength = (lenHigh << 8) | lenLow;
                    
                    // SOF markers
                    if ((marker >= 0xC0 && marker <= 0xC3) || (marker >= 0xC5 && marker <= 0xC7) ||
                        (marker >= 0xC9 && marker <= 0xCB) || (marker >= 0xCD && marker <= 0xCF))
                    {
                        int precision = stream.ReadByte();
                        int hHigh = stream.ReadByte();
                        int hLow = stream.ReadByte();
                        int wHigh = stream.ReadByte();
                        int wLow = stream.ReadByte();
                        if (precision == -1 || hHigh == -1 || hLow == -1 || wHigh == -1 || wLow == -1) return false;
                        
                        height = (hHigh << 8) | hLow;
                        width = (wHigh << 8) | wLow;
                        return true;
                    }
                    
                    if (segmentLength < 2) return false;
                    stream.Position += (segmentLength - 2);
                }
            }
            
            // GIF Check
            if (signature[0] == 'G' && signature[1] == 'I' && signature[2] == 'F' && signature[3] == '8' &&
                (signature[4] == '7' || signature[4] == '9') && signature[5] == 'a')
            {
                width = signature[6] | (signature[7] << 8);
                
                byte[] hBytes = new byte[2];
                if (stream.Read(hBytes, 0, 2) != 2) return false;
                height = hBytes[0] | (hBytes[1] << 8);
                return true;
            }
            
            // BMP Check
            if (signature[0] == 0x42 && signature[1] == 0x4D)
            {
                stream.Position = 18;
                byte[] bmpHeader = new byte[8];
                if (stream.Read(bmpHeader, 0, 8) != 8) return false;
                width = bmpHeader[0] | (bmpHeader[1] << 8) | (bmpHeader[2] << 16) | (bmpHeader[3] << 24);
                height = bmpHeader[4] | (bmpHeader[5] << 8) | (bmpHeader[6] << 16) | (bmpHeader[7] << 24);
                height = Math.Abs(height);
                return true;
            }
            
            return false;
        }
    }
}
