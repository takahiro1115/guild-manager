#!/usr/bin/env python3
"""冒険者の顔グラフィックをゲームへ取り込む（→ docs/03 §2.1・§0.46、肖像工房の書き出し形式）。

使い方:
    python3 tools/portrait_tool/import_portraits.py <zip またはフォルダ> [--dry-run] [--replace]

入力は肖像工房の書き出し（zip）か、同じ中身のフォルダ:
    {Id}.png ...           背景透過済みの顔グラフィック
    portraits_rows.csv     Id,Jobs,HairColor,EyeColor,note（肖像工房が作る行）

やること:
    1. 行と画像を検査する（Id の書式・重複、職業名、PNG の有無と形式）。1件でも問題があれば何も変えずに終了コード 1
    2. PNG を guild-manager.godot/assets/portraits/{Id}.png へ置く
    3. docs/04_バランス表/portraits.csv へ行を足す（--replace なら既存の同じ Id の行と画像を置き換える）

外部ライブラリは使わない（標準ライブラリのみ）。
"""
from __future__ import annotations

import argparse
import csv
import io
import re
import shutil
import sys
import tempfile
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PORTRAIT_DIR = Path("guild-manager.godot/assets/portraits")
PORTRAITS_CSV = Path("docs/04_バランス表/portraits.csv")
ROWS_FILE = "portraits_rows.csv"
HEADER = ["Id", "Jobs", "HairColor", "EyeColor", "note"]
JOBS = {"Warrior", "Knight", "Ranger", "Thief", "Mage", "Cleric", "Scholar"}
ID_RE = re.compile(r"^[A-Za-z0-9_-]+$")
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
# ゲーム内の最大表示は 120×160px（個人詳細）。肖像工房は既定で長辺 512px に縮めて書き出す。これを超える画像は警告だけ出す。
RECOMMENDED_MAX_SIDE = 512
# 初期メンバーの専用画像など、プール外の既存ファイル名。上書きで壊さない。
RESERVED = {"unknown_silhouette", "claudia", "rina", "fiona", "elsha", "gareth", "body_back", "eyes_fitted", "hair_front", "raw_base"}


def read_rows(text: str) -> list[dict[str, str]]:
    reader = csv.DictReader(io.StringIO(text.lstrip("﻿")))
    missing = [c for c in HEADER[:4] if c not in (reader.fieldnames or [])]
    if missing:
        raise ValueError(f"{ROWS_FILE} に列 {', '.join(missing)} がありません")
    return [{k: (row.get(k) or "").strip() for k in HEADER} for row in reader]


def png_size(data: bytes) -> tuple[int, int]:
    """PNG の IHDR から幅と高さを読む（シグネチャ8バイト＋長さ4＋"IHDR"4 の後ろ）。"""
    return int.from_bytes(data[16:20], "big"), int.from_bytes(data[20:24], "big")


def valid_jobs(value: str) -> bool:
    if value == "All":
        return True
    parts = value.split("|")
    return bool(parts) and all(p in JOBS for p in parts)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source", help="肖像工房の zip、または PNG と portraits_rows.csv を入れたフォルダ")
    ap.add_argument("--dry-run", action="store_true", help="検査と予定の表示だけ行い、何も変更しない")
    ap.add_argument("--replace", action="store_true", help="portraits.csv に既にある Id を置き換える")
    ap.add_argument("--repo", default=str(REPO), help="リポジトリの場所（既定：このスクリプトの2つ上）")
    args = ap.parse_args()

    repo = Path(args.repo)
    src = Path(args.source)
    with tempfile.TemporaryDirectory() as tmp:
        if src.is_file() and zipfile.is_zipfile(src):
            with zipfile.ZipFile(src) as z:
                for name in z.namelist():
                    # フォルダ付きで固めた zip でも、ファイル名だけで扱う（パスの .. は無視される）
                    base = Path(name).name
                    if base and not name.endswith("/"):
                        (Path(tmp) / base).write_bytes(z.read(name))
            src_dir = Path(tmp)
        elif src.is_dir():
            src_dir = src
        else:
            print(f"✖ {src} は zip でもフォルダでもありません", file=sys.stderr)
            return 1
        return run(src_dir, repo, args.dry_run, args.replace)


def run(src_dir: Path, repo: Path, dry_run: bool, replace: bool) -> int:
    errors: list[str] = []
    rows_path = src_dir / ROWS_FILE
    if not rows_path.exists():
        print(f"✖ {ROWS_FILE} が見つかりません（肖像工房の書き出しに含まれる）", file=sys.stderr)
        return 1
    try:
        rows = read_rows(rows_path.read_text(encoding="utf-8"))
    except (ValueError, UnicodeDecodeError) as e:
        print(f"✖ {e}", file=sys.stderr)
        return 1

    target_csv = repo / PORTRAITS_CSV
    existing_text = target_csv.read_text(encoding="utf-8").lstrip("﻿")
    existing = list(csv.DictReader(io.StringIO(existing_text)))
    existing_ids = {r["Id"] for r in existing}

    seen: set[str] = set()
    for i, row in enumerate(rows, start=2):
        rid = row["Id"]
        where = f"{ROWS_FILE} {i}行目（{rid or '空'}）"
        if not ID_RE.match(rid):
            errors.append(f"{where}: Id は半角英数字・_・- だけにする")
            continue
        if rid in seen:
            errors.append(f"{where}: 同じ Id が2回ある")
        seen.add(rid)
        if rid in RESERVED:
            errors.append(f"{where}: 既存の専用画像と同じ名前なので使えない")
        if rid in existing_ids and not replace:
            errors.append(f"{where}: portraits.csv に既にある（置き換えるなら --replace）")
        if not valid_jobs(row["Jobs"]):
            errors.append(f"{where}: Jobs「{row['Jobs']}」は All または {'/'.join(sorted(JOBS))} の | 区切り")
        png = src_dir / f"{rid}.png"
        if not png.exists():
            errors.append(f"{where}: {rid}.png が入っていない")
        elif png.read_bytes()[:8] != PNG_SIGNATURE:
            errors.append(f"{where}: {rid}.png が PNG 形式ではない")

    extra = sorted(p.stem for p in src_dir.glob("*.png") if p.stem not in seen)
    large = []
    for rid in sorted(seen):
        png = src_dir / f"{rid}.png"
        if png.exists():
            data = png.read_bytes()
            if data[:8] == PNG_SIGNATURE and max(png_size(data)) > RECOMMENDED_MAX_SIDE:
                large.append(f"{rid}（{'×'.join(map(str, png_size(data)))}px）")
    if errors:
        print("✖ 取り込みを中止した（何も変更していない）:", file=sys.stderr)
        for e in errors:
            print("  - " + e, file=sys.stderr)
        return 1

    added = [r for r in rows if r["Id"] not in existing_ids]
    replaced = [r for r in rows if r["Id"] in existing_ids]
    print(f"{'（予定）' if dry_run else ''}追加 {len(added)} 件・置き換え {len(replaced)} 件")
    for r in rows:
        mark = "置換" if r["Id"] in existing_ids else "追加"
        print(f"  [{mark}] {r['Id']}  {r['Jobs']}  髪:{r['HairColor']}  瞳:{r['EyeColor']}")
    if extra:
        print(f"  （CSV に行が無いので無視した PNG: {', '.join(extra)}）")
    if large:
        print(f"  ⚠ 長辺が {RECOMMENDED_MAX_SIDE}px を超える画像: {', '.join(large)}。肖像工房で「書き出す大きさ」を 512px にして送り直すとリポジトリが軽くなる")
    if dry_run:
        return 0

    dest_dir = repo / PORTRAIT_DIR
    for r in rows:
        shutil.copyfile(src_dir / f"{r['Id']}.png", dest_dir / f"{r['Id']}.png")

    by_id = {r["Id"]: r for r in rows}
    fieldnames = list(existing[0].keys()) if existing else HEADER
    out = io.StringIO()
    writer = csv.DictWriter(out, fieldnames=fieldnames, lineterminator="\n", extrasaction="ignore")
    writer.writeheader()
    for r in existing:
        writer.writerow(by_id.get(r["Id"], r))
    for r in added:
        writer.writerow(r)
    target_csv.write_text(out.getvalue(), encoding="utf-8")
    print(f"✔ {dest_dir.relative_to(repo)} に PNG {len(rows)} 枚、{PORTRAITS_CSV} に反映した")
    return 0


if __name__ == "__main__":
    sys.exit(main())
