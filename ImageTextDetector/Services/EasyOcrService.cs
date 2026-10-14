using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ImageTextDetector.Services
{
    public class EasyOcrService
    {
        private readonly string _pythonScriptPath;
        private readonly string _pythonExePath;

        public EasyOcrService()
        {
            _pythonScriptPath = FindScript(AppDomain.CurrentDomain.BaseDirectory);
            _pythonExePath = "py";

            Console.WriteLine($"[EasyOCR] Script: {_pythonScriptPath}");
            Console.WriteLine($"[EasyOCR] Existe: {File.Exists(_pythonScriptPath)}");
        }

        private static string FindScript(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "ocr_service.py");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", "ocr_service.py"));
        }

        private ProcessStartInfo BuildProcessInfo(string arguments) => new()
        {
            FileName = _pythonExePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        /// <summary>
        /// Processa um lote de imagens com detecção de texto e pessoas.
        /// onResult é chamado para cada imagem com resultado completo de análise.
        /// </summary>
        public async Task BatchDetectImagesAsync(
            IList<string> imagePaths,
            Action<string, Models.ImageAnalysisResult> onResult,
            CancellationToken cancellationToken = default)
        {
            if (imagePaths.Count == 0) return;

            if (!File.Exists(_pythonScriptPath))
            {
                Console.WriteLine($"[EasyOCR] Script Python não encontrado: {_pythonScriptPath}");
                foreach (var path in imagePaths)
                    onResult(path, new Models.ImageAnalysisResult(false, false, []));
                return;
            }

            string tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllLinesAsync(tempFile, imagePaths, Encoding.UTF8, cancellationToken);

                // 1 worker = 1 modelo EasyOCR em memória (~2GB vs ~5GB com 2 workers)
                var processInfo = BuildProcessInfo($"-u \"{_pythonScriptPath}\" --batch \"{tempFile}\" --workers 1");
                processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("[EasyOCR] Falha ao iniciar processo Python");
                    foreach (var path in imagePaths)
                        onResult(path, new Models.ImageAnalysisResult(false, false, []));
                    return;
                }

                // Prioridade baixa para não congelar o PC durante o processamento
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        int jsonStart = line.IndexOf('{');
                        if (jsonStart > 0) line = line[jsonStart..];

                        var result = JsonSerializer.Deserialize<BatchImageResult>(line);
                        if (result == null) continue;

                        var textRegions = ParseRegions(result);
                        bool hasText = result.Success == true && textRegions.Count > 0;
                        bool hasPeople = result.HasPeople ?? false;
                        
                        var analysisResult = new Models.ImageAnalysisResult(hasText, hasPeople, textRegions);
                        onResult(result.Image ?? "", analysisResult);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EasyOCR] Erro ao processar linha: {ex.Message}");
                    }
                }

                await process.WaitForExitAsync(cancellationToken);
                string errorOutput = await stderrTask;
                if (!string.IsNullOrEmpty(errorOutput))
                    Console.WriteLine($"[EasyOCR] Stderr: {errorOutput}");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        /// <summary>
        /// Processa um lote de imagens em um único processo Python com prioridade baixa.
        /// onResult é chamado para cada imagem conforme é processada, recebendo (imagePath, temTexto, regiões).
        /// </summary>
        public async Task BatchDetectImagesAsync(
            IList<string> imagePaths,
            Action<string, bool, List<Models.TextRegion>> onResult,
            CancellationToken cancellationToken = default)
        {
            if (imagePaths.Count == 0) return;

            if (!File.Exists(_pythonScriptPath))
            {
                Console.WriteLine($"[EasyOCR] Script Python não encontrado: {_pythonScriptPath}");
                foreach (var path in imagePaths)
                    onResult(path, false, []);
                return;
            }

            string tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllLinesAsync(tempFile, imagePaths, Encoding.UTF8, cancellationToken);

                // 1 worker = 1 modelo EasyOCR em memória (~2GB vs ~5GB com 2 workers)
                var processInfo = BuildProcessInfo($"-u \"{_pythonScriptPath}\" --batch \"{tempFile}\" --workers 1");
                processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("[EasyOCR] Falha ao iniciar processo Python");
                    foreach (var path in imagePaths)
                        onResult(path, false, []);
                    return;
                }

                // Prioridade baixa para não congelar o PC durante o processamento
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        int jsonStart = line.IndexOf('{');
                        if (jsonStart > 0) line = line[jsonStart..];

                        var result = JsonSerializer.Deserialize<BatchImageResult>(line);
                        if (result == null) continue;

                        var textRegions = ParseRegions(result);
                        bool hasText = result.Success == true && textRegions.Count > 0;
                        onResult(result.Image ?? "", hasText, textRegions);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EasyOCR] Erro ao processar linha: {ex.Message}");
                    }
                }

                await process.WaitForExitAsync(cancellationToken);
                string errorOutput = await stderrTask;
                if (!string.IsNullOrEmpty(errorOutput))
                    Console.WriteLine($"[EasyOCR] Stderr: {errorOutput}");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        /// <summary>Detecta texto em uma única imagem (modo compatibilidade).</summary>
        public bool DetectTextInImage(string imagePath, out List<Models.TextRegion> textRegions)
        {
            textRegions = [];

            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"[EasyOCR] Arquivo não encontrado: {imagePath}");
                return false;
            }

            if (!File.Exists(_pythonScriptPath))
            {
                Console.WriteLine($"[EasyOCR] Script Python não encontrado: {_pythonScriptPath}");
                return false;
            }

            try
            {
                var processInfo = BuildProcessInfo($"-u \"{_pythonScriptPath}\" \"{imagePath}\"");
                processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("[EasyOCR] Falha ao iniciar processo Python");
                    return false;
                }

                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
                var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
                process.WaitForExit(180000);
                string output = stdoutTask.Result;
                string errorOutput = stderrTask.Result;

                if (!string.IsNullOrEmpty(errorOutput))
                    Console.WriteLine($"[EasyOCR] Stderr: {errorOutput}");

                if (string.IsNullOrEmpty(output))
                {
                    Console.WriteLine("[EasyOCR] Sem resposta do Python");
                    return false;
                }

                int jsonStart = output.IndexOf('{');
                if (jsonStart < 0) return false;

                var response = JsonSerializer.Deserialize<OcrResponse>(output[jsonStart..]);
                if (response?.Success != true || response.Regions == null) return false;

                textRegions = ParseRegions(response);
                return textRegions.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EasyOCR] Exceção: {ex.Message}");
                return false;
            }
        }

        private static List<Models.TextRegion> ParseRegions(OcrResponse response)
        {
            var list = new List<Models.TextRegion>();
            if (response.Regions == null) return list;
            foreach (var region in response.Regions)
            {
                if (string.IsNullOrEmpty(region.Text)) continue;
                list.Add(new Models.TextRegion(region.Text, new Rect(region.X, region.Y, region.Width, region.Height), region.Confidence));
            }
            return list;
        }
    }

    internal class OcrResponse
    {
        [JsonPropertyName("success")]     public bool? Success { get; set; }
        [JsonPropertyName("error")]       public string? Error { get; set; }
        [JsonPropertyName("text_count")]  public int TextCount { get; set; }
        [JsonPropertyName("regions")]     public List<OcrRegion>? Regions { get; set; }
        [JsonPropertyName("has_people")]  public bool? HasPeople { get; set; }
    }

    internal class BatchImageResult : OcrResponse
    {
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    internal class OcrRegion
    {
        [JsonPropertyName("text")]       public string? Text { get; set; }
        [JsonPropertyName("x")]          public int X { get; set; }
        [JsonPropertyName("y")]          public int Y { get; set; }
        [JsonPropertyName("width")]      public int Width { get; set; }
        [JsonPropertyName("height")]     public int Height { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
    }
}
