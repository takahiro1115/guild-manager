# GuildManager — Godot 4.7.2 (.NET) + C#

女性限定の冒険者ギルドを経営するゲーム。ロジックは純粋な C#（Core）、表示は Godot（C#）に分けている。
詳しい構成と経緯は `docs/05_技術メモ.md`、ゲーム仕様は `docs/03_システム仕様_v2.0.md`。

## 構成と責務
- `GuildManager.Core/` … 純粋な C#（net8.0）。ゲームルール・計算式・セーブ。Godot を参照しない（`using Godot` はビルドエラーになる）
- `GuildManager.Core.Tests/` … xUnit。Core を変えたら必ずテストを足すか直す
- `guild-manager.godot/scripts/` … Node / Control を継承する表示層。計算は Core に委譲し、UI は Core の関数から値を取る（UI に式を持たせない）
- `guild-manager.godot/scenes/`・`guild-manager.godot/*.tscn` … シーン。`themes/dungeon_theme.tres` で文字サイズを本文16・補足15・見出し18にそろえる（RichText の太字も通常字体で表示する）
- `docs/04_バランス表/*.csv` … バランス値。読み込みにフォールバックが無いので、コードでキーを足したら CSV にも必ず足す（備考に ASCII のカンマを含めるときは `"…"` で囲む）
- `tools/portrait_tool/` … 肖像工房（顔グラフィックの加工ツール）。取り込みはスキル `import-portraits`
- `tools/balance_sim/` … バランス・シミュレーター（Core を回して要求値・成長・経済の手応えを数字で見る）。`dotnet run --project tools/balance_sim -- static` など（→ tools/balance_sim/README.md）

## コマンド（ルートに .sln は無いので、パスを指定する）
- テスト: `dotnet test GuildManager.Core.Tests`
- 特定のテスト: `dotnet test GuildManager.Core.Tests --filter "FullyQualifiedName~クラス名"`
- Godot 側のビルド: `dotnet build guild-manager.godot`（警告0を保つ）
- 変更したら両方を通し、テスト件数とビルドの警告数を報告する
- `dotnet format` は使わない（既存コードが整形規則にそろっておらず、無関係な差分が大量に出る）

## Godot 固有のルール
- ノード参照は `GetNode<T>("%Name")`（Unique Name）を使い、`_Ready()` でキャッシュする。深いパス文字列で辿らない
- Godot オブジェクトの破棄は `QueueFree()`。`Dispose()` を直接呼ばない
- `.tscn` は UI 変更の依頼があれば編集してよい。ext_resource の id・uid・`unique_name_in_owner` を壊さない
- 新しい .cs を作ると Godot が `.cs.uid` を生成する。スクリプトと一緒にコミットする

## セーブ互換
- セーブは JSON（`SaveLoadService`・`GameState.ToSaveData` / `FromSaveData`）。マイグレーションの仕組みは無い
- モデルに項目を足したら、旧セーブで既定値のまま読まれても壊れないようにし、必要なら `FromSaveData` で補う。補う処理のテストを足す

## 動作確認（ユーザーの実セーブを守る）
- 実セーブが `%APPDATA%\Godot\app_userdata\GuildManager.Godot\savegame.json` にある。プロジェクトをそのまま起動しない
- Godot 本体: `D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`（PATH に無い）
- 画面を確かめる手順:
  1. scratchpad へ `GuildManager.Core`（bin・obj を除く）と `docs/04_バランス表` を同じ相対配置でコピーし、`guild-manager.godot` を `.godot` と `addons/.godot_ai_update` を除いてコピーする
  2. コピー側の `project.godot` の `config/name` を変える（保存先のフォルダが分かれる）
  3. 検証用の C# を autoload として差し込み、`dotnet build` → `--headless --import` → `--headless --quit-after N`
- headless の編集時に出る「MCP | plugin disabled in headless mode」は正常

## ドキュメントの更新
- 仕様を変えたら `docs/03` に改訂の章（§0.xx）を足し、冒頭の改訂履歴の表も更新する。番号はブランチ間でぶつかりやすいので、`main` の最新と照らし合わせて決める
- バランス値を変えたら `docs/04_バランス表/README.md` も更新する
- 終わった作業・残った作業は `docs/06_タスクリスト.md` に記録する

## 進め方
- ユーザーへの質問と選択肢は日本語で書く
- NuGet パッケージなど新しい依存の追加は、事前に提案して承認を得る
- 段階的な指示のときは、今の段階だけを行う（後の段階の作業を先回りしない）
- コミットとプッシュは頼まれたときだけ
