# 計測の道具（判断の重み）

> **2026-10-11（03 §0.96）に `tools/balance_sim` の `audit` モードとして取り込んだ。** 今はそちらを使う（→ `tools/balance_sim/README.md`）。以下は検証のときの記録。

`tools/balance_sim` の写しに、画面と同じ流れ（部隊の方針で自動出撃 →「次の月へ」→ 月の途中で止まったら手番）で遊ばせる `audit` モードを足したもの。
2026-10-11 の検証レポート（`../report.md`）の数字はこれで出した。**リポジトリでは組み立てない**（`.txt` と `.diff` で置いてある）。

## 組み立て方

```bash
cp -r tools/balance_sim tools/decision_audit
patch tools/decision_audit/Program.cs < "docs/検証レポート/2026-10-11_単調さと判断の手応え/計測の道具/Program.cs.diff"
cp "docs/検証レポート/2026-10-11_単調さと判断の手応え/計測の道具/Audit.cs.txt" tools/decision_audit/Audit.cs
dotnet run --project tools/decision_audit -c Release -- audit 30 1440 L3 simple
```

`Program.cs.diff` の中身：`GameSim` を `partial` にする、`audit` モードの入口、操作を数える `Count(...)` を各操作に足す、くじ引きの選び方（`randhire` など）。

## 使い方

`audit 回数 最大週数 段階 [オプション…]`

| 段階 | プレイヤー |
|---|---|
| `L0` | 放置：最初に部隊を1つ組んで潜行の方針を付け、あとは「次の月へ」だけ |
| `L1` | 最小限：毎手番、部隊を組み直す・年1回の採用・契約交渉 |
| `L2` | L1＋遺物・店・施設・研究・教官・訓練・大会 |
| `L3` | L2＋依頼・霊薬・秘薬（balance_sim の campaign と同じ手入れ） |

| オプション | 意味 |
|---|---|
| `simple` | 部隊を強い順の4人＋おまかせで組む（無しはボスのギミックに合わせて組む） |
| `notourney` `nocomm` `noloot` `nofac` `notrain` `noresearch` `noelixir` `nosf` `nonego` `notrainer` `norecruit` | その手入れをしない |
| `randhire` `randtourney` `randcomm` `randequip` `randbuild` `randtrain` `randsquad` | その選び方をくじ引きにする |
| `bold` `cautious` | 扉前の構えを強気・慎重にする（既定は標準） |
| `notutorial` `dump=週` `dumpdir=フォルダ` | 手ほどき「なし」で始め、その週のセーブを書き出す（画面を撮るため） |
| `trace` | 週ごとの経過を出す |

出すもの：クリアの年・敗北・撃破数の推移・1年の手番・何もしなかった手番・操作の内訳・止まった理由・扉前の撤退の理由・機嫌と所持金の張り付き・月報の「主な出来事」が空だった割合。

## 気をつけること

- 同じ設定でも±1年ほど揺れる（レポートの BUG-06、大会の暦の乱数の種が部隊の Guid のハッシュ）。比べるときは30回以上で。
- 計測のプレイヤーは決まった手順で動く。人の上手さは測れない。

## 画面を撮る autoload（VerifyShots.cs.txt）

サンドボックス（実セーブと保存先を分けた写し）に autoload として差し込み、画面を順に開いて撮る検証用のスクリプト。
シナリオは環境変数 `SHOT_SCENARIO`（`midgame`・`newgame`・`stage1`・`stage2`）、出力先は `SHOT_DIR`。手順は `../引継ぎ.md` の6章。**リポジトリの Godot プロジェクトには入れない。**
