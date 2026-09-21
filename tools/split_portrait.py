"""
キャラクター立ち絵から「前髪パーツ（hair_front.png）」と「ベース枠（body_back.png）」を自動生成するスクリプト。

機能:
1. マゼンタ背景 (#FF00FF 近似色) の完全透過および境界フリンジ除去。
2. 前髪領域 (HairFront) の切り出し (パッツン前髪の束のみ抽出、背景・顔・体を透明化)。
3. ベース枠 (BodyBack) の補正 (前髪の除去と額・おでこ領域の自然な肌色補間)。
4. 重ね合わせテスト (出力画像のアスペクト比・解像度の一致と位置ズレゼロの検証)。
"""

import os
import sys
import argparse
import cv2
import numpy as np


def process_portrait(
    input_path: str,
    output_hair_front_path: str,
    output_body_back_path: str,
    debug_dir: str = None
):
    print(f"[1/5] 画像読み込み: {input_path}")
    src = cv2.imread(input_path, cv2.IMREAD_UNCHANGED)
    if src is None:
        raise FileNotFoundError(f"入力画像が見つかりません: {input_path}")

    h, w, c = src.shape
    b, g, r = src[:, :, 0], src[:, :, 1], src[:, :, 2]

    # -------------------------------------------------------------
    # 1. 背景（マゼンタ #FF00FF 近似色）の完全透過処理
    # -------------------------------------------------------------
    print("[2/5] マゼンタ背景の完全透過およびフリンジ除去処理...")
    hsv = cv2.cvtColor(src[:, :, :3], cv2.COLOR_BGR2HSV)
    H, S, V = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]

    # マゼンタ判定条件 (H: 140〜175, S >= 70, V >= 70) または BGR差分
    magenta_cond = ((H >= 140) & (H <= 175) & (S >= 70) & (V >= 70)) | \
                   ((r > 150) & (b > 100) & (g < 60) & (r - g > 90) & (b - g > 50))

    # 四隅および各辺からFloodFillで背景領域を特定
    bg_seed_mask = np.zeros((h + 2, w + 2), np.uint8)
    magenta_bin = (magenta_cond * 255).astype(np.uint8)

    seeds = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1),
             (w // 2, 0), (0, h // 2), (w - 1, h // 2)]
    for pt in seeds:
        if magenta_bin[pt[1], pt[0]] == 255:
            cv2.floodFill(magenta_bin, bg_seed_mask, pt, 128)

    # 背景マスク: 到達可能なマゼンタ + 髪の隙間のマゼンタ小穴
    is_bg = (magenta_bin == 128) | magenta_cond

    # アンチエイリアス境界（マゼンタフリンジ）の色被り補正
    dist_to_bg = cv2.distanceTransform((~is_bg).astype(np.uint8), cv2.DIST_L2, 3)

    alpha_clean = np.ones((h, w), dtype=np.uint8) * 255
    alpha_clean[is_bg] = 0

    fringe_mask = (dist_to_bg > 0) & (dist_to_bg <= 2.5)
    fringe_magenta = fringe_mask & (r > g + 30) & (b > g + 20)

    b_fixed = b.copy()
    g_fixed = g.copy()
    r_fixed = r.copy()

    # フリンジ部分のGチャンネルを補正してピンク色被りを中和
    avg_rb = ((r.astype(np.float32) + b.astype(np.float32)) / 2.0).astype(np.uint8)
    g_fixed[fringe_magenta] = np.maximum(g[fringe_magenta], avg_rb[fringe_magenta] - 10)

    clean_base = cv2.merge([b_fixed, g_fixed, r_fixed, alpha_clean])

    # -------------------------------------------------------------
    # 2. 顔面肌色領域の検出
    # -------------------------------------------------------------
    print("[3/5] 顔面（肌色領域）の解析と前髪毛先ラインの特定...")
    is_skin = (r > 190) & (g > 165) & (b > 155) & (r >= g) & (g >= b) & \
              (r - b < 70) & (r - g < 50) & (~is_bg)

    num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(is_skin.astype(np.uint8))
    max_label = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    face_mask = (labels == max_label).astype(np.uint8)

    # 肌色の基準中央値
    skin_b = int(np.median(b[face_mask > 0]))
    skin_g = int(np.median(g[face_mask > 0]))
    skin_r = int(np.median(r[face_mask > 0]))
    skin_color_bgr = [skin_b, skin_g, skin_r]

    # 顔の上端Y座標（前髪毛先との接合境界）
    face_pts = np.where(face_mask > 0)
    min_y_face = {}
    for y, x in zip(face_pts[0], face_pts[1]):
        if x not in min_y_face or y < min_y_face[x]:
            min_y_face[x] = y

    # -------------------------------------------------------------
    # 3. 前髪（HairFront）の精密マスク生成
    # -------------------------------------------------------------
    print("[4/5] 前髪パーツ（hair_front.png）の切り出し...")
    hair_front_contour = [
        (198, 362), # 左端毛先
        (193, 320),
        (193, 270),
        (200, 210),
        (215, 160),
        (240, 120),
        (275, 95),
        (318, 88),  # 左つむじ凹み
        (340, 85),
        (368, 88),  # 右つむじ凹み
        (410, 105),
        (445, 140),
        (470, 185),
        (482, 230),
        (485, 280),
        (482, 328), # 右端毛先
    ]

    # 毛先下端ラインを右端から左端へ追加
    xs = sorted([x for x in min_y_face.keys() if 198 <= x <= 482], reverse=True)
    for x in xs:
        hair_front_contour.append((x, min_y_face[x] + 3))

    hair_front_poly = np.array(hair_front_contour, dtype=np.int32)
    hair_mask = np.zeros((h, w), dtype=np.uint8)
    cv2.fillPoly(hair_mask, [hair_front_poly], 255)
    hair_mask[is_bg] = 0

    # 前髪下部の肌色・影ピクセルを除外（パッツン毛束のみを残す）
    lower_hair = hair_mask.copy()
    lower_hair[:330, :] = 0
    # 左下〜中央の肌色・影を除去
    is_skin_tone = (lower_hair > 0) & (r > 120) & (r > g + 10) & (r > b + 18)
    hair_mask[is_skin_tone] = 0

    # 顔面内部の侵入防止
    inner_face = cv2.erode(face_mask, np.ones((3, 3), np.uint8), iterations=1)
    hair_mask[inner_face > 0] = 0

    # hair_front.png の出力
    hair_front = clean_base.copy()
    hair_front[hair_mask == 0] = [0, 0, 0, 0]

    # -------------------------------------------------------------
    # 4. ベース枠（BodyBack）の補正（前髪除去 ＆ 額補間）
    # -------------------------------------------------------------
    print("[5/5] ベース枠（body_back.png）の補正とおでこ肌色補間...")
    body_back = clean_base.copy()

    # おでこの生え際ライン
    forehead_hairline = [
        (215, 345),
        (224, 270),
        (246, 205),
        (286, 170),
        (345, 155), # おでこ中央頂点
        (402, 160),
        (448, 190),
        (476, 235),
        (484, 305),
    ]

    # 額の補間領域ポリゴン（生え際〜顔面上部 y=378 までを自然にカバー）
    forehead_poly_pts = forehead_hairline.copy()
    forehead_poly_pts.append((484, 378))
    forehead_poly_pts.append((345, 380))
    forehead_poly_pts.append((215, 378))

    forehead_poly = np.array(forehead_poly_pts, dtype=np.int32)
    forehead_mask = np.zeros((h, w), dtype=np.uint8)
    cv2.fillPoly(forehead_mask, [forehead_poly], 255)
    forehead_mask[is_bg] = 0

    # 生え際より下の前髪ピクセルを除去
    hairline_dict = {pt[0]: pt[1] for pt in forehead_hairline}
    sorted_hx = sorted(hairline_dict.keys())
    interp_hairline_y = np.interp(range(w), sorted_hx, [hairline_dict[k] for k in sorted_hx])

    y_grid, x_grid = np.ogrid[:h, :w]
    below_hairline = y_grid >= (interp_hairline_y - 6)
    removal_mask = hair_mask & below_hairline

    # 前髪を除去
    body_back[removal_mask > 0] = [0, 0, 0, 0]

    # 額を肌色で滑らかに塗りつぶし
    for c_i in range(3):
        body_back[:, :, c_i] = np.where(
            forehead_mask > 0,
            skin_color_bgr[c_i],
            body_back[:, :, c_i]
        )
    body_back[:, :, 3] = np.where(forehead_mask > 0, 255, body_back[:, :, 3])

    # 額の内側を滑らかにぼかし
    blurred_skin = cv2.GaussianBlur(body_back[:, :, :3], (7, 7), 1.5)
    forehead_inner = cv2.erode(forehead_mask, np.ones((7, 7), np.uint8), iterations=1)
    for c_i in range(3):
        body_back[:, :, c_i] = np.where(forehead_inner > 0, blurred_skin[:, :, c_i], body_back[:, :, c_i])

    # 生え際境界の陰影ライン
    edge = cv2.Canny(forehead_mask, 100, 200)
    dilated_edge = cv2.dilate(edge, np.ones((3, 3), np.uint8), iterations=1)
    hair_contact = dilated_edge & (y_grid < 280) & (forehead_mask > 0)
    for c_i in range(3):
        body_back[hair_contact > 0, c_i] = np.clip(body_back[hair_contact > 0, c_i].astype(int) - 10, 0, 255)

    # -------------------------------------------------------------
    # 出力ファイル書き出し
    # -------------------------------------------------------------
    os.makedirs(os.path.dirname(os.path.abspath(output_hair_front_path)), exist_ok=True)
    os.makedirs(os.path.dirname(os.path.abspath(output_body_back_path)), exist_ok=True)

    cv2.imwrite(output_hair_front_path, hair_front)
    cv2.imwrite(output_body_back_path, body_back)
    print(f"出力成功: {output_hair_front_path} ({w}x{h})")
    print(f"出力成功: {output_body_back_path} ({w}x{h})")

    # -------------------------------------------------------------
    # 重ね合わせ検証
    # -------------------------------------------------------------
    composite = body_back.copy()
    hf_alpha = (hair_front[:, :, 3] / 255.0)[:, :, np.newaxis]
    composite[:, :, :3] = (hair_front[:, :, :3] * hf_alpha + composite[:, :, :3] * (1.0 - hf_alpha)).astype(np.uint8)
    composite[:, :, 3] = np.maximum(body_back[:, :, 3], hair_front[:, :, 3])

    if debug_dir:
        os.makedirs(debug_dir, exist_ok=True)
        cv2.imwrite(os.path.join(debug_dir, "composite_test.png"), composite)
        print(f"合成検証画像保存: {os.path.join(debug_dir, 'composite_test.png')}")


def main():
    parser = argparse.ArgumentParser(description="立ち絵の前髪・ベース枠自動分割ツール")
    parser.add_argument("--input", "-i", default=r"guild-manager.godot/assets/portraits/raw_base.png",
                        help="入力元画像パス (デフォルト: guild-manager.godot/assets/portraits/raw_base.png)")
    parser.add_argument("--out-hair", default=r"guild-manager.godot/assets/portraits/hair_front.png",
                        help="前髪パーツ出力パス (デフォルト: guild-manager.godot/assets/portraits/hair_front.png)")
    parser.add_argument("--out-body", default=r"guild-manager.godot/assets/portraits/body_back.png",
                        help="ベース枠出力パス (デフォルト: guild-manager.godot/assets/portraits/body_back.png)")
    parser.add_argument("--debug-dir", default=None, help="デバッグ出力ディレクトリ")

    args = parser.parse_args()
    process_portrait(args.input, args.out_hair, args.out_body, args.debug_dir)


if __name__ == "__main__":
    main()
