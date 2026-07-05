using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Scarl.UI
{
    public class ClipMatch
    {
        public string Name       { get; set; } = "";
        public float  Similarity { get; set; }   // cosine similarity, 0-1
    }

    /// <summary>
    /// Wraps CLIP ViT-L/14 ONNX vision + text encoders.
    /// Image embedding vs pre-encoded character names → top-K matches.
    /// </summary>
    public class ClipClassifier : IDisposable
    {
        // CLIP ViT-L/14 image normalisation (different from ImageNet)
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f,  0.40821073f };
        private static readonly float[] Std  = { 0.26862954f, 0.26130258f, 0.27577711f };

        private readonly InferenceSession _visionSess;
        private readonly InferenceSession _textSess;
        private readonly ClipTokenizer    _tokenizer;

        // Cached text embeddings: name → normalised vector
        private readonly Dictionary<string, float[]> _textCache = new();
        private readonly object _textCacheLock = new();

        private readonly string _visionInputName;
        private readonly string _textInputIdsName;
        private readonly string _textAttnMaskName;

        public ClipClassifier(string modelDir)
        {
            var opts = new SessionOptions();
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            opts.IntraOpNumThreads = Environment.ProcessorCount;

            _visionSess = new InferenceSession(Path.Combine(modelDir, "clip_vision.onnx"), opts);
            _textSess   = new InferenceSession(Path.Combine(modelDir, "clip_text.onnx"),   opts);
            _tokenizer  = new ClipTokenizer(
                Path.Combine(modelDir, "clip_vocab.json"),
                Path.Combine(modelDir, "clip_merges.txt"));

            // Probe input names (robust to different ONNX export flavours)
            _visionInputName = _visionSess.InputNames.FirstOrDefault(n =>
                n.Contains("pixel") || n.Contains("image")) ?? _visionSess.InputNames[0];

            _textInputIdsName = _textSess.InputNames.FirstOrDefault(n =>
                n.Contains("input_ids") || n.Contains("ids")) ?? _textSess.InputNames[0];

            _textAttnMaskName = _textSess.InputNames.FirstOrDefault(n =>
                n.Contains("attention") || n.Contains("mask")) ?? _textSess.InputNames[1];
        }

        // ── Image encoding ────────────────────────────────────────────────────
        public float[] EncodeImage(string imagePath)
        {
            BitmapSource src;
            using (var s = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var dec = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                src = dec.Frames[0];
            }

            // Resize to 224×224 (CLIP standard)
            var scaled = new TransformedBitmap(src,
                new System.Windows.Media.ScaleTransform(224.0 / src.PixelWidth, 224.0 / src.PixelHeight));
            var rgb = new FormatConvertedBitmap(
                scaled, System.Windows.Media.PixelFormats.Rgb24, null, 0);

            int stride = 224 * 3;
            byte[] px = new byte[224 * stride];
            rgb.CopyPixels(px, stride, 0);

            // Build NCHW float32 tensor with CLIP normalisation
            var tensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
            for (int y = 0; y < 224; y++)
            for (int x = 0; x < 224; x++)
            {
                int i = y * stride + x * 3;
                tensor[0, 0, y, x] = (px[i]     / 255f - Mean[0]) / Std[0];
                tensor[0, 1, y, x] = (px[i + 1] / 255f - Mean[1]) / Std[1];
                tensor[0, 2, y, x] = (px[i + 2] / 255f - Mean[2]) / Std[2];
            }

            var inputs = new List<NamedOnnxValue>
                { NamedOnnxValue.CreateFromTensor(_visionInputName, tensor) };

            using var res = _visionSess.Run(inputs);
            return Normalise(PickEmbedding(res));
        }

        // ── Text encoding (with cache) ────────────────────────────────────────
        public float[] EncodeText(string text)
        {
            lock (_textCacheLock)
            {
                if (_textCache.TryGetValue(text, out var cached)) return cached;
            }

            long[] ids  = _tokenizer.Encode(text);
            long[] mask = ids.Select(t => t != 0 ? 1L : 0L).ToArray();

            var idsTensor   = new DenseTensor<long>(ids,  new[] { 1, 77 });
            var maskTensor  = new DenseTensor<long>(mask, new[] { 1, 77 });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_textInputIdsName,  idsTensor),
                NamedOnnxValue.CreateFromTensor(_textAttnMaskName,  maskTensor)
            };

            float[] emb;
            using (var res = _textSess.Run(inputs))
            {
                emb = Normalise(PickEmbedding(res));
            }

            lock (_textCacheLock)
            {
                _textCache[text] = emb;
            }
            return emb;
        }

        // ── Top-K character matching ──────────────────────────────────────────
        public List<ClipMatch> MatchCharacters(string imagePath, IEnumerable<string> candidates, int topK = 5)
        {
            float[] imgEmb = EncodeImage(imagePath);

            // CLIP prompting: query with multiple templates and take the best
            var results = new List<ClipMatch>();
            foreach (var name in candidates)
            {
                // Ensemble of prompts — average their embeddings for robustness
                var prompts = new[]
                {
                    $"a photo of {name}",
                    $"a drawing of {name}",
                    $"an illustration of {name}",
                    $"{name}"
                };

                float[] avgEmb = AverageEmbeddings(prompts.Select(EncodeText));
                float sim = CosineSim(imgEmb, avgEmb);
                results.Add(new ClipMatch { Name = name, Similarity = sim });
            }

            return results.OrderByDescending(r => r.Similarity).Take(topK).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static float[] PickEmbedding(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> res)
        {
            // Prefer pooler_output / image_embeds / text_embeds over last_hidden_state
            var preferred = res.FirstOrDefault(r =>
                r.Name.Contains("pooler") ||
                r.Name.Contains("embeds") ||
                r.Name.Contains("embed"));
            var target = preferred ?? res.First();
            var t = target.AsTensor<float>();

            // If 3D [1, seq, dim], take the first token ([CLS])
            if (t.Dimensions.Length == 3) return Enumerable.Range(0, t.Dimensions[2])
                .Select(i => t[0, 0, i]).ToArray();

            return t.ToArray();
        }

        private static float[] AverageEmbeddings(IEnumerable<float[]> embs)
        {
            float[]? avg = null;
            int cnt = 0;
            foreach (var e in embs)
            {
                if (avg == null) avg = new float[e.Length];
                for (int i = 0; i < e.Length; i++) avg[i] += e[i];
                cnt++;
            }
            if (avg == null) return Array.Empty<float>();
            return Normalise(avg.Select(x => x / cnt).ToArray());
        }

        private static float[] Normalise(float[] v)
        {
            double norm = Math.Sqrt(v.Sum(x => (double)x * x));
            return norm > 1e-8 ? v.Select(x => (float)(x / norm)).ToArray() : v;
        }

        private static float CosineSim(float[] a, float[] b)
        {
            float dot = 0;
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) dot += a[i] * b[i];
            return Math.Clamp(dot, -1f, 1f);
        }

        public void Dispose()
        {
            _visionSess.Dispose();
            _textSess.Dispose();
        }
    }
}
