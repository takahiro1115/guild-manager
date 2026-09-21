"""
前髪＋三つ編みおさげマスクの検証スクリプト
"""
import cv2
import numpy as np

def test_hair_mask():
    img_path = r'C:/Users/shimizu/.gemini/antigravity/brain/6cc41094-2709-49a2-a8fe-c7998fb6d1d6/.user_uploaded/media_1789867669806.png'
    img = cv2.imread(img_path, cv2.IMREAD_UNCHANGED)
    h, w, _ = img.shape
    b, g, r, a = cv2.split(img)

    # 1. 肌色検出
    # 肌色は R>G>B、R-B >= 10, V >= 150
    hsv = cv2.cvtColor(cv2.merge([b, g, r]), cv2.COLOR_BGR2HSV)
    H, S, V = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]

    skin_diff_rb = r.astype(np.int16) - b.astype(np.int16)
    skin_diff_rg = r.astype(np.int16) - g.astype(np.int16)
    
    # 肌（顔、首、耳）
    is_skin = (a > 30) & (r > 185) & (g > 160) & (b > 140) & \
              (skin_diff_rb >= 8) & (skin_diff_rg >= 0) & (skin_diff_rb <= 60)

    # 顔と首の接続領域
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7))
    skin_closed = cv2.morphologyEx(is_skin.astype(np.uint8), cv2.MORPH_CLOSE, kernel)

    # 目・眉・鼻・口の顔面内要素（肌に囲まれている部分）
    # y in [130, 290], x in [140, 290]
    face_inner = np.zeros((h, w), dtype=bool)
    face_inner[130:290, 140:290] = True
    
    # 2. 下部おさげ束の抽出 (y >= 260)
    # 左おさげ領域: x in [100, 210], y in [260, 480]
    # 右おさげ領域: x in [250, 345], y in [260, 480]
    left_braid_box = (np.indices((h, w))[1] >= 100) & (np.indices((h, w))[1] <= 210) & \
                     (np.indices((h, w))[0] >= 260) & (np.indices((h, w))[0] <= 480)
    right_braid_box = (np.indices((h, w))[1] >= 250) & (np.indices((h, w))[1] <= 345) & \
                      (np.indices((h, w))[0] >= 260) & (np.indices((h, w))[0] <= 480)

    # おさげの髪色: 明るいグレー〜白 (V > 120, S < 40) または留め具
    # 甲冑は暗い (V < 90)
    braid_hair = (left_braid_box | right_braid_box) & (a > 50) & (V > 100) & (S < 45)

    # 留め具（金具/革）のカバー (y in [370, 405])
    tie_bands = (left_braid_box | right_braid_box) & (a > 50) & \
                (np.indices((h, w))[0] >= 370) & (np.indices((h, w))[0] <= 405) & (V > 60)

    braid_mask = (braid_hair | tie_bands) & ~is_skin
    # 穴埋め
    braid_mask_u8 = cv2.morphologyEx(braid_mask.astype(np.uint8), cv2.MORPH_CLOSE, kernel)

    # 3. 頭部全体の髪マスク (y < 270)
    # 不透明領域から肌色、目、唇、首、耳を除外したもの
    head_hair = (a > 30) & (np.indices((h, w))[0] < 270) & ~is_skin & ~(face_inner & (V < 150))

    # 全体髪マスク
    full_hair = head_hair | (braid_mask_u8 > 0)

    # 保存して確認
    hair_img = img.copy()
    hair_img[:, :, 3] = np.where(full_hair, a, 0)
    cv2.imwrite('tools/debug_hair_front.png', hair_img)
    print("Saved tools/debug_hair_front.png")

if __name__ == '__main__':
    test_hair_mask()
