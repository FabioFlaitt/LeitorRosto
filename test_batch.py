#!/usr/bin/env python3
"""
Script de teste direto para batch processing.
Executa batch_mode diretamente sem passar pela GUI.
"""

import os
import sys
import json
import tempfile
from pathlib import Path

# Add the repo root to path so we can import face_service
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from face_service import batch_mode, _load_reference_face

def test_batch_processing():
    from datetime import datetime
    
    print(f"\n{'='*60}")
    print(f"  BATCH PROCESSING TEST - {datetime.now().strftime('%H:%M:%S')}")
    print(f"{'='*60}\n")
    
    # Configurações
    reference_path = os.path.join(os.path.dirname(__file__), "reference_face.jpg")
    
    print(f"Reference face path: {reference_path}")
    print(f"Reference face exists: {os.path.exists(reference_path)}")
    
    if not os.path.exists(reference_path):
        print("❌ Reference face not found!")
        return False
    
    # Carregar reference face
    print("\nLoading reference face encoding...")
    ref_result = _load_reference_face(reference_path)
    
    if "error" in ref_result:
        print(f"❌ Error loading reference: {ref_result['error']}")
        return False
    
    reference_encoding = ref_result["encoding"]
    tolerance = ref_result.get("tolerance", 0.6)
    print(f"✓ Reference face loaded (tolerance={tolerance})")
    
    # Procurar pastas com imagens
    source_folders = [
        Path(__file__).parent / "ImagesTest",
        Path(__file__).parent / "Imagens",
        Path.home() / "Desktop" / "ImagesTest",
    ]
    
    selected_folder = None
    for folder in source_folders:
        if folder.exists():
            images = list(folder.glob("*.jpg")) + list(folder.glob("*.png"))
            if images:
                selected_folder = folder
                print(f"\n✓ Found images in: {folder}")
                print(f"  Total images: {len(images)}")
                break
    
    if not selected_folder:
        print(f"\n⚠️  No image folders found. Checked:")
        for folder in source_folders:
            print(f"  - {folder}")
        return False
    
    # Criar arquivo de lista de imagens
    images = sorted(
        list(selected_folder.glob("*.jpg")) + 
        list(selected_folder.glob("*.png")) +
        list(selected_folder.glob("*.bmp"))
    )
    
    print(f"\nTotal images to process: {len(images)}")
    print(f"First 3 images:")
    for img in images[:3]:
        print(f"  - {img.name}")
    
    # Criar arquivo temporário com lista de caminhos
    with tempfile.NamedTemporaryFile(mode='w', suffix='.txt', delete=False, encoding='utf-8') as f:
        for img_path in images:
            f.write(str(img_path) + '\n')
        temp_file = f.name
    
    try:
        print(f"\nStarting batch processing...")
        print(f"Temp file: {temp_file}")
        print(f"Number of images: {len(images)}\n")
        print(f"{'-'*60}")
        
        # Executar batch_mode
        batch_mode(temp_file, reference_encoding, tolerance=tolerance, num_workers=1)
        
        print(f"{'-'*60}\n")
        print(f"✓ Batch processing completed!")
        return True
        
    finally:
        os.unlink(temp_file)


if __name__ == "__main__":
    success = test_batch_processing()
    sys.exit(0 if success else 1)
