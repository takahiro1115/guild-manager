"""
おさげ束および前髪の精密抽出テスト
"""
import cv2
import numpy as np

def test_refined_hair():
    img_path = r'C:/Users/shimizu/.gemini/antigravity/brain/6cc41094-2709-49a2-a8fe-c7998fb6d1d6/.user_uploaded/media_1789867669806.png'
    img = cv2.imread(img_path, cv2.IMREAD_UNCHANGED)
    h, w, _ = img.shape
    b, g, r, a = cv2.split(img)

    y_grid, x_grid = np.indices((h, w))

    # 色空間変換
    hsv = cv2.cvtColor(cv2.merge([b, g, r]), cv2.COLOR_BGR2HSV)
    H, S, V = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]

    # 肌色判定 (赤みが強い、R-B >= 8, V >= 150)
    diff_rb = r.astype(np.int16) - b.astype(np.int16)
    is_skin = (a > 30) & (r > 180) & (g > 150) & (b > 130) & (diff_rb >= 7)

    # 1. 頭部〜前髪 (y < 260)
    # 髪色: (diff_rb < 7) & (V > 120) & (S < 45) & (a > 30)
    # またはハイライト (V > 240)
    is_white_hair = (a > 30) & (diff_rb < 7) & (V > 120) & (S < 50)
    
    # 顔面領域の除外マスク
    # 目・鼻・口・顎 (y in [160, 280], x in [155, 275])
    face_inner = (y_grid >= 160) & (y_grid <= 280) & (x_grid >= 155) & (x_grid <= 275)

    # 耳の除外 (右耳 x in [305, 355], y in [160, 230])
    is_ear = (x_grid >= 305) & (x_grid <= 355) & (y_grid >= 160) & (y_grid <= 230) & is_skin

    # 首の除外 (x in [190, 280], y in [250, 310])
    is_neck = (x_grid >= 190) & (x_grid <= 280) & (y_grid >= 250) & (y_grid <= 310) & is_skin

    # 前髪・頭部髪
    head_mask = (y_grid < 260) & (a > 30) & ~face_inner & ~is_skin & ~is_ear & ~is_neck

    # 2. 左右のおさげ束 (y >= 250)
    # 左おさげの探索範囲: x in [100, 205], y in [250, 475]
    # 右おさげの探索範囲: x in [250, 345], y in [250, 475]
    left_braid_roi = (x_grid >= 100) & (x_grid <= 205) & (y_grid >= 250) & (y_grid <= 475)
    right_braid_roi = (x_grid >= 250) & (x_grid <= 345) & (y_grid >= 250) & (y_grid <= 475)
    
    braid_roi = left_braid_roi | right_braid_roi

    # おさげの髪・留め具
    # 髪色 (明るい白〜グレー) または 留め具 (y in [370, 410], V > 50)
    braid_hair = braid_roi & (a > 30) & (
        ((V > 100) & (S < 45) & (diff_rb < 15)) | # 髪
        ((y_grid >= 370) & (y_grid <= 405) & (V > 50)) # 留め具
    ) & ~is_skin & ~is_neck

    # 甲冑ハイライト（孤立した点）を除去するため、おさげROI内で大きな連結成分のみ採用
    num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(braid_hair.astype(np.uint8))
    clean_braid_mask = np.zeros((h, w), dtype=bool)
    for i in range(1, num_labels):
        area = stats[i, cv2.CC_STAT_AREA]
        if area > 100: # 小さなノイズ・甲冑ハイライトを無視
            clean_braid_mask |= (labels == i)

    # 穴埋め（おさげ内部の影ピクセルなど）
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    clean_braid_mask_u8 = cv2.morphologyEx(clean_braid_mask.astype(np.uint8), cv2.MORPH_CLOSE, kernel)

    # 全体髪マスク
    final_hair_mask = head_mask | (clean_braid_mask_u8 > 0)

    # hair_front 画像生成
    hair_front = img.copy()
    hair_front[:, :, 3] = np.where(final_hair_mask, a, 0)
    cv2.imwrite('tools/debug_refined_hair_front.png', hair_front)

    # 評価
    print('Refined hair pixel count:', np.sum(final_hair_mask))
    print('Face inner overlap:', np.sum(final_hair_mask & face_inner))
    print('Chest armor (200-250, >310) overlap:', np.sum(final_hair_mask[310:, 200:250]))

if __name__ == '__main__':
    test_refined_hair()
