using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextDetector.Services
{
    /// <summary>Um grupo de fotos consideradas repetidas (mesmo rosto).</summary>
    public class DuplicateGroup
    {
        public List<string> Files { get; set; } = new();
        public int Count { get; set; }
        public double AvgSimilarity { get; set; }
    }

    public class DuplicateResult
    {
        public List<DuplicateGroup> Groups { get; set; } = new();
        public int Total { get; set; }
        public int WithFace { get; set; }
        public int DuplicateGroups { get; set; }
        public int DuplicateFiles { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Localiza fotos repetidas (mesmo rosto) numa lista de imagens usando o
    /// motor ArcFace (insightface_service.py --duplicates).
    /// </summary>
    public class DuplicateFinderService
    {
        private readonly string _scriptPath;
        private readonly string _pythonExePath;
        private readonly string[] _supportedFormats =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

        public DuplicateFinderService()
        {
            _scriptPath = FaceRecognitionService.FindScript(AppDomain.CurrentDomain.BaseDirectory, "insightface_service.py");
            _pythonExePath = FaceRecognitionService.FindVenvPython(AppDomain.CurrentDomain.BaseDirectory);
        }

        public List<string> GetImageFiles(string folder, bool recursive)
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = new List<string>();
            foreach (var fmt in _supportedFormats)
                files.AddRange(Directory.GetFiles(folder, $"*{fmt}", option));
            return files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Analisa a pasta e devolve os grupos de fotos repetidas.
        /// onProgress(processadas, total, nomeArquivo) é chamado durante a análise.
        /// </summary>
        public async Task<DuplicateResult> FindDuplicatesAsync(
            IList<string> imagePaths,
            double simThreshold,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new DuplicateResult { Total = imagePaths.Count };

            if (imagePaths.Count == 0)
                return result;

            if (!File.Exists(_scriptPath))
            {
                result.Error = $"Script não encontrado: {_scriptPath}";
                return result;
            }

            string tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllLinesAsync(tempFile, imagePaths, Encoding.UTF8, cancellationToken);

                string thr = simThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExePath,
                    Arguments = $"-u \"{_scriptPath}\" --duplicates \"{tempFile}\" --sim-threshold {thr}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";

                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.Error = "Falha ao iniciar o processo Python.";
                    return result;
                }

                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                var stderrTask = Task.Run(async () =>
                {
                    string? l;
                    while ((l = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                        Console.WriteLine($"[Duplicates] STDERR: {l}");
                });

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    int jsonStart = line.IndexOf('{');
                    if (jsonStart < 0) continue;
                    line = line[jsonStart..];

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("progress", out var prog))
                        {
                            int done = prog.GetInt32();
                            int total = root.TryGetProperty("total", out var t) ? t.GetInt32() : imagePaths.Count;
                            string name = root.TryGetProperty("image", out var im) ? (im.GetString() ?? "") : "";
                            onProgress?.Invoke(done, total, name);
                            continue;
                        }

                        if (root.TryGetProperty("groups", out _) || root.TryGetProperty("error", out _))
                        {
                            var final = JsonSerializer.Deserialize<DuplicatePayload>(line);
                            if (final != null)
                            {
                                if (!string.IsNullOrEmpty(final.Error))
                                {
                                    result.Error = final.Error;
                                }
                                else
                                {
                                    result.WithFace = final.WithFace;
                                    result.DuplicateGroups = final.DuplicateGroups;
                                    result.DuplicateFiles = final.DuplicateFiles;
                                    result.Total = final.Total > 0 ? final.Total : result.Total;
                                    foreach (var g in final.Groups ?? new List<DuplicateGroupDto>())
                                    {
                                        result.Groups.Add(new DuplicateGroup
                                        {
                                            Files = g.Files ?? new List<string>(),
                                            Count = g.Count,
                                            AvgSimilarity = g.AvgSimilarity,
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Duplicates] Erro ao parsear linha: {ex.Message}");
                    }
                }

                await process.WaitForExitAsync(cancellationToken);
                await stderrTask;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }

            return result;
        }

        private class DuplicatePayload
        {
            [JsonPropertyName("groups")] public List<DuplicateGroupDto>? Groups { get; set; }
            [JsonPropertyName("total")] public int Total { get; set; }
            [JsonPropertyName("with_face")] public int WithFace { get; set; }
            [JsonPropertyName("duplicate_groups")] public int DuplicateGroups { get; set; }
            [JsonPropertyName("duplicate_files")] public int DuplicateFiles { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
        }

        private class DuplicateGroupDto
        {
            [JsonPropertyName("files")] public List<string>? Files { get; set; }
            [JsonPropertyName("count")] public int Count { get; set; }
            [JsonPropertyName("avg_similarity")] public double AvgSimilarity { get; set; }
        }
    }
}
