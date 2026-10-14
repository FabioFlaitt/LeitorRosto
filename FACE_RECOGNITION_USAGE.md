# Detector e Reconhecimento Facial - .NET WPF

## 📋 Descrição

Aplicação desktop que detecta e reconhece rostos específicos em imagens. O sistema:
- Carrega uma imagem de referência do rosto alvo
- Varre pastas configuráveis procurando por imagens
- Detecta todos os rostos em cada imagem
- Compara com o rosto de referência usando algoritmos de embedding facial
- Move apenas as imagens que contêm o rosto alvo para a pasta de saída

## 🚀 Como Usar

### 1. **Selecionar Referência do Rosto**
   - Na seção "👤 Imagem de Referência do Rosto" você tem duas opções:
     - **Foto...** — escolhe uma única imagem clara do rosto alvo.
     - **Pasta...** — escolhe uma pasta com **várias fotos** do mesmo rosto
       (ângulos, iluminação e expressões diferentes). Isso melhora muito a
       precisão: cada rosto encontrado é comparado com a **menor distância**
       entre todas as fotos de referência.
   - **Rigor (tolerância):** use o controle deslizante para ajustar o quão
     exigente é a correspondência. Menor = mais rigoroso (menos falsos
     positivos); maior = mais permissivo. Padrão: **0.55**.

### 2. **Configurar Pastas de Origem**
   - Use as três seções "Local 1", "Local 2", "Local 3" para especificar onde as imagens a processar estão localizadas
   - Você precisa especificar PELO MENOS uma pasta
   - As pastas podem conter subpastas (busca recursiva)

### 3. **Configurar Pasta de Saída**
   - Especifique onde as imagens com o rosto alvo serão movidas
   - Será criada automaticamente se não existir

### 4. **Iniciar Processamento**
   - Clique em "▶ Iniciar Processamento"
   - Monitore o progresso na janela de saída
   - O processamento roda em background thread para não congelar a UI

### 5. **Revisar Resultados**
   - Verifique o resumo das imagens processadas
   - As imagens com o rosto alvo estarão na pasta de saída especificada

## 🔬 Tela InsightFace (ArcFace) + Fotos Repetidas

Na tela principal, o botão **"🔬 InsightFace / Repetidas"** abre uma janela com
um motor mais moderno e preciso (InsightFace / **ArcFace**, detector SCRFD,
embedding de 512 dimensões). Ela tem duas abas:

### Aba "Reconhecimento (ArcFace)"
Mesmo fluxo da tela principal (referência → pastas de origem → destinos), mas
usando ArcFace. O controle **"Similaridade mínima"** usa similaridade de
cosseno (padrão **0.42**; maior = mais rigoroso). Costuma acertar muito mais em
rostos de perfil, pequenos, mal iluminados ou parcialmente cobertos.

### Aba "Fotos Repetidas"
1. Selecione uma **pasta** (opcionalmente incluindo subpastas).
2. Ajuste a **Similaridade**: `0.50` agrupa fotos da **mesma pessoa**; `0.80+`
   só agrupa fotos **quase idênticas** (a mesma foto duplicada).
3. Clique **🔍 Analisar** — o app calcula o embedding de cada foto e agrupa as
   repetidas por similaridade de cosseno.
4. O resultado lista cada grupo (quantidade + similaridade média + arquivos).
5. **📦 Mover repetidas (manter 1 de cada)** move as fotos extras de cada grupo
   para uma pasta à sua escolha, preservando uma de cada na origem.

> O modelo `buffalo_l` (~280 MB) é baixado automaticamente na primeira
> utilização e fica em cache em `C:\Users\<voce>\.insightface\models`.

## 📊 Resumo de Processamento

- **Total de Imagens**: Quantidade total de imagens encontradas
- **Com Rosto Alvo**: Imagens que contêm o rosto de referência com confiança suficiente
- **Imagens Movidas**: Quantidade de imagens movidas com sucesso para a saída

## 🔧 Dependências Python

O aplicativo requer os seguintes pacotes Python:

```bash
# Motor dlib (tela principal)
pip install face_recognition opencv-python ultralytics

# Motor ArcFace / fotos repetidas (tela InsightFace)
pip install insightface onnxruntime "numpy<2"
```

> ⚠️ Mantenha o **numpy < 2** — o dlib e o YOLO deste projeto foram compilados
> para numpy 1.x. O InsightFace funciona normalmente com numpy 1.x.

Scripts usados automaticamente:
- `face_service.py` — motor dlib (HOG/CNN + YOLO).
- `insightface_service.py` — motor ArcFace + detecção de fotos repetidas.

## 📁 Estrutura de Arquivos

```
Leitor - Rosto/
├── Leitor.sln
├── face_service.py          # Serviço Python de reconhecimento facial
├── README.md
├── settings.json
└── ImageTextDetector/       # Projeto WPF .NET
    ├── App.xaml
    ├── App.xaml.cs
    ├── MainWindow.xaml      # Interface principal (refatorada)
    ├── MainWindow.xaml.cs   # Code-behind (refatorado)
    ├── ImageTextDetector.csproj
    ├── bin/
    ├── obj/
    ├── Models/
    │   ├── FaceMatch.cs             # Novo modelo para correspondência facial
    │   ├── FaceAnalysisResult.cs    # Novo modelo para resultado de análise
    │   ├── TextRegion.cs            # (Legado)
    │   └── ImageAnalysisResult.cs   # (Legado)
    └── Services/
        ├── FaceRecognitionService.cs    # Novo serviço de face recognition
        ├── ImageProcessingService.cs    # Refatorado para usar FaceRecognitionService
        ├── TextDetectionService.cs      # (Legado)
        └── EasyOcrService.cs            # (Legado)
```

## 🎯 Algoritmo de Reconhecimento Facial

1. **Carregamento da Referência**: Extrai um ou mais embeddings (vetores de
   características) do rosto alvo. Quando a referência é uma pasta, cada foto
   gera um embedding e todos são usados na comparação. O embedding da
   referência usa reamostragem (`num_jitters=10`) para maior robustez.
2. **Detecção (em estágios)**: Para cada imagem:
   - Reduz imagens muito grandes (≥1600 px) para acelerar e padronizar.
   - Procura rostos com HOG (rápido); se não achar, tenta HOG com *upsample*
     (rostos pequenos/distantes); por fim, *fallback* para CNN (mais preciso).
   - Extrai o embedding de cada rosto encontrado.
3. **Comparação**: Calcula a distância para **todas** as referências e usa a
   **menor**. Se for ≤ tolerância, é uma correspondência. Cada correspondência
   traz também um `confidence` (0..1).
4. **Fallback de pessoa (YOLO)**: Se nenhum rosto for detectado, o YOLO
   verifica se há uma pessoa na imagem.
5. **Resultado**: Move a imagem conforme a categoria (rosto alvo / outro rosto
   / pessoa sem rosto / sem rosto).

## ⚙️ Parâmetros

- **Tolerância / Rigor**: Limite de distância para considerar correspondência
  (padrão: **0.55**; ajustável no controle deslizante da interface).
  - Valores menores = mais rigoroso (menos falsos positivos)
  - Valores maiores = mais permissivo
- **Modelo de detecção**: `hog` (rápido), `cnn` (preciso) ou `auto` (HOG com
  fallback para CNN). Padrão: `auto`.

## 🔍 Dicas

- Use uma imagem de referência clara, bem iluminada e com o rosto visível
- Para melhores resultados, a imagem de referência deve ter o rosto em boa qualidade
- O sistema é invariante a rotação, escala e iluminação até certo ponto
- Versões de resolução baixa são processadas mais rapidamente

## 📝 Limitações

- Requer pelo menos um rosto visível na imagem de referência
- Rostos parcialmente cobertos podem não ser detectados ou comparados com precisão
- Imagens muito pixeladas ou desfocadas podem ter baixa taxa de reconhecimento

## 🗂️ Formatos de Imagem Suportados

- JPG/JPEG
- PNG
- BMP
- GIF
- TIFF
- WebP

## 📦 Compilação e Execução

### Build
```bash
cd ImageTextDetector
dotnet build
```

### Run
```bash
dotnet run
```

## ⚠️ Avisos Conhecidos

- Aviso NU1701 sobre System.Windows.Forms é esperado e inofensivo em .NET 8.0
- Na primeira execução, o modelo facial pode levar alguns segundos para carregar

## 🤝 Suporte

Para problemas com Python ou dependências:
```bash
pip list  # Verificar pacotes instalados
python --version  # Verificar versão Python
```

Certifique-se de que `face_recognition` e `opencv-python` estão instalados e atualizados.
