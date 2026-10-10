#!/usr/bin/env python3
"""立ち絵（1536x2048・マゼンタ背景）を「輪郭素体／目元・眉／前髪」の3枚の透過PNGへ分割する。

使い方:
    python tools/extract_portrait_parts.py --input <入力画像> [--output-dir <出力先>] [--character-id <識別子>]

出力（すべて 1536x2048 の RGBA PNG。Godot では 3枚とも (0, 0) に置き、下から順に重ねる）:
    body_base.png       輪郭素体。前髪を除いた範囲の額・生え際を肌色で補完（cv2.inpaint）。耳・首・衣装・輪郭は元のまま
    eyes.png            目元・眉・瞳（ハイライト含む）だけ。周りの肌は透明
    hair_front.png      前髪（頭頂から額・目の高さまでの髪）だけ。顔・首・衣装は透明
    composite_test.png  上の3枚を body_base → eyes → hair_front の順に重ね直したもの（目視確認用）
    parts_meta.json     検出した顔・目元の矩形、背景色、再合成の誤差など

重ね順と一致の保証:
    各パーツの画素は背景透過後の元画像の画素そのもの。body_base で補完した画素（額）は必ず hair_front が
    不透明で覆い、パーツ同士が半透明で重なるのは「下の画素が元画像のまま」の所だけにしているので、
    3枚を重ねると透過後の元画像と画素単位で一致する（ログに誤差を出す）。

顔の位置:
    既定では肌色から顔を自動検出する。同じ構図の立ち絵を量産していて顔の輪郭が固定なら、
    --face-box（と --eye-box）で矩形を固定するか、前回の parts_meta.json を --layout で渡すと同じ基準で切れる。

必要なもの: Python 3.9+ / opencv-python / numpy / Pillow
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[1]
DEFAULT_PARTS_DIR = REPO / "guild-manager.godot" / "assets" / "portraits" / "parts"
CANVAS_W, CANVAS_H = 1536, 2048
MAGENTA = (255, 0, 255)
# 顔が見つからない時の顔矩形（キャンバスに対する比率 x, y, w, h）。バストアップ立ち絵の標準的な位置
FALLBACK_FACE_BOX = (0.36, 0.16, 0.28, 0.22)


# ---------------------------------------------------------------- 小道具

def log(msg: str) -> None:
    print(msg, flush=True)


def odd(n: float) -> int:
    n = max(1, int(round(n)))
    return n if n % 2 else n + 1


def ellipse_kernel(size: float) -> np.ndarray:
    k = odd(size)
    return cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))


def dilate(mask: np.ndarray, size: float) -> np.ndarray:
    return cv2.dilate(mask.astype(np.uint8), ellipse_kernel(size)).astype(bool)


def erode(mask: np.ndarray, size: float) -> np.ndarray:
    return cv2.erode(mask.astype(np.uint8), ellipse_kernel(size)).astype(bool)


def close(mask: np.ndarray, size: float) -> np.ndarray:
    return cv2.morphologyEx(mask.astype(np.uint8), cv2.MORPH_CLOSE, ellipse_kernel(size)).astype(bool)


def open_(mask: np.ndarray, size: float) -> np.ndarray:
    return cv2.morphologyEx(mask.astype(np.uint8), cv2.MORPH_OPEN, ellipse_kernel(size)).astype(bool)


def components(mask: np.ndarray, connectivity: int = 8):
    return cv2.connectedComponentsWithStats(mask.astype(np.uint8), connectivity=connectivity)


def labels_on_border(labels: np.ndarray, region: tuple[int, int, int, int] | None = None) -> np.ndarray:
    """矩形 region（既定は画像全体）の外周に掛かるラベル番号。"""
    if region is None:
        y0, y1, x0, x1 = 0, labels.shape[0], 0, labels.shape[1]
    else:
        x0, y0, w, h = region
        x1, y1 = x0 + w, y0 + h
    sub = labels[y0:y1, x0:x1]
    return np.unique(np.concatenate([sub[0], sub[-1], sub[:, 0], sub[:, -1]]))


def fill_holes(mask: np.ndarray, max_area: float | None = None) -> np.ndarray:
    """mask に囲まれた穴（画像の端に触れない非マスク領域）を埋める。max_area より大きい穴は残す。"""
    n, lab, stats, _ = components(~mask, connectivity=4)
    fill = np.ones(n, bool)
    fill[0] = False
    fill[labels_on_border(lab)] = False
    if max_area is not None:
        fill &= stats[:, cv2.CC_STAT_AREA] <= max_area
    return mask | fill[lab]


def feather(core: np.ndarray, width: float, allowed: np.ndarray) -> np.ndarray:
    """core=1.0、外側 width px で 0 へ落ちるアルファ（allowed の外には広げない）。"""
    a = core.astype(np.float32)
    if width <= 0:
        return a
    dist = cv2.distanceTransform((~core).astype(np.uint8), cv2.DIST_L2, 5)
    ring = (~core) & allowed & (dist <= width)
    a[ring] = 1.0 - dist[ring] / (width + 1.0)
    return a


def to_lab(rgb: np.ndarray) -> np.ndarray:
    return cv2.cvtColor(rgb.astype(np.float32) / 255.0, cv2.COLOR_RGB2LAB)


def lab_distance(lab: np.ndarray, ref: np.ndarray, l_weight: float = 0.5) -> np.ndarray:
    d = lab - ref.reshape(1, 1, 3)
    return np.sqrt((d[..., 0] * l_weight) ** 2 + d[..., 1] ** 2 + d[..., 2] ** 2)


def parse_box(text: str) -> tuple[int, int, int, int]:
    try:
        x, y, w, h = (int(v) for v in text.split(","))
    except ValueError as e:
        raise argparse.ArgumentTypeError("x,y,w,h（px の整数4つ）で指定する") from e
    if w <= 0 or h <= 0:
        raise argparse.ArgumentTypeError("幅と高さは正の数")
    return x, y, w, h


def parse_color(text: str) -> tuple[int, int, int]:
    t = text.strip().lstrip("#")
    try:
        if "," in t:
            r, g, b = (int(v) for v in t.split(","))
        elif len(t) == 6:
            r, g, b = (int(t[i:i + 2], 16) for i in (0, 2, 4))
        else:
            raise ValueError
    except ValueError as e:
        raise argparse.ArgumentTypeError("#RRGGBB か R,G,B で指定する") from e
    return r, g, b


def box_mask(shape: tuple[int, int], box: tuple[int, int, int, int]) -> np.ndarray:
    x, y, w, h = box
    m = np.zeros(shape, bool)
    m[max(0, y):max(0, y + h), max(0, x):max(0, x + w)] = True
    return m


def clamp_box(box, W: int, H: int) -> tuple[int, int, int, int]:
    x, y, w, h = (int(round(v)) for v in box)
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    return x0, y0, max(1, x1 - x0), max(1, y1 - y0)


# ---------------------------------------------------------------- 入出力

def load_image(path: Path) -> tuple[np.ndarray, np.ndarray | None]:
    im = Image.open(path)
    im.load()
    if im.mode in ("RGBA", "LA", "PA") or (im.mode == "P" and "transparency" in im.info):
        arr = np.asarray(im.convert("RGBA"))
        return arr[..., :3].copy(), arr[..., 3].copy()
    return np.asarray(im.convert("RGB")).copy(), None


def save_rgba(path: Path, rgb: np.ndarray, alpha: np.ndarray) -> None:
    rgba = np.dstack([rgb, alpha]).astype(np.uint8)
    rgba[alpha == 0, :3] = 0  # 完全透明な画素の色は捨てる（ファイルが軽くなる。Godot は Fix Alpha Border で縁を補う）
    Image.fromarray(rgba, "RGBA").save(path, compress_level=6)


# ---------------------------------------------------------------- 1. 背景透過とデフリンジ

def detect_key_color(rgb: np.ndarray, alpha_in: np.ndarray | None) -> tuple[int, int, int]:
    b = 8
    frame = [rgb[:b].reshape(-1, 3), rgb[-b:].reshape(-1, 3), rgb[:, :b].reshape(-1, 3), rgb[:, -b:].reshape(-1, 3)]
    pix = np.concatenate(frame)
    if alpha_in is not None:
        a = np.concatenate([alpha_in[:b].ravel(), alpha_in[-b:].ravel(), alpha_in[:, :b].ravel(), alpha_in[:, -b:].ravel()])
        pix = pix[a > 0]
        if len(pix) == 0:
            return MAGENTA
    return tuple(int(v) for v in np.median(pix, axis=0))


def key_excess(f: np.ndarray, hi: list[int], lo: list[int]) -> np.ndarray:
    """キー色らしさ。マゼンタなら min(R, B) - G（肌・髪・衣装ではほぼ 0 以下）。"""
    return f[..., hi].min(axis=-1) - f[..., lo].max(axis=-1)


def remove_background(rgb: np.ndarray, alpha_in: np.ndarray | None, key: tuple[int, int, int],
                      bg_tol: float, edge_width: int, despill: float) -> tuple[np.ndarray, np.ndarray, dict]:
    f = rgb.astype(np.float32)
    K = np.array(key, np.float32)
    dist = np.sqrt(((f - K) ** 2).sum(axis=2))

    # 背景本体: キー色に近く、画像の外周とつながっている所。キー色にごく近い色は囲まれた所（腕と胴の隙間など）も背景
    near = dist < bg_tol
    n, lab, _, _ = components(near, connectivity=4)
    is_bg = np.zeros(n, bool)
    is_bg[labels_on_border(lab)] = True
    is_bg[0] = False
    bg = is_bg[lab] | (dist < bg_tol * 0.5)
    if alpha_in is not None:
        bg |= alpha_in == 0

    band = dilate(bg, 2 * edge_width + 1) & ~bg            # アルファを推定し直す縁
    spill_zone = dilate(bg, 4 * edge_width + 1) & ~bg      # 色かぶりを取る縁（少し広め）

    alpha = np.ones(bg.shape, np.float32)
    alpha[bg] = 0.0
    hi = [i for i in range(3) if key[i] >= 128]
    lo = [i for i in range(3) if key[i] < 128]
    chroma_key = bool(hi) and bool(lo)
    if chroma_key:
        k_ex = max(float(key_excess(K, hi, lo)), 1.0)
        a_est = 1.0 - np.clip(key_excess(f, hi, lo) / k_ex, 0.0, 1.0)
    else:  # 白・灰など無彩色の背景は距離で推定
        a_est = np.clip((dist - bg_tol * 0.5) / (bg_tol * 1.5), 0.0, 1.0)
    alpha[band] = a_est[band]
    alpha[alpha < 0.04] = 0.0

    # 縁の画素から背景色の混ざりを取り除く: P = a*F + (1-a)*K → F = (P - (1-a)K) / a
    a3 = alpha[..., None]
    unmixed = np.clip((f - (1.0 - a3) * K) / np.maximum(a3, 1e-3), 0, 255)
    out = f.copy()
    out[band] = unmixed[band]
    if chroma_key and despill > 0:
        spill = np.clip(key_excess(out, hi, lo), 0, None) * despill
        for c in hi:
            out[..., c] = np.where(spill_zone, out[..., c] - spill, out[..., c])
        out = np.clip(out, 0, 255)

    a8 = np.round(alpha * 255).astype(np.uint8)
    if alpha_in is not None:
        a8 = np.minimum(a8, alpha_in)
    rgb8 = np.round(out).astype(np.uint8)
    rgb8[a8 == 0] = 0
    stats = {"background_px": int((a8 == 0).sum()), "edge_px": int(band.sum()),
             "semi_transparent_px": int(((a8 > 0) & (a8 < 255)).sum())}
    return rgb8, a8, stats


# ---------------------------------------------------------------- 2. 顔の位置

def rough_skin_mask(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    h, s, v = (hsv[..., i].astype(np.int32) for i in range(3))
    return ((h <= 16) | (h >= 170)) & (s >= 8) & (s <= 130) & (v >= 130) & (alpha == 255)


def refined_skin_mask(lab: np.ndarray, alpha: np.ndarray, ref: np.ndarray, tol: float) -> np.ndarray:
    chroma = np.hypot(lab[..., 1], lab[..., 2])
    ref_chroma = float(np.hypot(ref[1], ref[2]))
    return (lab_distance(lab, ref) < tol) & (chroma >= ref_chroma * 0.35) & (alpha == 255)


def locate_face(rgb: np.ndarray, lab: np.ndarray, alpha: np.ndarray, face_box, skin_tol: float) -> dict:
    H, W = alpha.shape
    rough = rough_skin_mask(rgb, alpha)
    seed = None
    method = "auto"
    if face_box is not None:
        method = "face-box"
        region = box_mask(alpha.shape, face_box)
        seed = rough & region
        if seed.sum() < 200:  # 肌色の当たりが少なければ矩形の中央下寄りを肌とみなす
            x, y, w, h = face_box
            seed = box_mask(alpha.shape, (x + w // 3, y + h // 2, w // 3, h // 4)) & (alpha == 255)
    else:
        cand = open_(rough & (np.arange(H)[:, None] < H * 0.6), 5)
        n, labs, stats, cents = components(cand)
        best, best_area = 0, 0
        for i in range(1, n):
            area = stats[i, cv2.CC_STAT_AREA]
            if area >= 0.002 * H * W and 0.2 * W <= cents[i][0] <= 0.8 * W and area > best_area:
                best, best_area = i, area
        if best:
            seed = labs == best
        else:
            method = "fallback"
            fx, fy, fw_, fh_ = FALLBACK_FACE_BOX
            face_box = clamp_box((fx * W, fy * H, fw_ * W, fh_ * H), W, H)
            log(f"  ! 顔の肌色が見つからないので既定の顔矩形 {face_box} を使う（--face-box で指定すると確実）")
            x, y, w, h = face_box
            seed = box_mask(alpha.shape, (x + w // 3, y + h // 2, w // 3, h // 4)) & (alpha == 255)

    core = erode(seed, 5)
    ref = np.median(lab[core if core.sum() > 100 else seed], axis=0)
    skin = refined_skin_mask(lab, alpha, ref, skin_tol)

    # 顔の肌の連結成分（種と最も重なる成分）
    restrict = dilate(box_mask(alpha.shape, face_box), 41) if face_box is not None else (np.arange(H)[:, None] < H * 0.65)
    n, labs, stats, _ = components(open_(skin & restrict, 3))
    overlap = np.bincount(labs[seed], minlength=n)
    overlap[0] = 0
    if n <= 1 or overlap.max() == 0:
        raise RuntimeError("顔の肌領域を特定できなかった。--face-box x,y,w,h で顔の矩形を指定する")
    fc = labs == int(overlap.argmax())

    ys = np.nonzero(fc.any(axis=1))[0]
    y_top, y_bot = int(ys[0]), int(ys[-1])
    widths = np.convolve(fc.sum(axis=1).astype(np.float32), np.ones(9) / 9, mode="same")
    upper_end = y_top + max(1, int((y_bot - y_top) * 0.7))
    y_wide = y_top + int(np.argmax(widths[y_top:upper_end + 1]))
    row = np.nonzero(fc[y_wide])[0]
    fw = float(row[-1] - row[0] + 1) if len(row) else float(W) * 0.25
    face_bottom = min(y_bot, int(y_wide + 0.9 * fw))
    fc[face_bottom + 1:] = False

    # 顔の幅と中心: 頬の高さ付近の行から取る
    rows = [r for r in range(y_top, face_bottom + 1) if fc[r].any()]
    ext = np.array([[np.nonzero(fc[r])[0][[0, -1]]] for r in rows]).reshape(-1, 2)
    fw = float(np.percentile(ext[:, 1] - ext[:, 0] + 1, 95))
    cx = float(np.median(ext[:, :].mean(axis=1)))

    pts = np.column_stack(np.nonzero(fc)[::-1]).astype(np.int32)
    hull_pts = cv2.convexHull(pts)
    hull = np.zeros(alpha.shape, np.uint8)
    cv2.fillConvexPoly(hull, hull_pts, 1)
    hull = hull.astype(bool)
    hx, hy, hw, hh = cv2.boundingRect(hull_pts)

    upper_half = fc & (np.arange(H)[:, None] <= (y_top + face_bottom) / 2)
    skin_rgb = np.median(rgb[upper_half if upper_half.sum() > 100 else fc], axis=0)
    return {
        "method": method, "skin": skin, "skin_ref_lab": ref, "skin_rgb": skin_rgb,
        "face": fc, "hull": hull, "hull_pts": hull_pts, "box": (hx, hy, hw, hh),
        "fw": fw, "cx": cx, "y_top": y_top, "y_wide": y_wide, "face_bottom": face_bottom,
    }


# ---------------------------------------------------------------- 3. 髪色・目元・前髪

def hair_color_model(lab: np.ndarray, alpha: np.ndarray, face: dict) -> np.ndarray | None:
    """額の上（前髪がある所）の肌以外の色を k-means でまとめる。"""
    H, W = alpha.shape
    fw, cx = face["fw"], face["cx"]
    y0, y1 = max(0, int(face["y_top"] - 0.6 * fw)), min(H, int(face["y_top"] + 0.1 * fw))
    x0, x1 = max(0, int(cx - 0.6 * fw)), min(W, int(cx + 0.6 * fw))
    region = np.zeros(alpha.shape, bool)
    region[y0:y1, x0:x1] = True
    samples = lab[region & ~face["skin"] & (alpha == 255)].astype(np.float32)
    if len(samples) < 200:
        return None
    rng = np.random.default_rng(0)
    if len(samples) > 20000:
        samples = samples[rng.choice(len(samples), 20000, replace=False)]
    k = 5
    criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.5)
    cv2.setRNGSeed(0)
    _, labels, centers = cv2.kmeans(samples, k, None, criteria, 3, cv2.KMEANS_PP_CENTERS)
    counts = np.bincount(labels.ravel(), minlength=k)
    return centers[counts >= 0.03 * len(samples)]


def hair_like_mask(lab: np.ndarray, centers: np.ndarray | None, tol: float) -> np.ndarray:
    if centers is None:
        return np.ones(lab.shape[:2], bool)
    d = np.min(np.stack([lab_distance(lab, c) for c in centers]), axis=0)
    return d < tol


def find_eye_band(alpha: np.ndarray, face: dict, eye_box) -> tuple[tuple[int, int, int, int], str]:
    H, W = alpha.shape
    if eye_box is not None:
        return clamp_box(eye_box, W, H), "eye-box"
    fw, cx, skin, hull = face["fw"], face["cx"], face["skin"], face["hull"]

    # 顔の輪郭の内側にあって、輪郭に触れていない肌以外の島 = 目・眉・鼻・口など
    hull_in = erode(hull, max(3, 0.03 * fw))
    interior = hull_in & ~skin & (alpha == 255)
    n, lab, stats, cents = components(interior)
    ring = hull_in & ~erode(hull_in, 3)
    touching = np.zeros(n, bool)
    touching[np.unique(lab[ring])] = True
    y_lo, y_hi = face["y_top"] - 0.1 * fw, face["y_wide"] + 0.25 * fw
    min_area = (0.025 * fw) ** 2
    cands = [i for i in range(1, n) if not touching[i] and stats[i, cv2.CC_STAT_AREA] >= min_area
             and y_lo <= cents[i][1] <= y_hi]
    if cands:
        big = max(stats[i, cv2.CC_STAT_AREA] for i in cands)
        keep = [i for i in cands if stats[i, cv2.CC_STAT_AREA] >= 0.15 * big]
        x0 = min(stats[i, cv2.CC_STAT_LEFT] for i in keep)
        x1 = max(stats[i, cv2.CC_STAT_LEFT] + stats[i, cv2.CC_STAT_WIDTH] for i in keep)
        y0 = min(stats[i, cv2.CC_STAT_TOP] for i in keep)
        y1 = max(stats[i, cv2.CC_STAT_TOP] + stats[i, cv2.CC_STAT_HEIGHT] for i in keep)
        # 片目が髪に隠れて見つからなくても両目が入るよう、顔の中心で左右対称に広げる
        half = max(cx - x0, x1 - cx, 0.3 * fw)
        x0, x1 = cx - half - 0.05 * fw, cx + half + 0.05 * fw
        y0, y1 = y0 - 0.15 * fw, y1 + 0.05 * fw
        if y1 - y0 < 0.25 * fw:
            y0 = y1 - 0.25 * fw
        method = "auto"
    else:
        x0, x1 = cx - 0.45 * fw, cx + 0.45 * fw
        y0, y1 = face["y_wide"] - 0.42 * fw, face["y_wide"] + 0.08 * fw
        method = "fallback"
        log("  ! 目の位置を検出できなかったので顔の比率から目元の範囲を決めた（--eye-box で指定可能）")
    return clamp_box((x0, y0, x1 - x0, y1 - y0), W, H), method


def build_eye_mask(alpha: np.ndarray, hair_like: np.ndarray, band, face: dict) -> np.ndarray:
    fw = face["fw"]
    in_band = box_mask(alpha.shape, band)
    cand = in_band & face["hull"] & ~face["skin"] & (alpha == 255)
    n, lab, stats, _ = components(cand)
    if n <= 1:
        return np.zeros(alpha.shape, bool)
    area = stats[:, cv2.CC_STAT_AREA].astype(np.float64)
    hair_frac = np.bincount(lab.ravel(), weights=hair_like.ravel().astype(np.float64), minlength=n) / np.maximum(area, 1)
    on_edge = np.zeros(n, bool)
    x, y, w, h = band
    sub = lab[y:y + h, x:x + w]
    on_edge[np.unique(np.concatenate([sub[0], sub[:, 0], sub[:, -1]]))] = True  # 上・左・右の辺
    keep = (area >= (0.01 * fw) ** 2) & ~(on_edge & (hair_frac > 0.6))  # 上から垂れてくる髪の束は除く
    keep[0] = False
    m = keep[lab]
    m = close(m, max(3, 0.012 * fw)) & in_band
    m = fill_holes(m, max_area=(0.15 * fw) ** 2)  # 白目・瞳のハイライトの抜けを埋める
    m = dilate(m, 3) & in_band & face["hull"] & (alpha == 255)  # 縁のアンチエイリアス分を含める
    return m


def build_hair_mask(alpha: np.ndarray, skin: np.ndarray, hair_like: np.ndarray, eye_mask: np.ndarray,
                    band, face: dict) -> np.ndarray:
    H, W = alpha.shape
    fw = face["fw"]
    hx, _, hw, _ = face["box"]
    bx, by, bw, bh = band
    zx0, zx1 = max(0, int(hx - 0.25 * fw)), min(W, int(hx + hw + 0.25 * fw))
    zy1 = min(H, by + bh)
    zone = np.zeros(alpha.shape, bool)
    zone[:zy1, zx0:zx1] = True

    cand = zone & (alpha > 0) & ~skin & hair_like & ~eye_mask
    n, lab, stats, _ = components(cand)
    keep = stats[:, cv2.CC_STAT_TOP] < by  # 目元より上の髪とつながっている束だけ（眉・まつ毛の島は除く）
    keep &= stats[:, cv2.CC_STAT_AREA] >= (0.01 * fw) ** 2
    keep[0] = False
    m = keep[lab]
    m = close(m, max(3, 0.01 * fw)) & zone & (alpha > 0) & ~eye_mask
    m = fill_holes(m, max_area=(0.12 * fw) ** 2) & zone & (alpha > 0) & ~eye_mask  # 髪のハイライトの抜けを埋める
    return m


def forehead_region(shape, face: dict, band) -> np.ndarray:
    """顔の輪郭（肌の凸包）＋前髪に隠れた額を楕円で補った領域。"""
    fw, cx = face["fw"], face["cx"]
    _, by, _, bh = band
    eye_y = by + bh / 2
    top = by - 0.45 * fw
    m = np.zeros(shape, np.uint8)
    cv2.ellipse(m, (int(round(cx)), int(round(eye_y))), (int(round(fw / 2)), int(round(eye_y - top))),
                0, 180, 360, 1, -1)
    return m.astype(bool) | face["hull"]


def inpaint_skin(rgb: np.ndarray, region: np.ndarray, skin: np.ndarray, fill_rgb: np.ndarray, fw: float) -> np.ndarray:
    """region を肌色で埋める。周りの髪・線の色が流れ込まないよう、近くの肌以外は一旦肌色で塗ってから inpaint する。"""
    out = rgb.copy()
    if not region.any():
        return out
    H, W = region.shape
    near = dilate(region, 0.1 * fw)
    ys, xs = np.nonzero(near)
    pad = int(0.05 * fw) + 8
    y0, y1 = max(0, ys.min() - pad), min(H, ys.max() + pad + 1)
    x0, x1 = max(0, xs.min() - pad), min(W, xs.max() + pad + 1)
    t = rgb[y0:y1, x0:x1].copy()
    r = region[y0:y1, x0:x1]
    pre = near[y0:y1, x0:x1] & ~skin[y0:y1, x0:x1] & ~r
    t[pre] = np.round(fill_rgb).astype(np.uint8)
    res = cv2.inpaint(np.ascontiguousarray(t), r.astype(np.uint8), max(3, int(0.015 * fw)), cv2.INPAINT_TELEA)
    res = cv2.GaussianBlur(res, (0, 0), max(1.5, 0.012 * fw))
    out[y0:y1, x0:x1][r] = res[r]
    return out


# ---------------------------------------------------------------- 4. 再合成チェック

def over(dst_rgb, dst_a, src_rgb, src_a):
    """ストレートアルファの source-over（Godot の通常ブレンドと同じ）。a は 0..1。"""
    out_a = src_a + dst_a * (1.0 - src_a)
    num = src_rgb * src_a[..., None] + dst_rgb * (dst_a * (1.0 - src_a))[..., None]
    out = np.where(out_a[..., None] > 0, num / np.maximum(out_a, 1e-12)[..., None], 0.0)
    return out, out_a


def composite(parts: list[tuple[np.ndarray, np.ndarray]]) -> tuple[np.ndarray, np.ndarray]:
    H, W = parts[0][1].shape
    rgb = np.zeros((H, W, 3), np.float64)
    a = np.zeros((H, W), np.float64)
    for prgb, pa in parts:
        prgb = prgb.astype(np.float64).copy()
        prgb[pa == 0] = 0
        rgb, a = over(rgb, a, prgb, pa.astype(np.float64) / 255.0)
    return np.clip(np.round(rgb), 0, 255).astype(np.uint8), np.round(a * 255).astype(np.uint8)


def debug_overlay(rgb, alpha, hair, eyes, inpainted, face, band) -> np.ndarray:
    a = alpha.astype(np.float32)[..., None] / 255.0
    vis = (rgb.astype(np.float32) * a + 96.0 * (1 - a))
    for m, color, k in ((hair, (255, 60, 60), 0.45), (eyes, (60, 140, 255), 0.55), (inpainted, (60, 255, 90), 0.35)):
        vis[m] = vis[m] * (1 - k) + np.array(color, np.float32) * k
    vis = np.clip(vis, 0, 255).astype(np.uint8)
    cv2.polylines(vis, [face["hull_pts"]], True, (255, 230, 0), 3)
    x, y, w, h = band
    cv2.rectangle(vis, (x, y), (x + w, y + h), (0, 230, 255), 3)
    return vis


# ---------------------------------------------------------------- 本体

def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--input", required=True, type=Path, help="入力画像（1536x2048。マゼンタ背景、または透過PNG）")
    ap.add_argument("--output-dir", type=Path, default=None,
                    help="出力先フォルダ（既定: guild-manager.godot/assets/portraits/parts/{character_id}/）")
    ap.add_argument("--character-id", default=None, help="識別子（既定: 入力ファイル名の拡張子を除いた部分）")
    g = ap.add_argument_group("顔の基準（固定構図向け）")
    g.add_argument("--layout", type=Path, default=None,
                   help="前回出力した parts_meta.json。中の face_box / eye_box を固定値として使う")
    g.add_argument("--face-box", type=parse_box, default=None, help="顔の矩形 x,y,w,h（px）。省略時は肌色から自動検出")
    g.add_argument("--eye-box", type=parse_box, default=None, help="目元・眉の矩形 x,y,w,h（px）。省略時は自動検出")
    g = ap.add_argument_group("調整値")
    g.add_argument("--key-color", type=parse_color, default=None,
                   help="背景色 #RRGGBB（省略時は画像の外周から検出。通常は #FF00FF）")
    g.add_argument("--bg-tol", type=float, default=90.0, help="背景色とみなす RGB 距離（既定 90）")
    g.add_argument("--edge-width", type=int, default=3, help="縁のアルファを推定し直す幅 px（既定 3）")
    g.add_argument("--despill", type=float, default=1.0,
                   help="縁の色かぶり除去の強さ 0〜1（既定 1。ピンク・紫の髪で縁がくすむなら下げる）")
    g.add_argument("--skin-tol", type=float, default=18.0, help="肌色とみなす色差（既定 18）")
    g.add_argument("--hair-tol", type=float, default=26.0, help="髪色とみなす色差（既定 26）")
    g.add_argument("--feather", type=float, default=1.5, help="前髪・目元パーツ外周のぼかし幅 px（既定 1.5、0 で無効）")
    g.add_argument("--erase-eyes-in-base", action="store_true",
                   help="body_base の目元も肌色で埋める（表情差分で目を差し替える場合）")
    g.add_argument("--debug", action="store_true", help="検出結果を色分けした debug_overlay.png も出す")
    return ap


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    t0 = time.perf_counter()
    src: Path = args.input
    if not src.is_file():
        log(f"エラー: 入力画像が見つからない: {src}")
        return 1
    char_id = args.character_id or src.stem
    out_dir: Path = args.output_dir or (DEFAULT_PARTS_DIR / char_id)

    face_box, eye_box = args.face_box, args.eye_box
    if args.layout is not None:
        meta_in = json.loads(args.layout.read_text(encoding="utf-8"))
        face_box = face_box or (tuple(meta_in["face_box"]) if meta_in.get("face_box") else None)
        eye_box = eye_box or (tuple(meta_in["eye_box"]) if meta_in.get("eye_box") else None)

    rgb_in, alpha_in = load_image(src)
    H, W = rgb_in.shape[:2]
    if (W, H) != (CANVAS_W, CANVAS_H):
        # アスペクト比が 3:4 (1536:2048) か判定 (誤差許容)
        aspect = W / H
        target_aspect = CANVAS_W / CANVAS_H
        if abs(aspect - target_aspect) < 0.02:
            log(f"  * 入力解像度が {W}x{H} px のため、規格の {CANVAS_W}x{CANVAS_H} px へ高品質リサイズ（Lanczos）して処理します")
            im_full = Image.open(src)
            im_resized = im_full.resize((CANVAS_W, CANVAS_H), Image.Resampling.LANCZOS)
            if im_resized.mode in ("RGBA", "LA", "PA") or (im_resized.mode == "P" and "transparency" in im_resized.info):
                arr = np.asarray(im_resized.convert("RGBA"))
                rgb_in, alpha_in = arr[..., :3].copy(), arr[..., 3].copy()
            else:
                rgb_in, alpha_in = np.asarray(im_resized.convert("RGB")).copy(), None
            H, W = CANVAS_H, CANVAS_W
        else:
            log(f"エラー: 入力はアスペクト比 3:4（基準: {CANVAS_W}x{CANVAS_H} px）にしてください（この画像は {W}x{H} px、比率 {aspect:.3f}）。")
            return 1
    log(f"入力: {src}  ({W}x{H}, {'RGBA' if alpha_in is not None else 'RGB'})  -> {out_dir}")

    # 1. 背景透過・デフリンジ
    key = args.key_color or detect_key_color(rgb_in, alpha_in)
    if args.key_color is None and np.linalg.norm(np.subtract(key, MAGENTA)) > 80:
        log(f"  ! 外周の色 {key} がマゼンタから離れている。その色を背景として扱う")
    rgb, alpha, bg_stats = remove_background(rgb_in, alpha_in, key, args.bg_tol, args.edge_width, args.despill)
    log(f"[1] 背景透過: 背景色 #{key[0]:02X}{key[1]:02X}{key[2]:02X}  透明 {bg_stats['background_px']:,} px / "
        f"縁の補正 {bg_stats['edge_px']:,} px")

    # 2. 顔
    lab = to_lab(rgb)
    face = locate_face(rgb, lab, alpha, face_box, args.skin_tol)
    fw = face["fw"]
    log(f"[2] 顔: {face['method']}  輪郭矩形 {face['box']}  顔幅 {fw:.0f} px  中心 x={face['cx']:.0f}")

    # 3. 目元・前髪
    centers = hair_color_model(lab, alpha, face)
    hair_like = hair_like_mask(lab, centers, args.hair_tol)
    band, band_method = find_eye_band(alpha, face, eye_box)
    eye_core = build_eye_mask(alpha, hair_like, band, face)
    hair_core = build_hair_mask(alpha, face["skin"], hair_like, eye_core, band, face)
    log(f"[3] 目元: {band_method} 範囲 {band}  {int(eye_core.sum()):,} px / 前髪 {int(hair_core.sum()):,} px"
        f"（髪色クラスタ {0 if centers is None else len(centers)}）")
    if not eye_core.any():
        log("  ! 目元の画素が空。--eye-box や --skin-tol を調整する")
    if not hair_core.any():
        log("  ! 前髪の画素が空。--hair-tol を上げるか --face-box を見直す")

    # 4. 輪郭素体: 顔の内側の前髪を肌色で補完。補完する画素は必ず前髪パーツが不透明で覆う
    opaque = alpha == 255
    fill_region = hair_core & forehead_region(alpha.shape, face, band) & opaque
    if args.erase_eyes_in_base:
        fill_region |= eye_core & opaque
    base_rgb = inpaint_skin(rgb, fill_region, face["skin"], face["skin_rgb"], fw)
    base_a = alpha.copy()
    base_a[hair_core & ~opaque] = 0  # 半透明の縁は前髪パーツだけが持つ（同じ半透明を2回重ねない）
    log(f"[4] 輪郭素体: 額・生え際の補完 {int(fill_region.sum()):,} px")

    # 5. パーツのアルファ（ぼかしは下の画素が元画像のままの不透明部分だけ）
    hair_a = feather(hair_core, args.feather, opaque & ~eye_core) * (alpha.astype(np.float32) / 255.0)
    eyes_a = feather(eye_core, args.feather, opaque & ~fill_region)
    hair_a8 = np.round(hair_a * 255).astype(np.uint8)
    eyes_a8 = np.round(eyes_a * 255).astype(np.uint8)
    eyes_a8[~opaque] = 0

    out_dir.mkdir(parents=True, exist_ok=True)
    paths = {
        "body_base": out_dir / "body_base.png",
        "eyes": out_dir / "eyes.png",
        "hair_front": out_dir / "hair_front.png",
        "composite_test": out_dir / "composite_test.png",
    }
    save_rgba(paths["body_base"], base_rgb, base_a)
    save_rgba(paths["eyes"], rgb, eyes_a8)
    save_rgba(paths["hair_front"], rgb, hair_a8)

    # 6. 書き出したファイルを読み直して再合成し、透過後の元画像と比べる
    reread = []
    for name in ("body_base", "eyes", "hair_front"):
        arr = np.asarray(Image.open(paths[name]).convert("RGBA"))
        if arr.shape[:2] != (CANVAS_H, CANVAS_W):
            log(f"エラー: {name}.png の大きさが {arr.shape[1]}x{arr.shape[0]} になっている")
            return 1
        reread.append((arr[..., :3], arr[..., 3]))
    comp_rgb, comp_a = composite(reread)
    save_rgba(paths["composite_test"], comp_rgb, comp_a)

    a_diff = np.abs(comp_a.astype(np.int16) - alpha.astype(np.int16))
    vis = (comp_a > 0) & (alpha > 0)
    c_diff = np.abs(comp_rgb.astype(np.int16) - rgb.astype(np.int16)).max(axis=2)
    c_diff[~vis] = 0
    mismatch = int(((a_diff > 0) | (c_diff > 0)).sum())
    exact = mismatch == 0
    log(f"[5] 再合成チェック: アルファ最大誤差 {int(a_diff.max())} / 色最大誤差 {int(c_diff.max())} / "
        f"不一致 {mismatch:,} px -> {'完全一致' if exact else '不一致あり'}")

    if args.debug:
        Image.fromarray(debug_overlay(rgb, alpha, hair_core, eye_core, fill_region, face, band)).save(
            out_dir / "debug_overlay.png")

    meta = {
        "character_id": char_id,
        "source": str(src),
        "canvas": [CANVAS_W, CANVAS_H],
        "key_color": "#%02X%02X%02X" % key,
        "face_box": [int(v) for v in face["box"]],
        "face_detect": face["method"],
        "eye_box": [int(v) for v in band],
        "eye_detect": band_method,
        "layer_order": ["body_base", "eyes", "hair_front"],
        "pixels": {"hair_front": int(hair_core.sum()), "eyes": int(eye_core.sum()), "inpainted": int(fill_region.sum())},
        "composite_check": {"max_alpha_diff": int(a_diff.max()), "max_color_diff": int(c_diff.max()),
                            "mismatch_px": mismatch, "exact": exact},
    }
    (out_dir / "parts_meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for p in list(paths.values()):
        log(f"  -> {p}")
    log(f"完了 ({time.perf_counter() - t0:.1f} 秒)")
    return 0 if exact else 2


if __name__ == "__main__":
    sys.exit(main())
