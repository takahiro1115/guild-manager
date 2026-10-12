using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 志向（何をしたがるか、2026年10月・§0.100）。仕事の5つと動機の3つ。主志向1つ・副志向0〜1つ・苦手0〜1つを持つ（苦手は仕事だけ）。
    /// 並びの順は画面の並びと、志向を選ぶ乱数の順（→ Systems.PersonalitySystem）。
    /// </summary>
    public enum Drive
    {
        /// <summary>潜行（決戦を含む）。呼び名「武闘派」。</summary>
        Dive,
        /// <summary>迷宮調査。「探求派」。</summary>
        Survey,
        /// <summary>採取。「収穫派」。</summary>
        Gather,
        /// <summary>大会。「栄光派」。</summary>
        Tournament,
        /// <summary>訓練（訓練所・自主練）。「求道派」。</summary>
        Training,
        /// <summary>仲間（動機）。「仲間思い」。</summary>
        Comrades,
        /// <summary>お金（動機）。「稼ぎ屋」。</summary>
        Money,
        /// <summary>名声（動機）。「功名派」。</summary>
        Fame,
    }

    /// <summary>その志向への向き合い方。</summary>
    public enum DriveAttitude
    {
        Neutral,
        Main,
        Sub,
        Disliked,
    }

    /// <summary>見立てでどこまで分かったか（→ PersonalitySystem.Knowledge）。</summary>
    public enum PersonalityKnowledge
    {
        Unknown,
        Likely,
        Sure,
    }

    /// <summary>
    /// 冒険者の個性（2026年10月・§0.100、→ Systems.PersonalitySystem）。採用の候補・魂魄融和の娘・初期メンバーが加わるときに決める。
    /// 画面には出さず、見立て（Exposure の回数）で少しずつ分かる。相性の素地（Affinity）は分かることがない（隠す）。
    /// 旧セーブの冒険者は null（個性の効き目は働かない）。
    /// </summary>
    public sealed class Personality
    {
        public Drive Main { get; set; }
        public Drive? Sub { get; set; }
        /// <summary>苦手（仕事の5つのどれか）。無ければ null。</summary>
        public Drive? Disliked { get; set; }

        /// <summary>気質：−2＝とても慎重、−1＝慎重、0＝中庸、1＝大胆、2＝とても大胆。</summary>
        public int Temperament { get; set; }

        /// <summary>相性の素地（0〜255、循環。255と0は隣）。隠す。効き目は段4b から。</summary>
        public int Affinity { get; set; }

        /// <summary>採用の一言が匂わせる志向（主志向とは限らない）。</summary>
        public Drive HintDrive { get; set; }

        /// <summary>見立ての回数：志向の名前（"Dive" など）と "Temperament" → そのきっかけを見た回数。</summary>
        public Dictionary<string, int> Exposure { get; set; } = new();

        /// <summary>昇給・ボーナスを受けて、次の週の決算で「お金」の喜ぶきっかけにする回数（→ PersonalitySystem.NotifyMoneyJoy）。</summary>
        public int PendingMoneyJoy { get; set; }

        /// <summary>最後の手柄の週（名声の焦りの判定。加わった週で始める）。</summary>
        public int LastFameWeek { get; set; }

        public DriveAttitude AttitudeOf(Drive drive) =>
            drive == Main ? DriveAttitude.Main
            : drive == Sub ? DriveAttitude.Sub
            : drive == Disliked ? DriveAttitude.Disliked
            : DriveAttitude.Neutral;
    }
}
