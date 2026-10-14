using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImageTextDetector.Services;
using Microsoft.Win32;
using System.IO;

namespace ImageTextDetector
{
    public partial class MainWindow : Window
    {
        private ImageProcessingService? _processingService;
        private bool _isProcessing = false;
        private string? _referenceFacePath;
        private string? _noFaceFoundFolder;
        private string? _otherFaceFolder;
        private double _tolerance = 0.55;

        public MainWindow()
        {
            InitializeComponent();
            OutputFolderTextBox.Text = @"C:\Users\fabio\Desktop\Eu\eu";
            _noFaceFoundFolder = @"C:\Users\fabio\OneDrive\Imagens\Img4Exclude\Sem rosto";
            NoFaceFoundFolderTextBox.Text = _noFaceFoundFolder;
            _otherFaceFolder = @"C:\Users\fabio\OneDrive\Imagens\Img4Exclude\Com rosto";
            OtherFaceFolderTextBox.Text = _otherFaceFolder;
        }

        private void BrowseReferenceFaceButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecione a imagem do rosto de referência",
                Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp|Todos os arquivos|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                _referenceFacePath = dialog.FileName;
                ReferenceFaceTextBox.Text = _referenceFacePath;
                InitializeProcessingService();
                UpdateProgress($"✓ Imagem de referência carregada: {Path.GetFileName(_referenceFacePath)}");
            }
        }

        private void BrowseReferenceFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a pasta com várias fotos do rosto de referência"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _referenceFacePath = dialog.SelectedPath;
                ReferenceFaceTextBox.Text = _referenceFacePath;
                InitializeProcessingService();

                int count = 0;
                try
                {
                    var exts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };
                    count = Directory.EnumerateFiles(_referenceFacePath)
                        .Count(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()));
                }
                catch { }

                UpdateProgress($"✓ Pasta de referência carregada: {Path.GetFileName(_referenceFacePath)} ({count} imagem(ns))");
            }
        }

        /// <summary>Cria (ou recria) o serviço de processamento com a referência e tolerância atuais.</summary>
        private void InitializeProcessingService()
        {
            if (string.IsNullOrEmpty(_referenceFacePath)) return;
            _processingService = new ImageProcessingService(_referenceFacePath, _tolerance);
            _processingService.OnProgress += UpdateProgress;
            _processingService.OnSummary += UpdateSummary;
        }

        private void ToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _tolerance = Math.Round(e.NewValue, 2);
            if (ToleranceValueLabel != null)
                ToleranceValueLabel.Text = _tolerance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            // Recria o serviço se já houver referência, para aplicar o novo rigor
            if (!string.IsNullOrEmpty(_referenceFacePath))
                InitializeProcessingService();
        }

        private void BrowseSourceButton1_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a 1ª pasta de origem contendo as imagens"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourceFolder1TextBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseSourceButton2_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a 2ª pasta de origem contendo as imagens"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourceFolder2TextBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseSourceButton3_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a 3ª pasta de origem contendo as imagens"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourceFolder3TextBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a pasta de destino para as imagens recortadas"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputFolderTextBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseNoFaceFoundButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a pasta de destino para as imagens sem rosto"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _noFaceFoundFolder = dialog.SelectedPath;
                NoFaceFoundFolderTextBox.Text = _noFaceFoundFolder;
            }
        }

        private void BrowseOtherFaceButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a pasta de destino para as imagens com outro rosto (recortadas)"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _otherFaceFolder = dialog.SelectedPath;
                OtherFaceFolderTextBox.Text = _otherFaceFolder;
            }
        }

        private void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                MessageBox.Show("Um processamento já está em andamento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_referenceFacePath) || !File.Exists(_referenceFacePath))
            {
                MessageBox.Show("Por favor, selecione uma imagem de rosto de referência.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sourceFolder1 = SourceFolder1TextBox.Text.Trim();
            var sourceFolder2 = SourceFolder2TextBox.Text.Trim();
            var sourceFolder3 = SourceFolder3TextBox.Text.Trim();
            var outputFolder = OutputFolderTextBox.Text.Trim();
            var noFaceFoundFolder = NoFaceFoundFolderTextBox.Text.Trim();
            var otherFaceFolder = OtherFaceFolderTextBox.Text.Trim();

            // Validar se pelo menos uma pasta de origem foi selecionada
            if (string.IsNullOrEmpty(sourceFolder1) && string.IsNullOrEmpty(sourceFolder2) && string.IsNullOrEmpty(sourceFolder3))
            {
                MessageBox.Show("Por favor, selecione ao menos uma pasta de origem.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(outputFolder))
            {
                MessageBox.Show("Por favor, selecione a pasta de saída.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validar que as pastas de origem existem
            var sourceFolders = new List<string>();
            if (!string.IsNullOrEmpty(sourceFolder1))
            {
                if (!Directory.Exists(sourceFolder1))
                {
                    MessageBox.Show("A 1ª pasta de origem não existe.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                sourceFolders.Add(sourceFolder1);
            }

            if (!string.IsNullOrEmpty(sourceFolder2))
            {
                if (!Directory.Exists(sourceFolder2))
                {
                    MessageBox.Show("A 2ª pasta de origem não existe.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                sourceFolders.Add(sourceFolder2);
            }

            if (!string.IsNullOrEmpty(sourceFolder3))
            {
                if (!Directory.Exists(sourceFolder3))
                {
                    MessageBox.Show("A 3ª pasta de origem não existe.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                sourceFolders.Add(sourceFolder3);
            }

            _isProcessing = true;
            ProcessButton.IsEnabled = false;
            ProgressTextBox.Clear();

            // Run processing in background thread
            Task.Run(async () =>
            {
                try
                {
                    if (_processingService != null)
                        await _processingService.ProcessFolders(sourceFolders, outputFolder, noFaceFoundFolder, otherFaceFolder);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateProgress($"Erro durante o processamento: {ex.Message}");
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isProcessing = false;
                        ProcessButton.IsEnabled = true;
                    });
                }
            });
        }

        private void InsightFaceButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new InsightFaceWindow { Owner = this };
            window.Show();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ReferenceFaceTextBox.Clear();
            SourceFolder1TextBox.Clear();
            SourceFolder2TextBox.Clear();
            SourceFolder3TextBox.Clear();
            OutputFolderTextBox.Clear();
            NoFaceFoundFolderTextBox.Clear();
            OtherFaceFolderTextBox.Clear();
            ProgressTextBox.Clear();
            TotalImagesLabel.Text = "0";
            ImagesWithFaceLabel.Text = "0";
            MovedImagesLabel.Text = "0";
            NoFaceFoundLabel.Text = "0";
            OtherFaceLabel.Text = "0";
        }

        private void UpdateProgress(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressTextBox.AppendText(message + Environment.NewLine);
                ProgressTextBox.ScrollToEnd();
            });
        }

        private void UpdateSummary(int total, int withFace, int moved, int noFaceFound, int otherFace)
        {
            Dispatcher.Invoke(() =>
            {
                TotalImagesLabel.Text = total.ToString();
                ImagesWithFaceLabel.Text = withFace.ToString();
                MovedImagesLabel.Text = moved.ToString();
                NoFaceFoundLabel.Text = noFaceFound.ToString();
                OtherFaceLabel.Text = otherFace.ToString();
            });
        }
    }
}