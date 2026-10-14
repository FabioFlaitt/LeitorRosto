# .NET WPF Image Text Detection & Cropping Tool

## Project Overview
A desktop application that detects images containing text using OCR, extracts the text regions, and saves cropped images to a configurable output folder.

## Setup Checklist

- [x] Create copilot-instructions.md
- [x] Scaffold .NET WPF project structure
- [x] Implement image detection logic (OCR integration)
- [x] Implement image cropping feature
- [x] Create UI with folder configuration
- [x] Compile and test project

## Key Features
- Configurable source folder for image scanning
- OCR-based text detection in images
- Automatic cropping of text regions
- Configurable output folder for cropped images
- Progress reporting and statistics
- Support for multiple image formats (JPG, PNG, BMP, GIF, TIFF, WebP)

## Technology Stack
- .NET 8.0
- WPF for UI
- Tesseract OCR 5.2.0 for text detection
- OpenCV 4.9.0 for image processing
- System.Windows.Forms 4.0.0 for folder dialogs

## Project Structure
- `Models/` - Data models (TextRegion)
- `Services/` - Business logic (TextDetectionService, ImageCroppingService, ImageProcessingService)
- `MainWindow.xaml/cs` - WPF UI and code-behind
- `App.xaml/cs` - Application configuration

## How to Run
1. Open the project in Visual Studio Code
2. Build: `dotnet build`
3. Run: `dotnet run`
4. Select source and output folders via the UI
5. Click "Iniciar Processamento" to start

## Compilation Status
✅ Project builds successfully with .NET 8.0
⚠️ Minor compatibility warnings for System.Windows.Forms 4.0.0 (expected for .NET 8.0)

