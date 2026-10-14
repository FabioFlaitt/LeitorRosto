using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace ImageTextDetector.Services
{
    public class ImageCroppingService
    {
        public static bool CropTextRegions(string imagePath, List<Models.TextRegion> textRegions, string outputFolder, string outputFileName, out string? error)
        {
            error = null;

            if (!File.Exists(imagePath))
            {
                error = $"Arquivo não encontrado: {imagePath}";
                return false;
            }

            if (textRegions.Count == 0)
            {
                error = "Nenhuma região de texto para recortar";
                return false;
            }

            try
            {
                // Cv2.ImRead falha em caminhos com espaços/caracteres especiais no Windows.
                // Usar ImDecode sobre os bytes do arquivo é a solução correta.
                using var mat = Cv2.ImDecode(File.ReadAllBytes(imagePath), ImreadModes.Color);
                if (mat.Empty())
                {
                    error = $"OpenCV não conseguiu decodificar a imagem: {Path.GetFileName(imagePath)}";
                    return false;
                }

                var mergedRegions = MergeOverlappingRegions(textRegions);
                int padding = 10;

                Directory.CreateDirectory(outputFolder);

                for (int i = 0; i < mergedRegions.Count; i++)
                {
                    var region = mergedRegions[i];

                    int x = Math.Max(0, (int)region.BoundingBox.X - padding);
                    int y = Math.Max(0, (int)region.BoundingBox.Y - padding);
                    int width = Math.Min(mat.Width - x, (int)region.BoundingBox.Width + padding * 2);
                    int height = Math.Min(mat.Height - y, (int)region.BoundingBox.Height + padding * 2);

                    if (width > 0 && height > 0)
                    {
                        var roi = new Rect(x, y, width, height);
                        using var croppedMat = new Mat(mat, roi);

                        string baseName = Path.GetFileNameWithoutExtension(outputFileName);
                        string extension = Path.GetExtension(imagePath);
                        string outputPath = Path.Combine(outputFolder, $"{baseName}_crop_{i:D3}{extension}");

                        Cv2.ImWrite(outputPath, croppedMat);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static List<Models.TextRegion> MergeOverlappingRegions(List<Models.TextRegion> regions)
        {
            if (regions.Count == 0)
                return regions;

            var merged = new List<Models.TextRegion>();
            var used = new bool[regions.Count];

            for (int i = 0; i < regions.Count; i++)
            {
                if (used[i])
                    continue;

                var current = regions[i];
                var boundingBox = current.BoundingBox;

                for (int j = i + 1; j < regions.Count; j++)
                {
                    if (used[j])
                        continue;

                    if (boundingBox.IntersectsWith(regions[j].BoundingBox))
                    {
                        boundingBox.Union(regions[j].BoundingBox);
                        used[j] = true;
                    }
                }

                merged.Add(new Models.TextRegion(current.Text, boundingBox, current.Confidence));
                used[i] = true;
            }

            return merged;
        }
    }
}
