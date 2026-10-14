#!/usr/bin/env python3
"""
Face Recognition Service - Detecta e reconhece um rosto alvo em imagens.

Melhorias de identificação:
  * A referência pode ser UM arquivo OU UMA PASTA com várias fotos do alvo.
    Quando há várias fotos, a comparação usa a MENOR distância entre todas
    elas (muito mais robusto a ângulo, iluminação e expressão).
  * Encoding da referência com num_jitters (reamostragem) para maior robustez.
  * Detecção em dois estágios: HOG rápido e, se nada for encontrado numa
    imagem que contém pessoa, fallback para CNN (mais preciso).
  * Upsample para encontrar rostos pequenos/distantes e downscale automático
    de imagens muito grandes (velocidade + consistência).
  * Cada correspondência traz distância e "confidence" (0..1).

Uso:
  Imagem única:  py face_service.py <imagem> --reference <ref> [--tolerance 0.55]
  Lote:          py face_service.py --batch <arquivo_paths> --reference <ref>
                    [--tolerance 0.55] [--workers N] [--model hog|cnn|auto]
  Só pessoa:     py face_service.py --detect-person <imagem>

<ref> pode ser um arquivo de imagem ou uma pasta contendo várias imagens do alvo.
"""

import sys
import json
import os
import ssl
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps

os.environ['TF_CPP_MIN_LOG_LEVEL'] = '3'
ssl._create_default_https_context = ssl._create_unverified_context

try:
    import face_recognition
except ImportError:
    print(json.dumps({"error": "face_recognition não instalado. Execute: pip install face_recognition"}))
    sys.exit(1)

try:
    from ultralytics import YOLO as _YOLO_CLASS
except ImportError:
    _YOLO_CLASS = None


# ──────────────────────────────────────────────────────────────────────────────
# Configuração
# ──────────────────────────────────────────────────────────────────────────────

DEFAULT_TOLERANCE = 0.55          # < 0.6 reduz falsos positivos
REFERENCE_JITTERS = 10            # reamostragens ao codificar a referência
DETECT_JITTERS = 1                # reamostragens ao codificar rostos das fotos
MAX_DETECT_DIMENSION = 1600       # imagens maiores são reduzidas antes de detectar
SUPPORTED_EXT = {'.jpg', '.jpeg', '.png', '.bmp', '.gif', '.tiff', '.tif', '.webp'}


# ──────────────────────────────────────────────────────────────────────────────
# Carregamento de imagem
# ──────────────────────────────────────────────────────────────────────────────

def _load_image_corrected(image_path, max_dimension=None):
    """Carrega imagem corrigindo orientação EXIF.

    Retorna (array_rgb, scale) onde scale é o fator aplicado ao reduzir a
    imagem (1.0 quando não houve redução). As coordenadas detectadas devem
    ser divididas por scale para voltar à resolução original.
    """
    pil_img = Image.open(image_path)
    pil_img = ImageOps.exif_transpose(pil_img)
    pil_img = pil_img.convert('RGB')

    scale = 1.0
    if max_dimension:
        w, h = pil_img.size
        largest = max(w, h)
        if largest > max_dimension:
            scale = max_dimension / float(largest)
            new_size = (max(1, int(round(w * scale))), max(1, int(round(h * scale))))
            pil_img = pil_img.resize(new_size, Image.LANCZOS)

    return np.array(pil_img), scale


def _iter_reference_images(reference_path):
    """Gera os caminhos de imagem da referência (arquivo único ou pasta)."""
    p = Path(reference_path)
    if p.is_dir():
        for child in sorted(p.iterdir()):
            if child.is_file() and child.suffix.lower() in SUPPORTED_EXT:
                yield str(child)
    elif p.is_file():
        yield str(p)


# ──────────────────────────────────────────────────────────────────────────────
# YOLO - carregamento único por processo
# ──────────────────────────────────────────────────────────────────────────────

_yolo_model = None
_yolo_init_done = False


def _get_yolo_model():
    global _yolo_model, _yolo_init_done
    if not _yolo_init_done:
        _yolo_init_done = True
        if _YOLO_CLASS is not None:
            try:
                _yolo_model = _YOLO_CLASS('yolov8n.pt')
                print("[YOLO] Modelo carregado.", file=sys.stderr, flush=True)
            except Exception as e:
                print(f"[YOLO] Falha ao carregar: {e}", file=sys.stderr, flush=True)
    return _yolo_model


def _check_person_yolo(image_path):
    """Verifica se existe uma pessoa na imagem usando YOLO."""
    model = _get_yolo_model()
    if model is None:
        return False
    try:
        results = model(image_path, verbose=False)
        for result in results:
            for box in result.boxes:
                if int(box.cls[0]) == 0:  # classe 0 = person no COCO
                    return True
    except Exception:
        pass
    return False


# ──────────────────────────────────────────────────────────────────────────────
# Detecção de rostos (com upsample e fallback CNN)
# ──────────────────────────────────────────────────────────────────────────────

def _detect_face_locations(image, model="hog", upsample=1):
    try:
        return face_recognition.face_locations(
            image, number_of_times_to_upsample=upsample, model=model)
    except Exception as e:
        print(f"[DETECT] Erro ({model}, up={upsample}): {e}", file=sys.stderr, flush=True)
        return []


def _locate_faces(image, prefer_model="auto"):
    """Localiza rostos tentando estratégias progressivamente mais fortes.

    Retorna (locations, model_usado).
    """
    # 1) HOG rápido
    locs = _detect_face_locations(image, model="hog", upsample=1)
    if locs:
        return locs, "hog"

    # 2) HOG com upsample (rostos pequenos/distantes)
    locs = _detect_face_locations(image, model="hog", upsample=2)
    if locs:
        return locs, "hog+up"

    # 3) CNN (mais preciso, mais lento) — apenas se solicitado/auto
    if prefer_model in ("cnn", "auto"):
        locs = _detect_face_locations(image, model="cnn", upsample=1)
        if locs:
            return locs, "cnn"

    return [], prefer_model


# ──────────────────────────────────────────────────────────────────────────────
# Workers (multiprocessing)
# ──────────────────────────────────────────────────────────────────────────────

_worker_reference_encodings = None
_worker_tolerance = DEFAULT_TOLERANCE
_worker_model = "auto"


def _worker_init(reference_encodings, tolerance, model):
    global _worker_reference_encodings, _worker_tolerance, _worker_model
    _worker_reference_encodings = reference_encodings
    _worker_tolerance = tolerance
    _worker_model = model


def _worker_process(args):
    image_path, index, total = args
    if _worker_reference_encodings is None:
        return {
            "error": "Worker não inicializado", "success": False,
            "image": image_path, "index": index, "total": total, "has_person": False,
        }
    result = _detect_faces_and_match(
        image_path, _worker_reference_encodings, _worker_tolerance, _worker_model)
    result.update({"image": image_path, "index": index, "total": total})
    return result


# ──────────────────────────────────────────────────────────────────────────────
# Core
# ──────────────────────────────────────────────────────────────────────────────

def _load_reference_faces(reference_path):
    """Carrega a(s) imagem(ns) de referência e extrai encodings do alvo.

    Retorna {"success": True, "encodings": [[...], ...], "sources": [...]}
    ou {"error": "..."}.
    """
    sources = list(_iter_reference_images(reference_path))
    if not sources:
        return {"error": f"Referência não encontrada ou sem imagens: {reference_path}"}

    encodings = []
    used = []
    errors = []
    for src in sources:
        try:
            img, _ = _load_image_corrected(src, max_dimension=MAX_DETECT_DIMENSION)
            locs, _model = _locate_faces(img, prefer_model="auto")
            if not locs:
                errors.append(f"sem rosto: {Path(src).name}")
                continue
            # Para cada foto de referência usamos o rosto maior (mais confiável)
            locs = sorted(locs, key=lambda l: (l[2] - l[0]) * (l[1] - l[3]), reverse=True)
            encs = face_recognition.face_encodings(
                img, [locs[0]], num_jitters=REFERENCE_JITTERS)
            if encs:
                encodings.append(encs[0].tolist())
                used.append(src)
        except Exception as e:
            errors.append(f"{Path(src).name}: {str(e)[:80]}")

    if not encodings:
        return {"error": "Nenhum rosto válido nas imagens de referência. " + "; ".join(errors[:5])}

    print(f"[REF] {len(encodings)} encoding(s) de referência de {len(sources)} imagem(ns).",
          file=sys.stderr, flush=True)
    if errors:
        print(f"[REF] Avisos: {'; '.join(errors[:5])}", file=sys.stderr, flush=True)
    return {"success": True, "encodings": encodings, "sources": used}


def _detect_faces_and_match(image_path, reference_encodings, tolerance=DEFAULT_TOLERANCE, model="auto"):
    """Detecta rostos numa imagem e compara com os encodings de referência."""
    empty = {
        "success": False, "faces_detected": 0, "matches": [],
        "has_target_face": False, "has_person": False,
    }

    if not Path(image_path).exists():
        return {**empty, "error": f"Arquivo não encontrado: {image_path}"}

    try:
        ref = np.array(reference_encodings, dtype=np.float64)
        if ref.ndim == 1:
            ref = ref.reshape(1, -1)

        try:
            image, scale = _load_image_corrected(image_path, max_dimension=MAX_DETECT_DIMENSION)
        except Exception as e:
            return {**empty, "error": f"Erro ao carregar imagem: {str(e)[:100]}"}

        face_locations, used_model = _locate_faces(image, prefer_model=model)

        if not face_locations:
            # Sem rosto: checar se há pessoa via YOLO
            has_person = _check_person_yolo(image_path)
            return {**empty, "success": True, "has_person": has_person}

        try:
            face_encodings = face_recognition.face_encodings(
                image, face_locations, num_jitters=DETECT_JITTERS)
        except Exception as e:
            return {**empty, "error": f"Erro ao extrair encodings: {str(e)[:100]}"}

        inv_scale = 1.0 / scale if scale else 1.0
        matches = []
        for i, (loc, enc) in enumerate(zip(face_locations, face_encodings)):
            # Menor distância entre TODAS as referências
            distances = face_recognition.face_distance(ref, enc)
            distance = float(np.min(distances))
            is_match = bool(distance <= tolerance)
            # Confiança 0..1 (0.0 em distância == tolerância, 1.0 em distância 0)
            confidence = max(0.0, min(1.0, 1.0 - distance / max(tolerance, 1e-6))) if is_match \
                else max(0.0, 1.0 - distance)
            top, right, bottom, left = loc
            matches.append({
                "index": i,
                "match": is_match,
                "distance": round(distance, 4),
                "confidence": round(confidence, 4),
                "tolerance": tolerance,
                "x": int(left * inv_scale),
                "y": int(top * inv_scale),
                "width": int((right - left) * inv_scale),
                "height": int((bottom - top) * inv_scale),
            })

        has_target_face = any(m["match"] for m in matches)
        return {
            "success": True,
            "faces_detected": len(face_locations),
            "matches": matches,
            "has_target_face": has_target_face,
            "has_person": True,
            "model": used_model,
        }

    except Exception as e:
        import traceback
        return {**empty, "error": str(e), "traceback": traceback.format_exc()}


# ──────────────────────────────────────────────────────────────────────────────
# Batch
# ──────────────────────────────────────────────────────────────────────────────

def batch_mode(paths_file, reference_encodings, tolerance=DEFAULT_TOLERANCE, num_workers=1, model="auto"):
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
    print(f"[BATCH] {total} imagens, {num_workers} worker(s), model={model}", file=sys.stderr, flush=True)

    if num_workers == 1:
        for i, path in enumerate(paths):
            try:
                result = _detect_faces_and_match(path, reference_encodings, tolerance, model)
                result.update({"image": path, "index": i, "total": total})
                print(json.dumps(result, ensure_ascii=False), flush=True)
                if (i + 1) % 50 == 0:
                    print(f"[BATCH] {i + 1}/{total} processadas", file=sys.stderr, flush=True)
            except Exception as e:
                print(json.dumps({
                    "success": False, "error": str(e)[:200], "image": path,
                    "index": i, "total": total, "has_person": False,
                }), flush=True)
    else:
        import multiprocessing as mp
        args = [(path, i, total) for i, path in enumerate(paths)]
        ctx = mp.get_context('spawn')
        with ctx.Pool(
            processes=num_workers,
            initializer=_worker_init,
            initargs=(reference_encodings, tolerance, model),
        ) as pool:
            for result in pool.imap_unordered(_worker_process, args):
                print(json.dumps(result, ensure_ascii=False), flush=True)

    print(f"[BATCH] Concluído: {total} imagens", file=sys.stderr, flush=True)


def detect_face(image_path, reference_encodings, tolerance=DEFAULT_TOLERANCE, model="auto"):
    return _detect_faces_and_match(image_path, reference_encodings, tolerance, model)


def detect_person_yolo(image_path):
    if not Path(image_path).exists():
        return {"error": f"Arquivo não encontrado: {image_path}", "success": False}
    return {"success": True, "has_person": _check_person_yolo(image_path), "image": image_path}


# ──────────────────────────────────────────────────────────────────────────────
# CLI
# ──────────────────────────────────────────────────────────────────────────────

def _arg_value(name, cast, default):
    if name in sys.argv:
        idx = sys.argv.index(name)
        if idx + 1 < len(sys.argv):
            try:
                return cast(sys.argv[idx + 1])
            except (ValueError, TypeError):
                pass
    return default


def main():
    print(f"[MAIN] Argumentos: {sys.argv[1:]}", file=sys.stderr, flush=True)

    try:
        if len(sys.argv) < 2:
            print(json.dumps({"error": "Uso: face_service.py <imagem> --reference <ref>  OU  --batch <arquivo> --reference <ref> [--workers N] [--tolerance T] [--model hog|cnn|auto]"}))
            sys.exit(1)

        if sys.argv[1] == '--detect-person' and len(sys.argv) >= 3:
            print(json.dumps(detect_person_yolo(sys.argv[2]), ensure_ascii=False))
            sys.exit(0)

        if '--reference' not in sys.argv:
            print(json.dumps({"error": "Parâmetro --reference é obrigatório"}))
            sys.exit(1)

        ref_idx = sys.argv.index('--reference')
        if ref_idx + 1 >= len(sys.argv):
            print(json.dumps({"error": "Falta caminho para --reference"}))
            sys.exit(1)

        reference_path = sys.argv[ref_idx + 1]
        tolerance = _arg_value('--tolerance', float, DEFAULT_TOLERANCE)
        model = _arg_value('--model', str, "auto")
        if model not in ("hog", "cnn", "auto"):
            model = "auto"

        print(f"[MAIN] Carregando referência: {reference_path}", file=sys.stderr, flush=True)
        ref_result = _load_reference_faces(reference_path)
        if "error" in ref_result:
            print(json.dumps(ref_result))
            sys.exit(1)

        reference_encodings = ref_result["encodings"]
        print(f"[MAIN] Referência pronta. Tolerância={tolerance}, model={model}", file=sys.stderr, flush=True)

        if sys.argv[1] == '--batch' and len(sys.argv) >= 3:
            paths_file = sys.argv[2]
            num_workers = _arg_value('--workers', int, 1)
            batch_mode(paths_file, reference_encodings, tolerance, num_workers, model)
        else:
            image_path = sys.argv[1]
            result = detect_face(image_path, reference_encodings, tolerance, model)
            print(json.dumps(result, ensure_ascii=False))

    except Exception as e:
        import traceback
        print(f"[MAIN] ERRO FATAL: {e}", file=sys.stderr, flush=True)
        print(traceback.format_exc(), file=sys.stderr, flush=True)
        print(json.dumps({"error": str(e)}))
        sys.exit(1)


if __name__ == '__main__':
    main()
