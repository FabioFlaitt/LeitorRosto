using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ImageTextDetector.Services
{
    public class ImageProcessingService
    {
        private readonly FaceRecognitionService _faceService;
        private readonly string[] _supportedFormats = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp"];

        public event Action<string>? OnProgress;
        public event Action<int, int, int, int, int>? OnSummary;

        public ImageProcessingService(string? referenceFacePath = null, double tolerance = 0.55, string detectionModel = "auto", FaceEngine engine = FaceEngine.Dlib)
        {
            _faceService = new FaceRecognitionService(referenceFacePath, tolerance, detectionModel, engine);
        }

        public void SetReferenceFace(string referenceFacePath)
        {
            if (!string.IsNullOrEmpty(referenceFacePath) && File.Exists(referenceFacePath))
                OnProgress?.Invoke($"✓ Imagem de referência carregada: {Path.GetFileName(referenceFacePath)}");
            else
                OnProgress?.Invoke("⚠️ Arquivo de referência não encontrado.");
        }

        public async Task ProcessFolders(List<string> sourceFolders, string outputFolder, string noFaceFoundFolder = "", string otherFaceFolder = "")
        {
            var validSources = new List<string>();
            foreach (var folder in sourceFolders)
            {
                if (Directory.Exists(folder))
                    validSources.Add(folder);
                else
                    OnProgress?.Invoke($"⚠️ Pasta ignorada (não existe): {folder}");
            }

            if (validSources.Count == 0)
            {
                OnProgress?.Invoke("Erro: Nenhuma pasta de origem válida foi encontrada.");
                OnSummary?.Invoke(0, 0, 0, 0, 0);
                return;
            }

            Directory.CreateDirectory(outputFolder);

            bool hasNoFaceFolder = !string.IsNullOrEmpty(noFaceFoundFolder);
            if (hasNoFaceFolder) Directory.CreateDirectory(noFaceFoundFolder);

            bool hasOtherFaceFolder = !string.IsNullOrEmpty(otherFaceFolder);
            if (hasOtherFaceFolder) Directory.CreateDirectory(otherFaceFolder);

            var allImageFiles = new List<string>();
            foreach (var sourceFolder in validSources)
                allImageFiles.AddRange(GetImageFiles(sourceFolder));

            int totalImages = allImageFiles.Count;
            int imagesWithTargetFace = 0;
            int movedCount = 0;
            int noFaceFoundCount = 0;
            int otherFaceCount = 0;
            int processed = 0;

            if (totalImages == 0)
            {
                OnProgress?.Invoke("Nenhuma imagem encontrada nas pastas de origem.");
                OnSummary?.Invoke(0, 0, 0, 0, 0);
                return;
            }

            OnProgress?.Invoke($"Encontradas {totalImages} imagens em {validSources.Count} pasta(s).");
            OnProgress?.Invoke("Iniciando reconhecimento facial...\n");

            await _faceService.BatchDetectFacesAsync(allImageFiles, (imagePath, analysisResult) =>
            {
                processed++;
                int percent = (processed * 100) / totalImages;

                if (string.IsNullOrEmpty(imagePath))
                {
                    OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ ERRO: Caminho da imagem vazio!");
                    return;
                }

                string fileName = Path.GetFileName(imagePath);

                if (!File.Exists(imagePath))
                {
                    OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ⚠️ {fileName} — arquivo não encontrado");
                    return;
                }

                if (analysisResult.HasTargetFace)
                {
                    imagesWithTargetFace++;
                    try
                    {
                        if (FaceCroppingService.MoveFaceImage(imagePath, outputFolder, out string? moveError))
                        {
                            movedCount++;
                            OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✓ {fileName} → ROSTO ALVO ({analysisResult.FacesDetected} rosto(s))");
                        }
                        else
                        {
                            OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro ao mover: {moveError}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro: {ex.Message}");
                    }
                }
                else if (analysisResult.HasFaces)
                {
                    if (hasOtherFaceFolder)
                    {
                        try
                        {
                            if (FaceCroppingService.MoveFaceImage(imagePath, otherFaceFolder, out string? moveError))
                            {
                                otherFaceCount++;
                                OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✓ {fileName} → OUTRO ROSTO ({analysisResult.FacesDetected} rosto(s))");
                            }
                            else
                            {
                                OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro ao mover: {moveError}");
                            }
                        }
                        catch (Exception ex)
                        {
                            OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro: {ex.Message}");
                        }
                    }
                    else
                    {
                        OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% — {fileName} tem rosto mas não é o alvo ({analysisResult.FacesDetected} rosto(s))");
                    }
                }
                else if (analysisResult.HasPerson)
                {
                    // YOLO detectou pessoa (sem rosto visível) → pasta "Com Rosto"
                    imagesWithTargetFace++;
                    try
                    {
                        if (FaceCroppingService.MoveFaceImage(imagePath, outputFolder, out string? moveError))
                        {
                            movedCount++;
                            OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% 👤 {fileName} → COM ROSTO (pessoa detectada)");
                        }
                        else
                        {
                            OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro ao mover: {moveError}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro: {ex.Message}");
                    }
                }
                else
                {
                    // Sem rosto e sem pessoa
                    if (hasNoFaceFolder)
                    {
                        try
                        {
                            if (FaceCroppingService.MoveFaceImage(imagePath, noFaceFoundFolder, out string? moveError))
                            {
                                noFaceFoundCount++;
                                OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% → {fileName} movida para SEM ROSTO");
                            }
                            else
                            {
                                OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro ao mover: {moveError}");
                            }
                        }
                        catch (Exception ex)
                        {
                            OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% ✗ {fileName} — erro: {ex.Message}");
                        }
                    }
                    else
                    {
                        OnProgress?.Invoke($"[{processed}/{totalImages}] {percent}% — {fileName} (sem rosto e sem pessoa)");
                    }
                }
            });

            OnProgress?.Invoke($"\n✓ Concluído!");
            OnProgress?.Invoke($"   • {movedCount} imagens com ROSTO ALVO recortadas/salvas");
            OnProgress?.Invoke($"   • {otherFaceCount} imagens com OUTRO ROSTO recortadas");
            OnProgress?.Invoke($"   • {noFaceFoundCount} imagens SEM ROSTO movidas");
            OnSummary?.Invoke(totalImages, imagesWithTargetFace, movedCount, noFaceFoundCount, otherFaceCount);
        }

        public async Task ProcessFolder(string sourceFolder, string outputFolder, string noFaceFoundFolder = "", string otherFaceFolder = "")
        {
            if (!Directory.Exists(sourceFolder))
            {
                OnProgress?.Invoke($"Erro: Pasta de origem não encontrada: {sourceFolder}");
                return;
            }
            await ProcessFolders(new List<string> { sourceFolder }, outputFolder, noFaceFoundFolder, otherFaceFolder);
        }

        private List<string> GetImageFiles(string folderPath)
        {
            var files = new List<string>();
            foreach (var format in _supportedFormats)
            {
                files.AddRange(Directory.GetFiles(folderPath, $"*{format}", SearchOption.AllDirectories));
                files.AddRange(Directory.GetFiles(folderPath, $"*{format.ToUpper()}", SearchOption.AllDirectories));
            }
            return files.Distinct().ToList();
        }
    }
}
