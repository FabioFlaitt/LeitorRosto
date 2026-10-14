using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImageTextDetector.Services;

namespace ImageTextDetector
{
    public partial class InsightFaceWindow : Window
    {
        private double _simThreshold = 0.42;
        private double _dupSimThreshold = 0.50;
        private bool _busy = false;

        private readonly ObservableCollection<DuplicateGroupVm> _groups = new();
        private DuplicateResult? _lastDupResult;

        public InsightFaceWindow()
        {
            InitializeComponent();
            DupGroupsList.ItemsSource = _groups;
        }

        // ─────────────────────────── Helpers de diálogo ───────────────────────────

        private static string? PickFolder(string description)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        private static string? PickImageFile(string title)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.tiff;*.webp|Todos os arquivos|*.*",
                CheckFileExists = true
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        // ═══════════════════════ ABA 1: RECONHECIMENTO ═══════════════════════

        private void BrowseRefFile_Click(object sender, RoutedEventArgs e)
        {
            var f = PickImageFile("Selecione a foto do rosto de referência");
            if (f != null) RefTextBox.Text = f;
        }

        private void BrowseRefFolder_Click(object sender, RoutedEventArgs e)
        {
            var f = PickFolder("Selecione a pasta com várias fotos do rosto de referência");
            if (f != null) RefTextBox.Text = f;
        }

        private void BrowseSrc1_Click(object sender, RoutedEventArgs e) { var f = PickFolder("1ª pasta de origem"); if (f != null) Src1TextBox.Text = f; }
        private void BrowseSrc2_Click(object sender, RoutedEventArgs e) { var f = PickFolder("2ª pasta de origem"); if (f != null) Src2TextBox.Text = f; }
        private void BrowseSrc3_Click(object sender, RoutedEventArgs e) { var f = PickFolder("3ª pasta de origem"); if (f != null) Src3TextBox.Text = f; }
        private void BrowseOut_Click(object sender, RoutedEventArgs e) { var f = PickFolder("Pasta de destino (rosto alvo)"); if (f != null) OutTextBox.Text = f; }
        private void BrowseOther_Click(object sender, RoutedEventArgs e) { var f = PickFolder("Pasta para outro rosto"); if (f != null) OtherTextBox.Text = f; }
        private void BrowseNoFace_Click(object sender, RoutedEventArgs e) { var f = PickFolder("Pasta para imagens sem rosto"); if (f != null) NoFaceTextBox.Text = f; }

        private void SimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _simThreshold = Math.Round(e.NewValue, 2);
            if (SimLabel != null) SimLabel.Text = _simThreshold.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void RecLog(string msg) => Dispatcher.Invoke(() =>
        {
            RecProgressTextBox.AppendText(msg + Environment.NewLine);
            RecProgressTextBox.ScrollToEnd();
        });

        private async void RecProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) { MessageBox.Show("Aguarde a operação atual terminar."); return; }

            string reference = RefTextBox.Text.Trim();
            if (string.IsNullOrEmpty(reference) || !(File.Exists(reference) || Directory.Exists(reference)))
            {
                MessageBox.Show("Selecione uma foto ou pasta de referência válida.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sources = new[] { Src1TextBox.Text, Src2TextBox.Text, Src3TextBox.Text }
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (sources.Count == 0) { MessageBox.Show("Selecione ao menos uma pasta de origem.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            foreach (var s in sources)
                if (!Directory.Exists(s)) { MessageBox.Show($"Pasta de origem não existe:\n{s}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            string output = OutTextBox.Text.Trim();
            if (string.IsNullOrEmpty(output)) { MessageBox.Show("Selecione a pasta de destino (alvo).", "Erro", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            _busy = true;
            RecProcessButton.IsEnabled = false;
            RecProgressTextBox.Clear();
            RecLog("Iniciando reconhecimento com ArcFace (InsightFace)...");

            var service = new ImageProcessingService(reference, _simThreshold, "auto", FaceEngine.InsightFace);
            service.OnProgress += RecLog;
            service.OnSummary += (total, withFace, moved, noFace, other) =>
                RecLog($"\n✓ Concluído — total {total}, alvo {withFace}, movidas {moved}, outro {other}, sem rosto {noFace}");

            try
            {
                await Task.Run(() => service.ProcessFolders(sources, output, NoFaceTextBox.Text.Trim(), OtherTextBox.Text.Trim()));
            }
            catch (Exception ex)
            {
                RecLog($"Erro: {ex.Message}");
            }
            finally
            {
                _busy = false;
                RecProcessButton.IsEnabled = true;
            }
        }

        // ═══════════════════════ ABA 2: FOTOS REPETIDAS ═══════════════════════

        private void BrowseDupFolder_Click(object sender, RoutedEventArgs e)
        {
            var f = PickFolder("Selecione a pasta a analisar");
            if (f != null) DupFolderTextBox.Text = f;
        }

        private void DupSimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _dupSimThreshold = Math.Round(e.NewValue, 2);
            if (DupSimLabel != null) DupSimLabel.Text = _dupSimThreshold.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void DupLog(string msg) => Dispatcher.Invoke(() =>
        {
            DupProgressTextBox.AppendText(msg + Environment.NewLine);
            DupProgressTextBox.ScrollToEnd();
        });

        private async void DupAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) { MessageBox.Show("Aguarde a operação atual terminar."); return; }

            string folder = DupFolderTextBox.Text.Trim();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("Selecione uma pasta válida.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _busy = true;
            DupAnalyzeButton.IsEnabled = false;
            DupMoveButton.IsEnabled = false;
            _groups.Clear();
            _lastDupResult = null;
            DupProgressTextBox.Clear();
            DupStatusLabel.Text = "Coletando imagens...";

            var finder = new DuplicateFinderService();
            var files = finder.GetImageFiles(folder, DupRecursiveCheck.IsChecked == true);

            if (files.Count == 0)
            {
                DupStatusLabel.Text = "Nenhuma imagem encontrada.";
                _busy = false;
                DupAnalyzeButton.IsEnabled = true;
                return;
            }

            DupLog($"{files.Count} imagens. Analisando com ArcFace (sim ≥ {_dupSimThreshold:0.00})...");
            DupStatusLabel.Text = $"Analisando 0/{files.Count}...";

            try
            {
                var result = await finder.FindDuplicatesAsync(
                    files, _dupSimThreshold,
                    (done, total, name) =>
                    {
                        if (done % 10 == 0 || done == total)
                            Dispatcher.Invoke(() => DupStatusLabel.Text = $"Analisando {done}/{total}...");
                    });

                _lastDupResult = result;

                if (!string.IsNullOrEmpty(result.Error))
                {
                    DupStatusLabel.Text = "Erro na análise.";
                    DupLog($"Erro: {result.Error}");
                    return;
                }

                foreach (var g in result.Groups)
                {
                    _groups.Add(new DuplicateGroupVm
                    {
                        Count = g.Count,
                        AvgSimilarityText = g.AvgSimilarity.ToString("0.000", CultureInfo.InvariantCulture),
                        Files = g.Files
                    });
                }

                int extras = result.Groups.Sum(g => Math.Max(0, g.Count - 1));
                DupStatusLabel.Text = $"{result.DuplicateGroups} grupo(s) de repetidas · {result.WithFace}/{result.Total} com rosto · {extras} foto(s) extra(s) para remover";
                DupLog($"Concluído: {result.DuplicateGroups} grupo(s), {result.DuplicateFiles} arquivo(s) envolvidos.");
                DupMoveButton.IsEnabled = extras > 0;
            }
            catch (Exception ex)
            {
                DupStatusLabel.Text = "Erro.";
                DupLog($"Exceção: {ex.Message}");
            }
            finally
            {
                _busy = false;
                DupAnalyzeButton.IsEnabled = true;
            }
        }

        private void DupMove_Click(object sender, RoutedEventArgs e)
        {
            if (_lastDupResult == null || _lastDupResult.Groups.Count == 0) return;

            var dest = PickFolder("Pasta para mover as fotos repetidas (uma cópia de cada grupo será mantida na origem)");
            if (dest == null) return;

            int moved = 0, failed = 0;
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex) { MessageBox.Show($"Não foi possível criar a pasta:\n{ex.Message}"); return; }

            foreach (var g in _lastDupResult.Groups)
            {
                // Mantém o 1º arquivo de cada grupo; move os demais.
                foreach (var file in g.Files.Skip(1))
                {
                    try
                    {
                        if (!File.Exists(file)) continue;
                        string target = Path.Combine(dest, Path.GetFileName(file));
                        target = EnsureUniquePath(target);
                        File.Move(file, target);
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        DupLog($"Falha ao mover {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }

            DupLog($"Movidas {moved} foto(s) repetida(s) para: {dest}" + (failed > 0 ? $" ({failed} falha(s))" : ""));
            MessageBox.Show($"{moved} foto(s) repetida(s) movida(s).\n{failed} falha(s).", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            DupMoveButton.IsEnabled = false;
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int i = 1;
            string candidate;
            do { candidate = Path.Combine(dir, $"{name}_{i}{ext}"); i++; }
            while (File.Exists(candidate));
            return candidate;
        }
    }

    public class DuplicateGroupVm
    {
        public int Count { get; set; }
        public string AvgSimilarityText { get; set; } = "";
        public List<string> Files { get; set; } = new();
    }
}
