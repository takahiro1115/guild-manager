using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮の支配者1人の状態（2026年10月・§0.127、→ Systems.RulerSystem、docs/検討中_大迷宮の支配者.md）。
    /// 支配者は各フィールドの70F（前哨戦）と100F（決着）の主の代わりに待つ。体力は月をまたいで残る。
    /// セーブに残す。旧セーブ（§0.127 より前）には無いので、読み込みで補う（→ RulerSystem.EnsureRulers）。
    /// </summary>
    public sealed class RulerState
    {
        /// <summary>支配者の Id（→ RulerSystem.VesperaId など）。</summary>
        public string RulerId { get; set; } = "";

        /// <summary>今の体力（0〜RulerBalance.MaxHp）。</summary>
        public int Hp { get; set; }

        /// <summary>
        /// <see cref="Hp"/> がどの階の支配者のものか（70＝前哨戦、100＝決着。0＝まだ決まっていない）。
        /// 前哨戦で退いたあと、決着では体力が満タンに戻る（→ RulerSystem.Refresh）。
        /// </summary>
        public int HpFloor { get; set; }

        /// <summary>ヴェスペラの武器の並びの位置（剣→魔→技→剣…。月をまたいで続く）。</summary>
        public int WeaponIndex { get; set; }

        /// <summary>代表（順番つき）。選んだ顔ぶれは翌月も残る。</summary>
        public List<Guid> RepresentativeIds { get; set; } = new();

        /// <summary>この支配者と戦った回数（決闘の回数）。</summary>
        public int DuelCount { get; set; }

        /// <summary>最後に戦った週（無ければ null）。</summary>
        public int? LastDuelWeek { get; set; }

        /// <summary>
        /// 月を終えたときに決闘を待っている週（その月を代表が過ごしたあとの、新しい月の第1週）。決闘を終えたら null。
        /// 月のはじめに代表を選んですぐ決闘にならないよう、月を終えたときに付ける（→ RulerSystem.ProcessNewWeek）。
        /// </summary>
        public int? PendingDuelWeek { get; set; }

        /// <summary>出す部隊（メルジーネ、§0.129。SavedParty の Id、順番つき）。旧セーブでは空。</summary>
        public List<Guid> SquadIds { get; set; } = new();

        /// <summary>メルジーネの4曲の調べの並び（月ごとに入れ替わる、§0.129）。旧セーブでは空（読み込みで補う）。</summary>
        public List<SongMode> SongOrder { get; set; } = new();
    }

    /// <summary>メルジーネの調べ（§0.129）。</summary>
    public enum SongMode
    {
        /// <summary>激しい調べ（STR・VIT）。</summary>
        Fierce,
        /// <summary>静かな調べ（MND）。</summary>
        Calm,
        /// <summary>速い調べ（AGI・DEX）。</summary>
        Swift,
        /// <summary>甘い調べ（INT。INT の低い子は魅了される）。</summary>
        Sweet,
    }
}
