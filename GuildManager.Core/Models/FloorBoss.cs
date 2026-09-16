using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ダンジョンの階層ボス（→ ダンジョン攻略システム）。
    ///
    /// 通常クエスト（→ Quest）とは別枠の攻略対象。プレイヤーは「調査任務で情報を集める
    /// → 判明したギミックへ対策を組んだ編成で討伐する」という2段構えで攻略する。
    /// 無調査での突撃は未対策ペナルティにより壊滅する設計（→ DungeonResolver）。
    /// </summary>
    public class FloorBoss
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "";

        /// <summary>何階層のボスか（1〜）。難易度の基準になる。</summary>
        public int Floor { get; set; }

        public int MaxHp { get; set; }
        public int CurrentHp { get; set; }

        public List<BossGimmick> Gimmicks { get; set; } = new();

        /// <summary>
        /// 調査による情報解析率（0.0〜1.0）。調査任務の成果で上昇し、段階的に情報が開示される
        /// （→ ScoutingBalance の各閾値・IntelTier）。1.0（完全解析）に到達すると
        /// 討伐時の与ダメージにボーナスが付く（→ DungeonBalance.FullIntelDamageBonus）。
        /// </summary>
        public double IntelRate { get; set; } = 0.0;

        /// <summary>撃破済みか。</summary>
        public bool IsDefeated { get; set; } = false;
    }
}
