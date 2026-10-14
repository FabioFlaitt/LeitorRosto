#!/usr/bin/env python3
"""
EasyOCR + Person Detection Service - Detecta texto e pessoas em imagens
Uso:
  Imagem única:  py ocr_service.py <caminho_imagem>
  Lote:          py ocr_service.py --batch <arquivo_paths> [--workers N]
"""

import sys
import json
import cv2
import os
import ssl
from pathlib import Path

os.environ['TF_CPP_MIN_LOG_LEVEL'] = '3'
ssl._create_default_https_context = ssl._create_unverified_context

try:
    import easyocr
except ImportError:
    print(json.dumps({"error": "EasyOCR não instalado. Execute: pip install easyocr"}))
    sys.exit(1)

try:
    from ultralytics import YOLO
except ImportError:
    print(json.dumps({"error": "YOLO não instalado. Execute: pip install ultralytics"}))
    sys.exit(1)

# Reader global para modo single-process
_reader = None
_yolo_detector = None


# ──────────────────────────────────────────────────────────────────────────────
# Worker helpers (devem ser funções de nível global para funcionar com multiprocessing)
# ──────────────────────────────────────────────────────────────────────────────

_worker_reader = None
_worker_yolo = None

def _worker_init():
    """Inicializa o reader em cada processo worker (chamado uma vez por worker)."""
    global _worker_reader, _worker_yolo
    try:
        _worker_reader = easyocr.Reader(
            ['pt', 'en', 'es'],
            gpu=False,
            quantize=True,   # INT8 → ~2-3x mais rápido na CPU
            verbose=False
        )
        _worker_yolo = YOLO('yolov8n.pt')  # nano model - leve e rápido
    except Exception:
        pass  # permanecem None; tratado em _worker_process


def _worker_process(args):
    """Processa uma imagem dentro de um worker."""
    image_path, index, total = args
    global _worker_reader, _worker_yolo
    if _worker_reader is None:
        return {"error": "Reader não inicializado no worker", "success": False,
                "image": image_path, "index": index, "total": total}
    result = _detect_with_reader_and_yolo(_worker_reader, _worker_yolo, image_path)
    result.update({"image": image_path, "index": index, "total": total})
    return result


# ──────────────────────────────────────────────────────────────────────────────
# Core
# ──────────────────────────────────────────────────────────────────────────────

def _make_reader():
    """Cria um reader com quantização para uso em processo único."""
    return easyocr.Reader(['pt', 'en', 'es'], gpu=False, quantize=True, verbose=False)


def _make_yolo_detector():
    """Cria um detector YOLO nano para detecção de pessoas."""
    return YOLO('yolov8n.pt')


def _detect_people(image, yolo_detector):
    """Detecta pessoas em uma imagem usando YOLO. Retorna True se houver pessoas."""
    if yolo_detector is None:
        return False
    try:
        results = yolo_detector(image, verbose=False, conf=0.3)
        for result in results:
            if result.boxes is not None:
                for box in result.boxes:
                    # Class 0 é 'person' no COCO dataset
                    if int(box.cls) == 0:
                        return True
        return False
    except Exception:
        return False


def _extract_regions(results, scale=1.0):
    regions = []
    for detection in results:
        bbox_points, text, confidence = detection
        xs = [p[0] for p in bbox_points]
        ys = [p[1] for p in bbox_points]
        x_min, x_max = int(min(xs) * scale), int(max(xs) * scale)
        y_min, y_max = int(min(ys) * scale), int(max(ys) * scale)
        if confidence > 0.2 and text.strip():
            regions.append({
                "text": text.strip(),
                "x": x_min, "y": y_min,
                "width": max(1, x_max - x_min),
                "height": max(1, y_max - y_min),
                "confidence": float(confidence)
            })
    return regions


def _detect_with_reader(reader, image_path):
    if not Path(image_path).exists():
        return {"error": f"Arquivo não encontrado: {image_path}"}
    try:
        image = cv2.imread(image_path)
        if image is None:
            return {"error": "Não foi possível ler a imagem"}

        # Estratégia 1: imagem original
        results = reader.readtext(image, detail=1)
        regions = _extract_regions(results) if results else []

        # Estratégia 2: upscale 2x para texto pequeno
        if not regions:
            h, w = image.shape[:2]
            img2x = cv2.resize(image, (w * 2, h * 2), interpolation=cv2.INTER_LANCZOS4)
            results = reader.readtext(img2x, detail=1)
            regions = _extract_regions(results, scale=0.5) if results else []

        # Estratégia 3: realce de contraste
        if not regions:
            gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
            enhanced = cv2.cvtColor(clahe.apply(gray), cv2.COLOR_GRAY2BGR)
            results = reader.readtext(enhanced, detail=1)
            regions = _extract_regions(results) if results else []

        # Deduplicar
        seen, unique = set(), []
        for r in regions:
            k = r["text"].lower().strip()
            if k not in seen:
                seen.add(k)
                unique.append(r)

        return {"success": True, "text_count": len(unique), "regions": unique, "has_people": False}

    except Exception as e:
        import traceback
        return {"error": str(e), "traceback": traceback.format_exc()}


def _detect_with_reader_and_yolo(reader, yolo_detector, image_path):
    """Detecta texto com OCR e verifica se há pessoas na imagem."""
    if not Path(image_path).exists():
        return {"error": f"Arquivo não encontrado: {image_path}"}
    try:
        image = cv2.imread(image_path)
        if image is None:
            return {"error": "Não foi possível ler a imagem"}

        # Detectar pessoas primeiro (mais rápido que OCR)
        has_people = _detect_people(image, yolo_detector)

        # Estratégia 1: imagem original
        results = reader.readtext(image, detail=1)
        regions = _extract_regions(results) if results else []

        # Estratégia 2: upscale 2x para texto pequeno
        if not regions:
            h, w = image.shape[:2]
            img2x = cv2.resize(image, (w * 2, h * 2), interpolation=cv2.INTER_LANCZOS4)
            results = reader.readtext(img2x, detail=1)
            regions = _extract_regions(results, scale=0.5) if results else []

        # Estratégia 3: realce de contraste
        if not regions:
            gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
            enhanced = cv2.cvtColor(clahe.apply(gray), cv2.COLOR_GRAY2BGR)
            results = reader.readtext(enhanced, detail=1)
            regions = _extract_regions(results) if results else []

        # Deduplicar
        seen, unique = set(), []
        for r in regions:
            k = r["text"].lower().strip()
            if k not in seen:
                seen.add(k)
                unique.append(r)

        return {
            "success": True,
            "text_count": len(unique),
            "regions": unique,
            "has_people": has_people
        }

    except Exception as e:
        import traceback
        return {
            "error": str(e),
            "traceback": traceback.format_exc(),
            "has_people": False
        }


# ──────────────────────────────────────────────────────────────────────────────
# Modos de entrada
# ──────────────────────────────────────────────────────────────────────────────

def batch_mode(paths_file, num_workers=1):
    try:
        with open(paths_file, 'r', encoding='utf-8') as f:
            paths = [line.strip() for line in f if line.strip()]
    except Exception as e:
        print(json.dumps({"error": f"Erro ao ler arquivo de caminhos: {e}"}), flush=True)
        return

    total = len(paths)
    if total == 0:
        return

    num_workers = max(1, min(num_workers, total))

    if num_workers == 1:
        # Processo único — sem overhead de spawn
        global _reader, _yolo_detector
        if _reader is None:
            print(json.dumps({"status": "Carregando modelo EasyOCR..."}), file=sys.stderr)
            _reader = _make_reader()
        if _yolo_detector is None:
            print(json.dumps({"status": "Carregando modelo YOLO..."}), file=sys.stderr)
            _yolo_detector = _make_yolo_detector()
        for i, path in enumerate(paths):
            result = _detect_with_reader_and_yolo(_reader, _yolo_detector, path)
            result.update({"image": path, "index": i, "total": total})
            print(json.dumps(result, ensure_ascii=False), flush=True)
    else:
        # Multi-processo: N readers em paralelo
        import multiprocessing as mp
        print(json.dumps({"status": f"Iniciando {num_workers} workers paralelos..."}), file=sys.stderr)
        args = [(path, i, total) for i, path in enumerate(paths)]
        ctx = mp.get_context('spawn')
        with ctx.Pool(processes=num_workers, initializer=_worker_init) as pool:
            # imap_unordered: retorna resultados assim que ficam prontos (sem aguardar ordem)
            for result in pool.imap_unordered(_worker_process, args):
                print(json.dumps(result, ensure_ascii=False), flush=True)


def detect_text(image_path):
    global _reader, _yolo_detector
    if _reader is None:
        _reader = _make_reader()
    if _yolo_detector is None:
        _yolo_detector = _make_yolo_detector()
    result = _detect_with_reader_and_yolo(_reader, _yolo_detector, image_path)
    return result


def main():
    if len(sys.argv) < 2:
        print(json.dumps({"error": "Uso: ocr_service.py <imagem>  OU  --batch <arquivo> [--workers N]"}))
        sys.exit(1)

    if sys.argv[1] == '--batch' and len(sys.argv) >= 3:
        paths_file = sys.argv[2]
        num_workers = 1
        if '--workers' in sys.argv:
            idx = sys.argv.index('--workers')
            if idx + 1 < len(sys.argv):
                try:
                    num_workers = int(sys.argv[idx + 1])
                except ValueError:
                    pass
        batch_mode(paths_file, num_workers)
    else:
        result = detect_text(sys.argv[1])
        print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    main()
