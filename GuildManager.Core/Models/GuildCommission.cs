using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 依頼の種類（→ Systems.CommissionSystem、03 §4.9・§0.64）。旧通常クエスト（§0.8で撤去）とは別物で、
    /// 新しい任務は作らず、大迷宮の任務の結果（撃破・解析）や在庫・在籍者を条件として照らし合わせる。
    /// </summary>
    public enum CommissionType
    {
        /// <summary>撃破：指定の階層ボスを期限までに倒す（誰が倒してもよい）。週次決算で判定する。</summary>
        Defeat,

        /// <summary>完全解析：指定の階層ボスを期限までに完全解析する。先に倒してしまうと達成できない。週次決算で判定する。</summary>
        Survey,

        /// <summary>納品：指定の素材を指定の個数、在庫から納める（プレイヤーの操作で即時に達成）。</summary>
        Deliver,

        /// <summary>派遣（§0.85。旧・献上）：指定の能力が基準以上の冒険者を1人、依頼人へひと季節貸す（プレイヤーの操作で即時に達成。本人は名簿に残り、LoanWeeks 後に成長して帰ってくる）。</summary>
        Loan,
    }

    /// <summary>
    /// 依頼1件（→ GameState.Commissions、Systems.CommissionSystem、03 §4.9・§0.64）。
    /// 季節のはじめに掲示され（Accepted=false）、受けると Accepted=true になる。達成・失敗・期限切れ・
    /// 断った依頼は一覧から外す（達成数は GameState.CommissionCompletions に依頼人ごとに数える）。
    ///
    /// セーブにはこのクラスをそのまま書き出す（ボスは Id で持ち、参照は都度フィールドから引く）。
    /// </summary>
    public class GuildCommission
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>依頼人のId（→ commission_clients.csv）。</summary>
        public string ClientId { get; set; } = "";

        public CommissionType Type { get; set; }

        /// <summary>対象のフィールドId（撃破・解析＝ボスの所属、納品＝素材の産地、派遣＝報酬の遺物の出土先）。</summary>
        public string FieldId { get; set; } = "";

        /// <summary>撃破・解析の対象ボスのId（→ FloorBoss.Id）。納品・派遣は null。</summary>
        public Guid? BossId { get; set; }

        /// <summary>納品する素材のId（→ materials.csv）。納品以外は null。</summary>
        public string? MaterialId { get; set; }

        /// <summary>納品する個数。</summary>
        public int Count { get; set; }

        /// <summary>派遣の条件の能力（"STR" など、→ AdventurerStatAccessor.AllStatNames）。派遣以外は null。</summary>
        public string? StatName { get; set; }

        /// <summary>派遣の条件の値（この値以上の素の能力値。装備の補正は含めない）。</summary>
        public int StatThreshold { get; set; }

        /// <summary>掲示された週。</summary>
        public int OfferedWeek { get; set; }

        /// <summary>期限の週（この週の決算までに達成すればよい）。期限は掲示の週から数える（受けるのを遅らせても延びない）。</summary>
        public int DeadlineWeek { get; set; }

        /// <summary>報酬のゴールド（掲示の時点で決める）。</summary>
        public int RewardGold { get; set; }

        /// <summary>報酬の遺物の出土階層（希少度のロールに使う。掲示の時点の攻略の最前線の階層）。</summary>
        public int RewardRelicFloor { get; set; } = 1;

        /// <summary>受けたか（false＝掲示中）。</summary>
        public bool Accepted { get; set; }

        /// <summary>依頼文（掲示の時点で文例に対象を差し込んで作る）。</summary>
        public string Text { get; set; } = "";

        /// <summary>期限までの残り週数（今週を含む。今週が期限の週なら1、過ぎていれば0以下）。</summary>
        public int WeeksLeft(int currentWeek) => DeadlineWeek - currentWeek + 1;
    }
}
