using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using ImageTextDetector.Models;

namespace ImageTextDetector.Services
{
    public class FaceCroppingService
    {
        /// <summary>
        /// Recorta a face de uma imagem e salva na pasta de destino
        /// </summary>
        public static bool CropFaces(string imagePath, List<FaceMatch> faceMatches, string outputFolder, string outputFileName, out string? error)
        {
            error = null;

            if (!File.Exists(imagePath))
            {
                error = $"Arquivo não encontrado: {imagePath}";
                return false;
            }

            if (faceMatches.Count == 0)
            {
                error = "Nenhuma face para recortar";
                return false;
            }

            try
            {
                // Usar ImDecode para suportar caminhos com espaços
                byte[] imageData = File.ReadAllBytes(imagePath);
                using var mat = Cv2.ImDecode(imageData, ImreadModes.Color);
                
                if (mat.Empty())
                {
                    error = $"OpenCV não conseguiu decodificar a imagem: {Path.GetFileName(imagePath)}";
                    return false;
                }

                Directory.CreateDirectory(outputFolder);

                for (int i = 0; i < faceMatches.Count; i++)
                {
                    var faceMatch = faceMatches[i];
                    var boundingBox = faceMatch.BoundingBox;

                    // Adicionar padding de 20 pixels em torno do rosto
                    int padding = 20;
                    int x = Math.Max(0, (int)boundingBox.X - padding);
                    int y = Math.Max(0, (int)boundingBox.Y - padding);
                    int width = Math.Min(mat.Width - x, (int)boundingBox.Width + padding * 2);
                    int height = Math.Min(mat.Height - y, (int)boundingBox.Height + padding * 2);

                    if (width > 0 && height > 0)
                    {
                        var roi = new Rect(x, y, width, height);
                        using var croppedMat = new Mat(mat, roi);

                        string baseName = Path.GetFileNameWithoutExtension(outputFileName);
                        string extension = Path.GetExtension(imagePath);
                        string outputPath = Path.Combine(outputFolder, $"{baseName}_rosto_{i:D2}{extension}");

                        if (!Cv2.ImWrite(outputPath, croppedMat))
                        {
                            error = $"Erro ao salvar imagem recortada: {outputPath}";
                            return false;
                        }
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

        /// <summary>
        /// Copia a imagem completa (sem recorte) para o destino
        /// </summary>
        public static bool CopyFaceImage(string imagePath, string outputFolder, out string? error)
        {
            error = null;

            try
            {
                if (!File.Exists(imagePath))
                {
                    error = $"Arquivo não encontrado: {imagePath}";
                    return false;
                }

                Directory.CreateDirectory(outputFolder);
                
                string fileName = Path.GetFileName(imagePath);
                string destPath = Path.Combine(outputFolder, fileName);
                
                File.Copy(imagePath, destPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Move a imagem original para o destino (não recorta)
        /// </summary>
        public static bool MoveFaceImage(string imagePath, string outputFolder, out string? error)
        {
            error = null;

            try
            {
                if (!File.Exists(imagePath))
                {
                    error = $"Arquivo não encontrado: {imagePath}";
                    return false;
                }

                Directory.CreateDirectory(outputFolder);
                
                string fileName = Path.GetFileName(imagePath);
                string destPath = Path.Combine(outputFolder, fileName);
                
                File.Move(imagePath, destPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
