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
    /// <summary>Motor de reconhecimento facial.</summary>
    public enum FaceEngine
    {
        /// <summary>dlib / face_recognition (HOG/CNN, 128-d).</summary>
        Dlib,
        /// <summary>InsightFace / ArcFace (SCRFD, 512-d) — mais preciso.</summary>
        InsightFace
    }

    public class FaceRecognitionService
    {
        private readonly string _pythonScriptPath;
        private readonly string _pythonExePath;
        private readonly string? _referenceFacePath;
        private readonly double _tolerance;
        private readonly string _detectionModel;
        private readonly FaceEngine _engine;

        /// <param name="referenceFacePath">Arquivo de imagem OU pasta com várias fotos do rosto alvo.</param>
        /// <param name="tolerance">dlib: limite de distância (padrão 0.55). InsightFace: similaridade mínima de cosseno (padrão 0.42).</param>
        /// <param name="detectionModel">Apenas dlib: "hog", "cnn" ou "auto".</param>
        /// <param name="engine">Motor: dlib ou InsightFace (ArcFace).</param>
        public FaceRecognitionService(string? referenceFacePath = null, double tolerance = 0.55, string detectionModel = "auto", FaceEngine engine = FaceEngine.Dlib)
        {
            _engine = engine;
            string scriptName = engine == FaceEngine.InsightFace ? "insightface_service.py" : "face_service.py";
            _pythonScriptPath = FindScript(AppDomain.CurrentDomain.BaseDirectory, scriptName);
            _pythonExePath = FindVenvPython(AppDomain.CurrentDomain.BaseDirectory);
            _referenceFacePath = referenceFacePath;
            _tolerance = tolerance;
            _detectionModel = string.IsNullOrWhiteSpace(detectionModel) ? "auto" : detectionModel;

            Console.WriteLine($"[FaceRecognition] Motor: {engine}, Script: {_pythonScriptPath}");
            Console.WriteLine($"[FaceRecognition] Existe: {File.Exists(_pythonScriptPath)}");
            if (!string.IsNullOrEmpty(referenceFacePath))
                Console.WriteLine($"[FaceRecognition] Reference: {referenceFacePath}");
        }

        internal static string FindScript(string startDir, string scriptName)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, scriptName);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", scriptName));
        }

        internal static string FindVenvPython(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string venvPython = Path.Combine(dir.FullName, "face_venv", "Scripts", "python.exe");
                if (File.Exists(venvPython))
                    return venvPython;
                dir = dir.Parent;
            }
            return "py";
        }

        /// <summary>A referência é válida se for um arquivo OU uma pasta existente.</summary>
        private bool ReferenceExists() =>
            !string.IsNullOrEmpty(_referenceFacePath) &&
            (File.Exists(_referenceFacePath) || Directory.Exists(_referenceFacePath));

        private string CommonArgs()
        {
            string t = _tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return _engine == FaceEngine.InsightFace
                ? $"--reference \"{_referenceFacePath}\" --sim-threshold {t}"
                : $"--reference \"{_referenceFacePath}\" --tolerance {t} --model {_detectionModel}";
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
        /// Processa um lote de imagens com detecção e reconhecimento de rostos.
        /// onResult é chamado para cada imagem com resultado completo de análise.
        /// </summary>
        public async Task BatchDetectFacesAsync(
            IList<string> imagePaths,
            Action<string, Models.FaceAnalysisResult> onResult,
            CancellationToken cancellationToken = default)
        {
            if (imagePaths.Count == 0) return;

            if (!ReferenceExists())
            {
                Console.WriteLine($"[FaceRecognition] Referência não encontrada: {_referenceFacePath}");
                foreach (var path in imagePaths)
                    onResult(path, new Models.FaceAnalysisResult(false, false, false, 0, []));
                return;
            }

            if (!File.Exists(_pythonScriptPath))
            {
                Console.WriteLine($"[FaceRecognition] Script Python não encontrado: {_pythonScriptPath}");
                foreach (var path in imagePaths)
                    onResult(path, new Models.FaceAnalysisResult(false, false, false, 0, []));
                return;
            }

            string tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllLinesAsync(tempFile, imagePaths, Encoding.UTF8, cancellationToken);

                var processInfo = BuildProcessInfo(
                    $"-u \"{_pythonScriptPath}\" --batch \"{tempFile}\" {CommonArgs()} --workers 1");
                processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("[FaceRecognition] Falha ao iniciar processo Python");
                    foreach (var path in imagePaths)
                        onResult(path, new Models.FaceAnalysisResult(false, false, false, 0, []));
                    return;
                }

                // Prioridade baixa para não congelar o PC durante o processamento
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                // Capturar stderr em tempo real
                var stderrLines = new StringBuilder();
                var stderrTask = Task.Run(async () =>
                {
                    string? stderrLine;
                    while ((stderrLine = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                    {
                        Console.WriteLine($"[FaceRecognition] STDERR: {stderrLine}");
                        stderrLines.AppendLine(stderrLine);
                    }
                });

                string? line;
                int lineCount = 0;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    
                    lineCount++;
                    Console.WriteLine($"[FaceRecognition] Linha {lineCount} recebida: {line.Substring(0, Math.Min(500, line.Length))}...");

                    try
                    {
                        int jsonStart = line.IndexOf('{');
                        if (jsonStart >= 0) line = line[jsonStart..];  // Corrigido: >= ao invés de >

                        var result = JsonSerializer.Deserialize<BatchFaceResult>(line);
                        if (result == null)
                        {
                            Console.WriteLine($"[FaceRecognition] Falha ao desserializar linha {lineCount}");
                            continue;
                        }

                        // Validar e corrigir image path se vazio
                        if (string.IsNullOrEmpty(result.Image) && result.Index >= 0 && result.Index < imagePaths.Count)
                        {
                            result.Image = imagePaths[result.Index];
                        }

                        if (string.IsNullOrEmpty(result.Image))
                        {
                            Console.WriteLine($"[FaceRecognition] Aviso: Image path vazio no resultado do índice {result.Index}");
                            continue;
                        }

                        var matches = ParseMatches(result);
                        bool hasFaces = result.FacesDetected > 0;
                        bool hasTarget = result.HasTargetFace ?? false;
                        bool hasPerson = result.HasPerson ?? hasFaces;

                        Console.WriteLine($"[FaceRecognition] ✓ Processada: {Path.GetFileName(result.Image)} (idx={result.Index + 1}/{result.Total}) → Target={hasTarget}, Rostos={hasFaces}, Pessoa={hasPerson}");
                        var analysisResult = new Models.FaceAnalysisResult(hasFaces, hasTarget, hasPerson, result.FacesDetected ?? 0, matches);
                        onResult(result.Image, analysisResult);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FaceRecognition] ✗ Erro ao processar linha {lineCount}: {ex.Message}");
                    }
                }

                Console.WriteLine($"[FaceRecognition] Fim do stdout. Total de linhas JSON: {lineCount}");
                await process.WaitForExitAsync(cancellationToken);
                Console.WriteLine($"[FaceRecognition] Processo Python finalizou com exit code: {process.ExitCode}");
                
                await stderrTask;
                string errorOutput = stderrLines.ToString();
                if (!string.IsNullOrEmpty(errorOutput))
                    Console.WriteLine($"[FaceRecognition] Resumo Stderr:\n{errorOutput}");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        /// <summary>Detecta rostos em uma única imagem.</summary>
        public bool DetectFacesInImage(string imagePath, out List<Models.FaceMatch> faceMatches)
        {
            faceMatches = [];

            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"[FaceRecognition] Arquivo não encontrado: {imagePath}");
                return false;
            }

            if (!ReferenceExists())
            {
                Console.WriteLine($"[FaceRecognition] Referência não encontrada: {_referenceFacePath}");
                return false;
            }

            if (!File.Exists(_pythonScriptPath))
            {
                Console.WriteLine($"[FaceRecognition] Script Python não encontrado: {_pythonScriptPath}");
                return false;
            }

            try
            {
                var processInfo = BuildProcessInfo(
                    $"-u \"{_pythonScriptPath}\" \"{imagePath}\" {CommonArgs()}");
                processInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                processInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("[FaceRecognition] Falha ao iniciar processo Python");
                    return false;
                }

                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
                var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
                process.WaitForExit(180000);
                string output = stdoutTask.Result;
                string errorOutput = stderrTask.Result;

                if (!string.IsNullOrEmpty(errorOutput))
                    Console.WriteLine($"[FaceRecognition] Stderr: {errorOutput}");

                if (string.IsNullOrEmpty(output))
                {
                    Console.WriteLine("[FaceRecognition] Sem resposta do Python");
                    return false;
                }

                int jsonStart = output.IndexOf('{');
                if (jsonStart < 0) return false;

                var response = JsonSerializer.Deserialize<FaceResponse>(output[jsonStart..]);
                if (response?.Success != true || response.Matches == null) return false;

                faceMatches = ParseMatches(response);
                return faceMatches.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceRecognition] Exceção: {ex.Message}");
                return false;
            }
        }

        private static List<Models.FaceMatch> ParseMatches(FaceResponse response)
        {
            var list = new List<Models.FaceMatch>();
            if (response.Matches == null) return list;
            foreach (var match in response.Matches)
            {
                var boundingBox = new Rect(match.X, match.Y, match.Width, match.Height);
                list.Add(new Models.FaceMatch(
                    match.Index,
                    match.Match ?? false,
                    match.Distance ?? 0,
                    match.Tolerance ?? 0.55,
                    boundingBox,
                    match.Confidence ?? 0
                ));
            }
            return list;
        }
    }

    internal class FaceResponse
    {
        [JsonPropertyName("success")]           public bool? Success { get; set; }
        [JsonPropertyName("error")]             public string? Error { get; set; }
        [JsonPropertyName("faces_detected")]    public int? FacesDetected { get; set; }
        [JsonPropertyName("has_target_face")]   public bool? HasTargetFace { get; set; }
        [JsonPropertyName("has_person")]        public bool? HasPerson { get; set; }
        [JsonPropertyName("matches")]           public List<FaceMatchDto>? Matches { get; set; }
    }

    internal class BatchFaceResult : FaceResponse
    {
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    internal class FaceMatchDto
    {
        [JsonPropertyName("index")]     public int Index { get; set; }
        [JsonPropertyName("match")]     public bool? Match { get; set; }
        [JsonPropertyName("distance")]  public double? Distance { get; set; }
        [JsonPropertyName("confidence")] public double? Confidence { get; set; }
        [JsonPropertyName("tolerance")] public double? Tolerance { get; set; }
        [JsonPropertyName("x")]         public int X { get; set; }
        [JsonPropertyName("y")]         public int Y { get; set; }
        [JsonPropertyName("width")]     public int Width { get; set; }
        [JsonPropertyName("height")]    public int Height { get; set; }
    }
}
