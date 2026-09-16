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
    /// 配置による個人CP補正（→ Balance/PlacementBalance・combat.csv）は、新3職にも既存職と
    /// 同じ考え方で設定している（騎士＝前衛1.2、学者＝後衛1.2、盗賊＝前後とも1.0）。
    /// ここで決まる本来の配置と組み合わさることで、同じ役割の職業間に戦闘格差が出ないようにしている。
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
