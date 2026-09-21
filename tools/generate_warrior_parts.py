"""
Young Warrior Woman パーツ分離プロトタイプスクリプト
"""
import os
import cv2
import numpy as np

def analyze_parts():
    img_path = r'C:/Users/shimizu/.gemini/antigravity/brain/6cc41094-2709-49a2-a8fe-c7998fb6d1d6/.user_uploaded/media_1789867669806.png'
    img = cv2.imread(img_path, cv2.IMREAD_UNCHANGED)
    h, w, _ = img.shape
    b, g, r, a = cv2.split(img)

    # 1. 髪マスクの作成
    # 髪色: 白〜淡いグレー。S < 45, V > 100。
    # ただし三つ編みには影部分 (Vが60〜120程度) もある。
    # 背景透過 (a == 0) を除外。
    hsv = cv2.cvtColor(cv2.merge([b, g, r]), cv2.COLOR_BGR2HSV)
    H, S, V = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]

    # 肌色の定義
    # 肌は R > G > B, (R - B) >= 10, V >= 150
    # 耳・首・鼻・頬・額の肌色
    skin_diff = r.astype(np.int16) - b.astype(np.int16)
    skin_diff_rg = r.astype(np.int16) - g.astype(np.int16)
    is_skin = (a > 30) & (r > 190) & (g > 165) & (b > 150) & (skin_diff >= 10) & (skin_diff <= 55) & (skin_diff_rg >= 0)

    # 目元の肌（暗い影の部分など）
    # 唇（より赤みが強い）
    is_lip = (a > 30) & (skin_diff >= 25) & (r > 180) & (y_grid := np.indices((h, w))[0] > 230) & (y_grid < 270)

    # 顔全体の輪郭マスク（肌色からモルフォロジーで顔面領域を抽出）
    face_region = is_skin | is_lip
    # 穴埋めと拡張
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))
    face_closed = cv2.morphologyEx(face_region.astype(np.uint8), cv2.MORPH_CLOSE, kernel)

    cv2.imwrite('tools/debug_face_mask.png', face_closed * 255)
    print("Face mask generated.")

if __name__ == '__main__':
    analyze_parts()
