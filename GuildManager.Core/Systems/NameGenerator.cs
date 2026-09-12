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
    /// 事前調査メモ（項目48）：Gemini案のセクション3（C#実装コード）内の名前配列が、
    /// セクション2（マスターデータ、カタカナ・和名表記）と異なる英語ローマ字表記に
    /// なっていたため、セクション2のカタカナ・和名表記を正としてそのまま採用した。
    ///
    /// 設計メモ：他の全System（RecruitmentSystem・QuestResolver・AgingSystem等）と
    /// 同様に、乱数は`System.Random`を直接使わず`IRng`抽象を経由する（→ 05技術メモ§5
    /// 「決定性・再現性」）。Gemini案の元コードは`System.Random`を直接引数に取る設計
    /// だったが、テスト容易性・シード再現性のため`IRng`に差し替えた
    /// （→ GrowthBalance.PickJobWeightedStatと同じ「静的メソッドがIRngを引数で受け取る」
    /// パターンを踏襲）。
    /// </summary>
    public static class NameGenerator
    {
        private static readonly string[] WesternMaleNames =
        {
            "エドガー", "ロラン", "バルタザール", "ガレス", "レオン", "カディン", "ユリアン", "オスカー",
            "カリム", "リュカ", "トーマス", "アレク", "ヴァルター", "コンラッド", "ギルバート", "シドニー",
            "ダニエル", "ベネディクト", "マックス", "ルパート",
        };

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

        private static readonly string[] EasternMaleNames =
        {
            "レン", "イツキ", "ハヤテ", "ジン", "カゲツ", "ゲンシン", "タクマ", "タツミ", "ソウマ", "ヤマト",
            "カズマ", "シオン", "トウマ", "ナオツグ", "ライゾウ",
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

        /// <summary>ランダムな冒険者名（ファーストネームのみ）を生成する。</summary>
        public static string GenerateFirstName(Gender gender, NameCulture culture, IRng rng)
        {
            string[] pool = (culture, gender) switch
            {
                (NameCulture.Western, Gender.Male) => WesternMaleNames,
                (NameCulture.Western, Gender.Female) => WesternFemaleNames,
                (NameCulture.Eastern, Gender.Male) => EasternMaleNames,
                (NameCulture.Eastern, Gender.Female) => EasternFemaleNames,
                _ => WesternMaleNames,
            };
            return pool[rng.NextInt(0, pool.Length - 1)];
        }

        /// <summary>
        /// 現在の現役ロースターと重複しない名前を生成する
        /// （最大 NameGeneratorBalance.UniqueNameRetryLimit 回リトライ）。
        /// </summary>
        public static string GenerateUniqueFirstName(
            Gender gender, NameCulture culture, HashSet<string> existingNames, IRng rng)
        {
            for (int i = 0; i < NameGeneratorBalance.UniqueNameRetryLimit; i++)
            {
                string candidate = GenerateFirstName(gender, culture, rng);
                if (!existingNames.Contains(candidate)) return candidate;
            }
            // リトライオーバー時は末尾に識別番号を付与
            return $"{GenerateFirstName(gender, culture, rng)}2世";
        }

        /// <summary>
        /// 性別・文化圏をランダムに決定する（性別50/50、文化圏は洋名80%/和名20%、
        /// → NameGeneratorBalance。仮値、後日BAL側で調整予定）。
        /// </summary>
        public static (Gender Gender, NameCulture Culture) RollGenderAndCulture(IRng rng)
        {
            var gender = rng.NextInt(1, 100) <= NameGeneratorBalance.MaleGenderChancePercent ? Gender.Male : Gender.Female;
            var culture = rng.NextInt(1, 100) <= NameGeneratorBalance.WesternCultureChancePercent ? NameCulture.Western : NameCulture.Eastern;
            return (gender, culture);
        }
    }
}
