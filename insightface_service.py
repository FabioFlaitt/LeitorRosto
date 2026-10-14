#!/usr/bin/env python3
"""
InsightFace Service - Reconhecimento facial com ArcFace (buffalo_l) e
detecção de fotos repetidas / do mesmo rosto.

Vantagens sobre o dlib (face_service.py):
  * Detector SCRFD (perfil, rostos pequenos, oclusão) muito superior ao HOG.
  * Embedding ArcFace de 512 dimensões (bem mais preciso que os 128-d do dlib).
  * Comparação por similaridade de cosseno.

Modos de uso:
  Imagem única:   py insightface_service.py <imagem> --reference <ref> [--sim-threshold 0.42]
  Lote:           py insightface_service.py --batch <paths_file> --reference <ref> [--sim-threshold 0.42]
  Fotos repetidas:py insightface_service.py --duplicates <paths_file> [--sim-threshold 0.5]

<ref> pode ser um arquivo de imagem OU uma pasta com várias fotos do alvo.
A saída é sempre JSON (uma linha por imagem; o modo --duplicates termina com
uma linha {"groups": [...]}).
"""

import sys
import json
import os
import ssl
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps

os.environ['TF_CPP_MIN_LOG_LEVEL'] = '3'
os.environ.setdefault('ORT_LOGGING_LEVEL', '3')
ssl._create_default_https_context = ssl._create_unverified_context

try:
    from insightface.app import FaceAnalysis
except ImportError:
    print(json.dumps({"error": "insightface não instalado. Execute: pip install insightface onnxruntime"}))
    sys.exit(1)


# ──────────────────────────────────────────────────────────────────────────────
# Configuração
# ──────────────────────────────────────────────────────────────────────────────

DEFAULT_SIM_THRESHOLD = 0.42       # similaridade de cosseno p/ reconhecimento
DEFAULT_DUP_THRESHOLD = 0.50       # similaridade de cosseno p/ "fotos repetidas"
MODEL_NAME = 'buffalo_l'
DET_SIZE = (640, 640)
MAX_DIMENSION = 2000               # imagens maiores são reduzidas antes de detectar
SUPPORTED_EXT = {'.jpg', '.jpeg', '.png', '.bmp', '.gif', '.tiff', '.tif', '.webp'}


# ──────────────────────────────────────────────────────────────────────────────
# Modelo (carregado uma vez por processo)
# ──────────────────────────────────────────────────────────────────────────────

_app = None


def _get_app():
    global _app
    if _app is None:
        _app = FaceAnalysis(name=MODEL_NAME, providers=['CPUExecutionProvider'])
        _app.prepare(ctx_id=-1, det_size=DET_SIZE)
        print(f"[ARCFACE] Modelo {MODEL_NAME} carregado.", file=sys.stderr, flush=True)
    return _app


# ──────────────────────────────────────────────────────────────────────────────
# Carregamento de imagem (RGB corrigido por EXIF → BGR para o InsightFace)
# ──────────────────────────────────────────────────────────────────────────────

def _load_bgr(image_path, max_dimension=MAX_DIMENSION):
    pil_img = Image.open(image_path)
    pil_img = ImageOps.exif_transpose(pil_img)
    pil_img = pil_img.convert('RGB')

    scale = 1.0
    w, h = pil_img.size
    largest = max(w, h)
    if max_dimension and largest > max_dimension:
        scale = max_dimension / float(largest)
        pil_img = pil_img.resize(
            (max(1, int(round(w * scale))), max(1, int(round(h * scale)))),
            Image.LANCZOS)

    rgb = np.array(pil_img)
    bgr = rgb[:, :, ::-1].copy()  # RGB -> BGR
    return bgr, scale


def _iter_images(path):
    p = Path(path)
    if p.is_dir():
        for child in sorted(p.iterdir()):
            if child.is_file() and child.suffix.lower() in SUPPORTED_EXT:
                yield str(child)
    elif p.is_file():
        yield str(p)


def _read_paths_file(paths_file):
    with open(paths_file, 'r', encoding='utf-8') as f:
        return [line.strip() for line in f if line.strip()]


def _normed(face):
    """Retorna o embedding normalizado (vetor unitário) de um rosto."""
    emb = getattr(face, 'normed_embedding', None)
    if emb is None:
        emb = face.embedding
        norm = np.linalg.norm(emb)
        emb = emb / norm if norm > 0 else emb
    return np.asarray(emb, dtype=np.float32)


def _face_area(face):
    x1, y1, x2, y2 = face.bbox
    return max(0.0, (x2 - x1)) * max(0.0, (y2 - y1))


# ──────────────────────────────────────────────────────────────────────────────
# Referência
# ──────────────────────────────────────────────────────────────────────────────

def _load_reference(reference_path):
    """Extrai embeddings do rosto alvo (arquivo único ou pasta)."""
    sources = list(_iter_images(reference_path))
    if not sources:
        return {"error": f"Referência não encontrada ou sem imagens: {reference_path}"}

    app = _get_app()
    embeddings = []
    errors = []
    for src in sources:
        try:
            bgr, _ = _load_bgr(src)
            faces = app.get(bgr)
            if not faces:
                errors.append(f"sem rosto: {Path(src).name}")
                continue
            faces.sort(key=_face_area, reverse=True)
            embeddings.append(_normed(faces[0]))
        except Exception as e:
            errors.append(f"{Path(src).name}: {str(e)[:80]}")

    if not embeddings:
        return {"error": "Nenhum rosto válido na referência. " + "; ".join(errors[:5])}

    print(f"[REF] {len(embeddings)} encoding(s) de {len(sources)} imagem(ns).", file=sys.stderr, flush=True)
    if errors:
        print(f"[REF] Avisos: {'; '.join(errors[:5])}", file=sys.stderr, flush=True)
    return {"success": True, "embeddings": np.vstack(embeddings)}


# ──────────────────────────────────────────────────────────────────────────────
# Reconhecimento
# ──────────────────────────────────────────────────────────────────────────────

def _detect_and_match(image_path, ref_matrix, sim_threshold=DEFAULT_SIM_THRESHOLD):
    empty = {
        "success": False, "faces_detected": 0, "matches": [],
        "has_target_face": False, "has_person": False,
    }
    if not Path(image_path).exists():
        return {**empty, "error": f"Arquivo não encontrado: {image_path}"}

    try:
        app = _get_app()
        bgr, scale = _load_bgr(image_path)
        faces = app.get(bgr)
        if not faces:
            return {**empty, "success": True}

        inv = 1.0 / scale if scale else 1.0
        matches = []
        for i, face in enumerate(faces):
            emb = _normed(face)
            sims = ref_matrix @ emb          # cosseno (vetores unitários)
            cos = float(np.max(sims))
            is_match = bool(cos >= sim_threshold)
            # distance = 1 - cos para manter o MESMO schema do motor dlib
            distance = 1.0 - cos
            x1, y1, x2, y2 = face.bbox
            matches.append({
                "index": i,
                "match": is_match,
                "distance": round(distance, 4),
                "confidence": round(max(0.0, min(1.0, cos)), 4),
                "tolerance": sim_threshold,
                "x": int(x1 * inv),
                "y": int(y1 * inv),
                "width": int((x2 - x1) * inv),
                "height": int((y2 - y1) * inv),
            })

        return {
            "success": True,
            "faces_detected": len(faces),
            "matches": matches,
            "has_target_face": any(m["match"] for m in matches),
            "has_person": True,
            "model": "arcface",
        }
    except Exception as e:
        import traceback
        return {**empty, "error": str(e), "traceback": traceback.format_exc()}


def batch_recognition(paths_file, ref_matrix, sim_threshold):
    paths = _read_paths_file(paths_file)
    total = len(paths)
    if total == 0:
        return
    print(f"[BATCH] {total} imagens (ArcFace, sim>={sim_threshold})", file=sys.stderr, flush=True)
    for i, path in enumerate(paths):
        try:
            result = _detect_and_match(path, ref_matrix, sim_threshold)
        except Exception as e:
            result = {
                "success": False, "error": str(e)[:200],
                "faces_detected": 0, "matches": [],
                "has_target_face": False, "has_person": False,
            }
        result.update({"image": path, "index": i, "total": total})
        print(json.dumps(result, ensure_ascii=False), flush=True)
        if (i + 1) % 50 == 0:
            print(f"[BATCH] {i + 1}/{total}", file=sys.stderr, flush=True)
    print(f"[BATCH] Concluído: {total}", file=sys.stderr, flush=True)


# ──────────────────────────────────────────────────────────────────────────────
# Fotos repetidas (agrupamento por similaridade de cosseno)
# ──────────────────────────────────────────────────────────────────────────────

class _UnionFind:
    def __init__(self, n):
        self.parent = list(range(n))

    def find(self, a):
        while self.parent[a] != a:
            self.parent[a] = self.parent[self.parent[a]]
            a = self.parent[a]
        return a

    def union(self, a, b):
        ra, rb = self.find(a), self.find(b)
        if ra != rb:
            self.parent[rb] = ra


def find_duplicates(paths_file, sim_threshold=DEFAULT_DUP_THRESHOLD):
    paths = _read_paths_file(paths_file)
    total = len(paths)
    if total == 0:
        print(json.dumps({"success": True, "groups": [], "total": 0, "with_face": 0}), flush=True)
        return

    app = _get_app()
    print(f"[DUP] Analisando {total} imagens (sim>={sim_threshold})...", file=sys.stderr, flush=True)

    valid_paths = []
    embeddings = []
    for i, path in enumerate(paths):
        try:
            if not Path(path).exists():
                raise FileNotFoundError(path)
            bgr, _ = _load_bgr(path)
            faces = app.get(bgr)
            if faces:
                faces.sort(key=_face_area, reverse=True)
                valid_paths.append(path)
                embeddings.append(_normed(faces[0]))
            # imagens sem rosto são ignoradas no agrupamento por rosto
        except Exception as e:
            print(f"[DUP] Falha em {Path(path).name}: {str(e)[:80]}", file=sys.stderr, flush=True)

        done = i + 1
        # progresso para a interface
        print(json.dumps({"progress": done, "total": total,
                          "image": os.path.basename(path)}, ensure_ascii=False), flush=True)
        if done % 50 == 0:
            print(f"[DUP] {done}/{total}", file=sys.stderr, flush=True)

    n = len(valid_paths)
    groups = []
    if n >= 2:
        matrix = np.vstack(embeddings).astype(np.float32)   # (n, 512) unitários
        sims = matrix @ matrix.T                            # (n, n) cossenos
        uf = _UnionFind(n)
        iu = np.triu_indices(n, k=1)
        pair_sims = sims[iu]
        for idx in np.where(pair_sims >= sim_threshold)[0]:
            uf.union(int(iu[0][idx]), int(iu[1][idx]))

        clusters = {}
        for i in range(n):
            root = uf.find(i)
            clusters.setdefault(root, []).append(i)

        for members in clusters.values():
            if len(members) >= 2:
                members_sorted = sorted(members)
                # similaridade média interna do grupo (indicador de qualidade)
                sub = sims[np.ix_(members_sorted, members_sorted)]
                off = sub[np.triu_indices(len(members_sorted), k=1)]
                avg = float(np.mean(off)) if off.size else 1.0
                groups.append({
                    "files": [valid_paths[m] for m in members_sorted],
                    "count": len(members_sorted),
                    "avg_similarity": round(avg, 4),
                })

        groups.sort(key=lambda g: g["count"], reverse=True)

    print(json.dumps({
        "success": True,
        "groups": groups,
        "total": total,
        "with_face": n,
        "duplicate_groups": len(groups),
        "duplicate_files": sum(g["count"] for g in groups),
    }, ensure_ascii=False), flush=True)
    print(f"[DUP] {len(groups)} grupo(s) de fotos repetidas.", file=sys.stderr, flush=True)


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
            print(json.dumps({"error": "Uso: insightface_service.py <imagem|--batch|--duplicates> ..."}))
            sys.exit(1)

        # Modo fotos repetidas
        if sys.argv[1] == '--duplicates' and len(sys.argv) >= 3:
            threshold = _arg_value('--sim-threshold', float, DEFAULT_DUP_THRESHOLD)
            find_duplicates(sys.argv[2], threshold)
            sys.exit(0)

        # Modos de reconhecimento exigem referência
        if '--reference' not in sys.argv:
            print(json.dumps({"error": "Parâmetro --reference é obrigatório"}))
            sys.exit(1)

        ref_idx = sys.argv.index('--reference')
        if ref_idx + 1 >= len(sys.argv):
            print(json.dumps({"error": "Falta caminho para --reference"}))
            sys.exit(1)

        reference_path = sys.argv[ref_idx + 1]
        sim_threshold = _arg_value('--sim-threshold', float, DEFAULT_SIM_THRESHOLD)

        print(f"[MAIN] Carregando referência: {reference_path}", file=sys.stderr, flush=True)
        ref = _load_reference(reference_path)
        if "error" in ref:
            print(json.dumps(ref))
            sys.exit(1)
        ref_matrix = ref["embeddings"]
        print(f"[MAIN] Referência pronta. sim>={sim_threshold}", file=sys.stderr, flush=True)

        if sys.argv[1] == '--batch' and len(sys.argv) >= 3:
            batch_recognition(sys.argv[2], ref_matrix, sim_threshold)
        else:
            result = _detect_and_match(sys.argv[1], ref_matrix, sim_threshold)
            print(json.dumps(result, ensure_ascii=False))

    except Exception as e:
        import traceback
        print(f"[MAIN] ERRO FATAL: {e}", file=sys.stderr, flush=True)
        print(traceback.format_exc(), file=sys.stderr, flush=True)
        print(json.dumps({"error": str(e)}))
        sys.exit(1)


if __name__ == '__main__':
    main()
