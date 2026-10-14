# Detector e Reconhecimento Facial - .NET WPF

Um aplicativo desktop .NET WPF que detecta e reconhece rostos específicos em imagens. Carregue uma imagem de referência do rosto alvo e o sistema processará múltiplas pastas, movendo apenas as imagens que contêm aquele rosto específico.

## 🎯 O que foi Refatorado

**De**: Detector de Texto em Imagens (OCR + detecção de pessoas)  
**Para**: Detector e Reconhecimento Facial

### Principais Mudanças
- ❌ **Removido**: Detecção de texto via OCR (EasyOCR/Tesseract)
- ❌ **Removido**: Detecção genérica de pessoas (YOLO)
- ✅ **Adicionado**: Reconhecimento facial específico (face_recognition library)
- ✅ **Adicionado**: Comparação de embeddings faciais usando dlib
- ✅ **Refatorado**: UI para carregar imagem de referência do rosto
- ✅ **Refatorado**: Serviço Python `face_service.py` para face recognition
- ✅ **Refatorado**: Models `FaceMatch` e `FaceAnalysisResult`
- ✅ **Refatorado**: Service `FaceRecognitionService` para substituir `TextDetectionService`

## 🎯 Funcionalidades Atuais

- ✅ **Reconhecimento Facial**: Usa face_recognition (dlib) para detectar e reconhecer rostos
- ✅ **Correspondência de Embedding**: Compara embeddings faciais entre imagens
- ✅ **Múltiplas Pastas de Origem**: Processe 3 locais diferentes simultaneamente
- ✅ **Pasta Configurável**: Selecione origem e destino via UI
- ✅ **Suporte Multi-formato**: JPG, PNG, BMP, GIF, TIFF, WebP
- ✅ **Progresso em Tempo Real**: Visualize o processamento em tempo real
- ✅ **Estatísticas**: Resumo de imagens processadas e encontradas

## 📋 Requisitos

### Sistema Operacional
- Windows 7 ou superior

### Software Necessário
- **Python 3.8+** com pip
- **.NET 8.0** Runtime ou SDK
- **face_recognition** (instalado via pip)
- **opencv-python** (instalado via pip)

## 🔧 Instalação e Configuração

### 1. Clonar ou Baixar o Projeto

```bash
git clone <repository-url>
cd "Leitor - Rosto"
```

### 2. Instalar Dependências Python

**Opção A: Script Automatizado (Windows)**
```bash
install_dependencies.bat
```

**Opção B: Script Python**
```bash
python check_dependencies.py
```

**Opção C: Manual**
```bash
pip install face_recognition opencv-python
```

### 3. Restaurar Dependências .NET

```bash
cd ImageTextDetector
dotnet restore
```

### 4. Compilar o Projeto

```bash
dotnet build
```

## 🚀 Como Usar

### Executar a Aplicação

```bash
dotnet run
```

### Passo a Passo

1. **Selecionar Imagem de Referência**
   - Clique em "Procurar..." na seção "👤 Imagem de Referência do Rosto"
   - Escolha uma imagem clara do rosto que você deseja encontrar

2. **Configurar Pastas de Origem**
   - Especifique até 3 pastas contendo as imagens a processar
   - Pelo menos uma pasta é obrigatória

3. **Configurar Pasta de Saída**
   - Escolha onde as imagens com o rosto alvo serão movidas

4. **Iniciar Processamento**
   - Clique em "▶ Iniciar Processamento"
   - Monitore o progresso na janela de log

5. **Revisar Resultados**
   - Verifique o resumo de imagens processadas
   - As imagens encontradas estarão na pasta de saída



Ou use o arquivo compilado em:
```
ImageTextDetector/bin/Debug/net8.0-windows/ImageTextDetector.exe
```

### Interface da Aplicação

1. **Imagem de Referência**: Selecione uma imagem clara do rosto alvo
2. **Pastas de Origem**: Até 3 locais onde as imagens a processar estão
3. **Pasta de Saída**: Onde as imagens com o rosto alvo serão movidas
4. **Iniciar Processamento**: Clique para começar a análise
5. **Monitorar Progresso**: Acompanhe em tempo real
6. **Visualizar Resultados**: Veja estatísticas de imagens processadas

## 📊 Estrutura do Projeto

```
Leitor - Rosto/
├── Models/
│   ├── FaceMatch.cs            # ✨ NOVO: Correspondência facial
│   ├── FaceAnalysisResult.cs   # ✨ NOVO: Resultado de análise
│   ├── TextRegion.cs           # Legado
│   └── ImageAnalysisResult.cs  # Legado
├── Services/
│   ├── FaceRecognitionService.cs   # ✨ NOVO: Reconhecimento facial
│   ├── ImageProcessingService.cs   # Refatorado
│   ├── TextDetectionService.cs     # Legado
│   └── EasyOcrService.cs           # Legado
├── MainWindow.xaml             # ✨ Refatorado para face recognition
├── MainWindow.xaml.cs          # ✨ Refatorado para face recognition
└── App.xaml                    # Configuração da aplicação
└── face_service.py             # ✨ NOVO: Serviço Python de face recognition
```

## 📦 Dependências

### .NET
- **OpenCvSharp4**: Processamento de imagens (novo uso em outros contextos)
- **System.Windows.Forms**: Diálogos de seleção de pasta

### Python
- **face_recognition**: Detecção e reconhecimento de rostos (baseado em dlib)
- **opencv-python**: Processamento de imagens
- **dlib**: Motor de detecção facial (instalado como dependência do face_recognition)

## 🔍 Detalhes Técnicos

### FaceRecognitionService (.NET)
Gerencia a integração com o serviço Python:
- Carrega imagem de referência
- Executa o processo Python com parâmetros
- Processa resultado JSON do face_service.py
- Suporta processamento em batch com múltiplos workers

### face_service.py (Python)
Serviço Python que realiza:
- Extração de embeddings do rosto de referência
- Detecção de rostos em imagens
- Comparação de embeddings usando distância euclidiana
- Retorno de coordenadas de bounding boxes
- Suporte a processamento paralelo com multiprocessing

### ImageProcessingService
Coordena o fluxo completo:
- Varre pastas recursivamente
- Verifica se há imagem de referência
- Detecta rostos e compara com referência
- Move apenas imagens com o rosto alvo
- Fornece feedback em tempo real

## ⚙️ Configuração Avançada

### Ajustar Tolerância de Reconhecimento
A tolerância (por padrão 0.6) controla o quão similar um rosto deve ser para ser considerado uma correspondência.

- Valores menores (ex: 0.4) = Mais rigoroso, menos falsos positivos
- Valores maiores (ex: 0.8) = Mais permissivo, mais falsos positivos

Para modificar, edite `face_service.py` na função `_load_reference_face()`:

```python
return {
    "success": True,
    "encoding": reference_encodings[0].tolist(),
    "tolerance": 0.6  # <- Ajuste aqui
}
```

### Usar Múltiplos Workers
Para processar imagens mais rapidamente em máquinas com múltiplos cores, modifique `ImageProcessingService.cs`:

```csharp
// Mude de 1 para 2 ou mais workers
"--workers 2"  // foi "--workers 1"
```

**Nota**: Mais workers usam mais memória RAM. Use 1-2 para máquinas com <= 8GB de RAM.


- `spa+fra` - Espanhol + Francês

### Ajustar Padding
No arquivo `ImageCroppingService.cs`, modifique:

```csharp
int padding = 10;  // Aumentar para mais contexto
```

## 🐛 Troubleshooting

### Erro: "Tesseract não foi inicializado"
- Verifique se o pacote Tesseract foi instalado via NuGet
- Certifique-se de que tem espaço em disco suficiente

### Imagens recortadas pequenas
- Aumente o valor de `padding` em `ImageCroppingService.cs`
- Melhore a qualidade/resolução das imagens de origem

### Detecção ruim de texto
- Imagens de baixa qualidade resultam em OCR impreciso
- Textos finos ou decorativos são mais difíceis de detectar
- Tente melhorar o contraste das imagens

## 📝 Exemplos de Uso

### Processar Documentos Escaneados
1. Coloque todos os PDFs ou imagens de documentos em `C:\Documentos\Scans`
2. Defina a pasta de saída como `C:\Documentos\TextRegions`
3. Clique em "Iniciar Processamento"
4. As regiões com texto serão salvas em arquivos separados

### Filtrar Memes com Texto
1. Coloque imagens em `C:\Pictures\Memes`
2. Execute o aplicativo
3. Apenas memes contendo texto terão regiões recortadas
4. As imagens sem texto não gerarão arquivos de saída

## 📄 Licença

Este projeto é fornecido como exemplo educacional e de produção. Use livremente com atribuição.

## 🤝 Contribuições

Contribuições são bem-vindas! Feel free to submit issues e pull requests.

## ✨ Próximas Melhorias

- [ ] Suporte para PDF com múltiplas páginas
- [ ] Interface dark mode
- [ ] Configuração de idioma dinâmica via UI
- [ ] Histórico de processamentos anteriores
- [ ] Integração com cloud storage
- [ ] Melhor detecção de coordenadas com ML

---

**Desenvolvido com ❤️ em .NET 8.0**
