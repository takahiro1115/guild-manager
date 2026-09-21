"""
eyes.png 抽出テストスクリプト
"""
import cv2
import numpy as np

def test_eyes_extraction():
    img_path = r'C:/Users/shimizu/.gemini/antigravity/brain/6cc41094-2709-49a2-a8fe-c7998fb6d1d6/.user_uploaded/media_1789867669806.png'
    img = cv2.imread(img_path, cv2.IMREAD_UNCHANGED)
    h, w, _ = img.shape
    b, g, r, a = cv2.split(img)

    y_grid, x_grid = np.indices((h, w))

    # 目元のROI (左右の目と眉を含む領域)
    left_eye_roi = (x_grid >= 150) & (x_grid <= 212) & (y_grid >= 138) & (y_grid <= 204)
    right_eye_roi = (x_grid >= 220) & (x_grid <= 282) & (y_grid >= 132) & (y_grid <= 202)
    eyes_roi = left_eye_roi | right_eye_roi

    # 基準となる肌色（目のすぐ周囲の肌色）の中央値
    skin_sample_roi = (x_grid >= 180) & (x_grid <= 240) & (y_grid >= 205) & (y_grid <= 225)
    ref_b = np.median(b[skin_sample_roi])
    ref_g = np.median(g[skin_sample_roi])
    ref_r = np.median(r[skin_sample_roi])
    print(f"Reference skin color: BGR=[{ref_b:.1f}, {ref_g:.1f}, {ref_r:.1f}]")

    # 色差による肌色判定 (RGBユークリッド距離 or 色相・彩度・明度差)
    diff_b = b.astype(np.float32) - ref_b
    diff_g = g.astype(np.float32) - ref_g
    diff_r = r.astype(np.float32) - ref_r
    color_dist = np.sqrt(diff_b**2 + diff_g**2 + diff_r**2)

    diff_rb = r.astype(np.int16) - b.astype(np.int16)
    
    # 肌色判定: 参照肌色に近く、かつ赤みがある
    is_pure_skin = (color_dist < 22) & (diff_rb >= 12) & (r > 220)

    # 目の構成要素:
    # 1. 暗い線（まつ毛、アイライン、瞳孔、影）: 明度が低い
    # 2. 瞳・虹彩: 淡いグレー (彩度が低く、diff_rb < 10)
    # 3. 白目: 明るいが肌色よりも青白い (diff_rb <= 10)
    # 4. 眉毛: 淡いグレー (diff_rb < 12 かつ明度が少し暗い)
    is_eye_element = eyes_roi & (
        (color_dist >= 18) | 
        (diff_rb < 10) | 
        (r < 225) |
        (b < 190)
    ) & ~is_pure_skin

    # ソフトエッジ（フェザリング）
    # color_dist に基づく滑らかなアルファ値
    # color_dist < 12: alpha = 0
    # color_dist > 25: alpha = 255
    # 中間: 線形補間
    alpha_eyes = np.zeros((h, w), dtype=np.uint8)
    
    # 基本の目元アルファ
    raw_alpha = np.clip((color_dist - 12.0) / (26.0 - 12.0) * 255.0, 0, 255).astype(np.uint8)
    # 白目部分（color_distは小さめだがdiff_rbが小さい）
    sclera_boost = (diff_rb <= 9) & (r > 210) & (color_dist >= 8)
    raw_alpha[sclera_boost] = np.maximum(raw_alpha[sclera_boost], 230)

    # ROI外は0
    alpha_eyes[eyes_roi] = raw_alpha[eyes_roi]

    # 目元の輪郭（まぶたの境界）を整えるモルフォロジー
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    alpha_eyes = cv2.morphologyEx(alpha_eyes, cv2.MORPH_CLOSE, kernel)
    # わずかに平滑化
    alpha_eyes = cv2.GaussianBlur(alpha_eyes, (3, 3), 0.5)
    alpha_eyes[~eyes_roi] = 0

    eyes_png = img.copy()
    eyes_png[:, :, 3] = alpha_eyes

    cv2.imwrite('tools/debug_eyes_full.png', eyes_png)
    print("Saved tools/debug_eyes_full.png, non-zero alpha pixels:", np.sum(alpha_eyes > 0))

if __name__ == '__main__':
    test_eyes_extraction()
