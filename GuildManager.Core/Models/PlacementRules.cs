namespace GuildManager.Core.Models
{
    /// <summary>
    /// 配置（Placement）に関する職業ルール。仕様書 03 §4.2・§2.1 参照。
    ///
    /// 7職業化（改訂）：配置は**職業によって一意に決まる**ルールとした。
    ///  - 前衛（Front）：重戦士(Warrior)・騎士(Knight)・斥候(Ranger)・盗賊(Thief)
    ///  - 後衛（Back）：魔導士(Mage)・神官(Cleric)・学者(Scholar)
    ///
    /// プレイヤーが配置を選び直すUIは既に撤去済み（→ コミットd58de0b「前衛/後衛の表示・
    /// 選択操作を削除」）であり、実プレイ上の配置はここで決まる初期値がそのまま使われる。
    /// なお Adventurer.TrySetPlacement はCore上に残っている（テスト・将来拡張用）。
    ///
    /// ここで決まる配置は UI 上の役割表示（前衛／後衛バッジ）にのみ使う。職業×配置の個人CP補正
    /// （旧 PlacementBalance.GetPersonalCpCorrection・combat.csv の PlacementCorrection_*）は、配置が職業で
    /// 一意に決まる以上「職業ごとの固定倍率」に過ぎず形骸化していたため、2026年9月に撤廃した
    /// （→ DungeonPowerCalculator、03 §4.2・§0.23）。
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>職業から一意に決まる配置。冒険者生成時（初期メンバー・採用試験）に設定する。</summary>
        public static Placement GetDefault(JobClass jobClass) => jobClass switch
        {
            JobClass.Warrior => Placement.Front,
            JobClass.Knight => Placement.Front,
            JobClass.Ranger => Placement.Front,
            JobClass.Thief => Placement.Front,

            JobClass.Mage => Placement.Back,
            JobClass.Cleric => Placement.Back,
            JobClass.Scholar => Placement.Back,

            // 列挙型に職業を追加した際の設定漏れを黙って前衛扱いにしないよう、明示的に失敗させる。
            _ => throw new System.ArgumentOutOfRangeException(nameof(jobClass), jobClass, "配置ルールが未定義の職業です。"),
        };
    }
}
