using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTextDetector.Services
{
    public class TextDetectionService
    {
        private readonly EasyOcrService _easyOcr;

        public TextDetectionService()
        {
            try
            {
                _easyOcr = new EasyOcrService();
                Console.WriteLine("✅ EasyOCR Service inicializado com sucesso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao inicializar EasyOCR: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Processa todas as imagens em lote em um único processo Python.
        /// onResult(imagePath, resultado_analise) é chamado para cada imagem processada.
        /// </summary>
        public async Task BatchDetectImages(IList<string> imagePaths, Action<string, Models.ImageAnalysisResult> onResult)
            => await _easyOcr.BatchDetectImagesAsync(imagePaths, onResult);

        /// <summary>
        /// Processa todas as imagens em lote (compatibilidade com versão anterior).
        /// onResult(imagePath, temTexto, regiões) é chamado para cada imagem processada.
        /// </summary>
        public async Task BatchDetectImages(IList<string> imagePaths, Action<string, bool, List<Models.TextRegion>> onResultLegacy)
        {
            await _easyOcr.BatchDetectImagesAsync(imagePaths, (imagePath, result) =>
            {
                onResultLegacy?.Invoke(imagePath, result.HasText, result.TextRegions);
            });
        }

        public bool DetectTextInImage(string imagePath, out List<Models.TextRegion> textRegions)
        {
            try
            {
                bool success = _easyOcr.DetectTextInImage(imagePath, out textRegions);
                Console.WriteLine(success
                    ? $"✅ Detectadas {textRegions.Count} regiões de texto"
                    : "ℹ️ Nenhum texto detectado nesta imagem");
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao detectar texto: {ex.Message}");
                textRegions = [];
                return false;
            }
        }
    }
}
