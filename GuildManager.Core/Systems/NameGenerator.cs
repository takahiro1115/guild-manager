using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 名前の文化圏。仕様書 03 §2.4 参照。姓・二つ名は今回実装しない
    /// （ファーストネームのみ生成する。post-MVP、→ §11）ため、Adventurer側の
    /// 永続データとしては保持しない生成時限定の区分。
    /// </summary>
    public enum NameCulture
    {
        Western,
        Eastern,
    }

    /// <summary>
    /// 新人応募者の氏名ジェネレーター（和名・洋名）。仕様書 03 §2.4 参照。
    ///
    /// **女性限定ギルドの構造化（改訂）**：本作の冒険者は全員女性であり、男性が登場する
    /// 予定は無い（→ 01_コンセプト.md・Models.Gender）。以前は男女の名前プールを持ち、
    /// 出現率（MaleGenderChancePercent）を0%にすることで女性限定を表現していたが、
    /// それでは設定値を書き換えるだけで男性が復活してしまう状態だった。そのため以下を
    /// 撤去し、構造的に女性以外が生成され得ないようにした：
    ///  - 男性名プール（WesternMaleNames / EasternMaleNames）
    ///  - 性別の抽選（旧 RollGenderAndCulture）とバランス値 MaleGenderChancePercent
    ///  - 氏名生成APIの gender 引数
    /// 抽選対象として残るのは文化圏（洋名／和名）のみ。
    ///
    /// 設計メモ：他の全System（RecruitmentSystem・QuestResolver・AgingSystem等）と
    /// 同様に、乱数は`System.Random`を直接使わず`IRng`抽象を経由する（→ 05技術メモ§5
    /// 「決定性・再現性」）。
    /// </summary>
    public static class NameGenerator
    {
        private static readonly string[] WesternFemaleNames =
        {
            "セリア", "クラウディア", "イネス", "ミレイ", "マリー", "エレオノーラ", "セレスティア", "アリス",
            "ヴェロニカ", "シェリル", "ソフィア", "ディアナ", "ヘレナ", "ベアトリス", "マグラダ", "ロザリア",
            "アイリーン", "カトリーナ", "シルヴィア", "シャルロット",
        };

        // 現時点では未使用（姓はpost-MVP、→ 03 §11）。将来のために保持。
        private static readonly string[] WesternSurnames =
        {
            "アッシュフォード", "ヴァルシュタイン", "エルドリッジ", "グレイサム", "スターリング",
            "ベルモンド", "ハーコート", "ランカスター", "ヴァンガード", "メルヴィル",
        };

        private static readonly string[] EasternFemaleNames =
        {
            "シノ", "カエデ", "葵", "凛", "桔梗", "サクラ", "トモエ", "千代", "鈴", "弥生", "梢",
        };

        // 現時点では未使用（姓はpost-MVP、→ 03 §11）。将来のために保持。
        private static readonly string[] EasternSurnames =
        {
            "逢坂", "橘", "鳳", "九条", "葛葉", "氷室", "神楽", "白縫", "影森", "月影",
        };

        /// <summary>
        /// ランダムな冒険者名（ファーストネームのみ）を生成する。
        /// 参照するのは文化圏ごとの女性名プールのみ（→ クラスdocコメント）。
        /// </summary>
        public static string GenerateFirstName(NameCulture culture, IRng rng)
        {
            string[] pool = culture == NameCulture.Eastern ? EasternFemaleNames : WesternFemaleNames;
            return pool[rng.NextInt(0, pool.Length - 1)];
        }

        /// <summary>
        /// 現在の現役ロースターと重複しない名前を生成する
        /// （最大 NameGeneratorBalance.UniqueNameRetryLimit 回リトライ）。
        /// </summary>
        public static string GenerateUniqueFirstName(NameCulture culture, HashSet<string> existingNames, IRng rng)
        {
            for (int i = 0; i < NameGeneratorBalance.UniqueNameRetryLimit; i++)
            {
                string candidate = GenerateFirstName(culture, rng);
                if (!existingNames.Contains(candidate)) return candidate;
            }
            // リトライオーバー時は末尾に識別番号を付与
            return $"{GenerateFirstName(culture, rng)}2世";
        }

        /// <summary>
        /// 文化圏をランダムに決定する（洋名80%／和名20%、→ NameGeneratorBalance）。
        ///
        /// 旧 RollGenderAndCulture から性別の抽選を撤去したもの。冒険者は全員女性のため、
        /// 抽選する余地があるのは文化圏だけになった（→ クラスdocコメント）。
        /// </summary>
        public static NameCulture RollCulture(IRng rng) =>
            rng.NextInt(1, 100) <= NameGeneratorBalance.WesternCultureChancePercent
                ? NameCulture.Western
                : NameCulture.Eastern;
    }
}
