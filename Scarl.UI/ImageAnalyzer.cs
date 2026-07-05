using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Scarl.UI
{
    public class ClassificationResult
    {
        public string Label      { get; set; } = "";
        public float  Confidence { get; set; }
    }

    public class AnalysisResult
    {
        public bool   IsPixelArt             { get; set; }
        public bool   HasCharacter           { get; set; }
        public float  BlockinessScore        { get; set; }
        public int    UniqueColors           { get; set; }
        public string Description            { get; set; } = "";
        public string CharacterInfo          { get; set; } = "";
        public List<ClassificationResult> TopClasses { get; set; } = new();
        public int    RecommendedDepixelate  { get; set; }
    }

    public static class ImageAnalyzer
    {
        // Lazy singleton CLIP classifier — loaded once, reused
        private static ClipClassifier? _clip;
        private static string[]?       _characters;
        private static readonly object _lock = new();

        private static bool TryLoadClip(out ClipClassifier? clip, out string[]? chars)
        {
            lock (_lock)
            {
                if (_clip != null) { clip = _clip; chars = _characters; return true; }
                try
                {
                    string dir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
                    string vOnnx = Path.Combine(dir, "clip_vision.onnx");
                    string tOnnx = Path.Combine(dir, "clip_text.onnx");
                    string vocab = Path.Combine(dir, "clip_vocab.json");
                    string merges= Path.Combine(dir, "clip_merges.txt");
                    string chFile= Path.Combine(dir, "characters.txt");

                    if (!File.Exists(vOnnx) || !File.Exists(tOnnx) ||
                        !File.Exists(vocab)  || !File.Exists(merges))
                    { clip = null; chars = null; return false; }

                    _clip = new ClipClassifier(dir);

                    // Load character list, skip comments and blanks
                    _characters = File.Exists(chFile)
                        ? File.ReadAllLines(chFile)
                            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                            .Select(l => l.Trim())
                            .Distinct()
                            .ToArray()
                        : Array.Empty<string>();

                    clip  = _clip;
                    chars = _characters;
                    return true;
                }
                catch { clip = null; chars = null; return false; }
            }
        }

        public static Task<AnalysisResult> AnalyzeAsync(string imagePath) =>
            Task.Run(() => Analyze(imagePath));

        private static AnalysisResult Analyze(string imagePath)
        {
            // ── Load + downscale to 256px for heuristics ─────────────────────
            Bitmap original;
            using (var s = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                original = new Bitmap(s);
            }

            int w = Math.Min(original.PixelSize.Width, 256), h = Math.Min(original.PixelSize.Height, 256);
            byte[] px = new byte[h * w * 4];
            int stride = w * 4;
            using (var scaled = original.CreateScaledBitmap(new PixelSize(w, h), BitmapInterpolationMode.HighQuality))
            {
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    scaled.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), px.Length, stride);
                }
                finally
                {
                    handle.Free();
                }
            }

            // ── Unique colour count ───────────────────────────────────────────
            var uColors = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < px.Length; i += 4)
                uColors.Add(((px[i+2]>>3)<<10)|((px[i+1]>>3)<<5)|(px[i]>>3));
            int colorCount = uColors.Count;

            // ── Blockiness ───────────────────────────────────────────────────
            const int T = 8;
            double totVar = 0; int tCnt = 0;
            for (int ty = 0; ty+T <= h; ty+=T)
            for (int tx = 0; tx+T <= w; tx+=T)
            {
                double sR=0,sG=0,sB=0,s2R=0,s2G=0,s2B=0;
                int n = T*T;
                for (int dy=0;dy<T;dy++) for (int dx=0;dx<T;dx++)
                {
                    int idx=(ty+dy)*stride+(tx+dx)*4;
                    double b=px[idx],g=px[idx+1],r=px[idx+2];
                    sB+=b;s2B+=b*b;sG+=g;s2G+=g*g;sR+=r;s2R+=r*r;
                }
                totVar+=((s2R/n-(sR/n)*(sR/n))+(s2G/n-(sG/n)*(sG/n))+(s2B/n-(sB/n)*(sB/n)))/3.0;
                tCnt++;
            }
            float blockiness = (float)Math.Max(0,Math.Min(1,1.0-((tCnt>0?totVar/tCnt:9999)/500.0)));
            bool isPixelArt  = colorCount < 2000 && blockiness > 0.30f;
            int  recommended = isPixelArt ? Math.Clamp((int)(blockiness*80+20),30,100) : 0;

            // ── CLIP character recognition ────────────────────────────────────
            var topClasses = new List<ClassificationResult>();
            bool hasCharacter = false;
            string characterInfo = "";

            if (TryLoadClip(out var clip, out var chars) && clip != null && chars != null && chars.Length > 0)
            {
                try
                {
                    var matches = clip.MatchCharacters(imagePath, chars, topK: 5);

                    foreach (var m in matches)
                    {
                        topClasses.Add(new ClassificationResult
                        {
                            Label      = m.Name,
                            Confidence = m.Similarity
                        });
                    }

                    var best = matches.FirstOrDefault();
                    // CLIP cosine similarity > 0.20 indicates a genuine match
                    hasCharacter = best != null && best.Similarity > 0.20f;

                    if (topClasses.Count > 0)
                    {
                        var top3 = topClasses.Take(3)
                            .Select(c => $"{c.Label} ({c.Confidence:P0})");
                        characterInfo = "AI: " + string.Join(" · ", top3);
                    }
                    else
                    {
                        characterInfo = "No strong character match found.";
                    }
                }
                catch (Exception ex)
                {
                    characterInfo = $"CLIP inference failed: {ex.Message}";
                }
            }
            else
            {
                // Fallback to skin-tone heuristic when CLIP models not loaded
                int skinPx = 0, totPx = px.Length / 4;
                for (int i = 0; i < px.Length; i += 4)
                {
                    float b=px[i]/255f,g=px[i+1]/255f,r=px[i+2]/255f;
                    float cMax=Math.Max(r,Math.Max(g,b)),delta=cMax-Math.Min(r,Math.Min(g,b));
                    float hue=0;
                    if (delta>0.001f)
                    {
                        if      (cMax==r) hue=60*(((g-b)/delta)%6);
                        else if (cMax==g) hue=60*(((b-r)/delta)+2);
                        else              hue=60*(((r-g)/delta)+4);
                        if (hue<0) hue+=360;
                    }
                    float sat=cMax<0.001f?0:delta/cMax;
                    if(((hue>=0&&hue<=28)||(hue>=330&&hue<=360))&&sat>=0.12f&&sat<=0.88f&&cMax>=0.25f)
                        skinPx++;
                }
                hasCharacter = totPx>0 && (float)skinPx/totPx >= 0.02f;
                characterInfo = hasCharacter
                    ? "Character detected (skin-tone fallback — CLIP models loading…)"
                    : "No character detected (CLIP models not yet loaded).";
            }

            string description = isPixelArt && blockiness > 0.65f
                ? "Heavy pixel art / very low-res character image detected."
                : isPixelArt
                    ? "Pixel art or blocky low-resolution image detected."
                    : "Standard image — normal upscale recommended.";

            return new AnalysisResult
            {
                IsPixelArt            = isPixelArt,
                HasCharacter          = hasCharacter || topClasses.Count > 0,
                BlockinessScore       = blockiness,
                UniqueColors          = colorCount,
                Description           = description,
                CharacterInfo         = characterInfo,
                TopClasses            = topClasses,
                RecommendedDepixelate = recommended
            };
        }
    }
}
