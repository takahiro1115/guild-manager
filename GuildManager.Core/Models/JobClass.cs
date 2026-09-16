namespace GuildManager.Core.Models
{
    /// <summary>
    /// 職業。仕様書 03 §2.1 参照。
    /// 旧v1.0の「Scout」は顧問「Scout Master」との名称衝突を避けるため Ranger に改名済み。
    ///
    /// 7職業化（改訂）：Knight・Thief・Scholar を追加した。
    /// 既存4職（Warrior/Ranger/Mage/Cleric）の数値を変えないよう、新3職は末尾に追加している
    /// （列挙値を数値として扱う箇所 → RecruitmentSystem の職業抽選など）。
    ///
    /// 配置（前衛／後衛）は職業から一意に決まる（→ PlacementRules.GetDefault）：
    ///  - 前衛：Warrior / Knight / Ranger / Thief
    ///  - 後衛：Mage / Cleric / Scholar
    /// </summary>
    public enum JobClass
    {
        /// <summary>重戦士（前衛）：STR・VIT寄りの攻守。</summary>
        Warrior,

        /// <summary>斥候（前衛）：AGI・DEX寄りの索敵・機動。</summary>
        Ranger,

        /// <summary>魔導士（後衛）：MND・INT寄りの魔法火力。</summary>
        Mage,

        /// <summary>神官（後衛）：MND・LDR寄りの支援・回復。</summary>
        Cleric,

        /// <summary>騎士（前衛）：VIT特化の重装・統率。部隊の盾役。</summary>
        Knight,

        /// <summary>盗賊（前衛）：AGI特化の俊敏・器用。罠解除・隠密に長ける。</summary>
        Thief,

        /// <summary>学者（後衛）：INT特化の解析役。ボスギミックの生体解析に長ける。</summary>
        Scholar,
    }
}
