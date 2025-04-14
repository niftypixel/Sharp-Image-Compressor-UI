using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpImageCompressor
{
    public class ImageCompressor
    {
        public string InputFolder { get; set; }
        public string OutputFolder { get; set; }
        public bool ConvertPngToJpeg { get; set; }
        public int JpegCompressionQuality { get; set; }
        public string PngCompressionQuality { get; set; }
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        private int _pngsRetained;
        private int _pngsConverted;

        public int PngsRetained => Interlocked.CompareExchange(ref _pngsRetained, 0, 0);
        public int PngsConverted => Interlocked.CompareExchange(ref _pngsConverted, 0, 0);

        private int _imgsRetained;
        private int _imgsConverted;

        public int ImgsRetained => Interlocked.CompareExchange(ref _imgsRetained, 0, 0);
        public int ImgsConverted => Interlocked.CompareExchange(ref _imgsConverted, 0, 0);

        private static readonly string[] SupportedExtensions = new[] { ".jpg", ".jpeg", ".png" };

        public async Task CompressAllAsync(IProgress<(int completed, int total)>? progress = null)
        {
            var files = Directory.GetFiles(InputFolder, "*.*", SearchOption.AllDirectories).Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

            int total = files.Count;
            int completed = 0;

            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism }, async (file, _) =>
            {
                try
                {
                    string ext = Path.GetExtension(file).ToLower();
                    string relativePath = Path.GetRelativePath(InputFolder, file);
                    string outputRelativePath = relativePath;
                    string outputPath = Path.Combine(OutputFolder, outputRelativePath);

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                    using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(file);
                    image.Metadata.ExifProfile = null;

                    using var ms = new MemoryStream();
                    string finalOutputPath = outputPath;

                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                        await image.SaveAsync(ms, new JpegEncoder { Quality = JpegCompressionQuality });
                    }
                    else if (ext == ".png")
                    {
                        bool hasTransparency = HasTransparency(image);

                        if (ConvertPngToJpeg && !hasTransparency)
                        {
                            finalOutputPath = Path.Combine(OutputFolder, Path.ChangeExtension(relativePath, ".jpg"));
                            await image.SaveAsync(ms, new JpegEncoder { Quality = JpegCompressionQuality });
                            Interlocked.Increment(ref _pngsConverted);

                            string oldPngPath = Path.Combine(OutputFolder, relativePath);
                            if (File.Exists(oldPngPath))
                            {
                                try { File.Delete(oldPngPath); } catch { }
                            }
                        }
                        else
                        {
                            await image.SaveAsync(ms, new PngEncoder
                            {
                                CompressionLevel = GetPngCompressionLevelFromString(PngCompressionQuality),
                                FilterMethod = PngFilterMethod.Adaptive
                            });
                            Interlocked.Increment(ref _pngsRetained);
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    FileInfo originalFileInfo = new FileInfo(file);

                    if (ms.Length < originalFileInfo.Length)
                    {
                        using var outFile = File.Create(finalOutputPath);
                        await ms.CopyToAsync(outFile);
                        Interlocked.Increment(ref _imgsConverted);
                    }
                    else
                    {
                        File.Copy(file, finalOutputPath, overwrite: true);
                        Interlocked.Increment(ref _imgsRetained);
                    }

                    Interlocked.Increment(ref completed);
                    progress?.Report((completed, total));
                }
                catch { }
            });
        }

        private static PngCompressionLevel GetPngCompressionLevelFromString(string selectedText)
        {
            return selectedText switch
            {
                "Max Compression" => PngCompressionLevel.BestCompression,
                "Very High Compression" => PngCompressionLevel.Level8,
                "High Compression" => PngCompressionLevel.Level7,
                "Medium High Compression" => PngCompressionLevel.Level6,
                "Medium Compression" => PngCompressionLevel.Level5,
                "Medium Low Compression" => PngCompressionLevel.Level4,
                "Light Compression" => PngCompressionLevel.Level3,
                "Low Compression" => PngCompressionLevel.Level2,
                "Very Low Compression" => PngCompressionLevel.Level1,
                "No Compression" => PngCompressionLevel.Level0,
                _ => PngCompressionLevel.DefaultCompression // fallback
            };
        }

        private static bool HasTransparency(Image<Rgba32> image)
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image[x, y].A < 255)
                        return true;
                }
            }
            return false;
        }
    }
}