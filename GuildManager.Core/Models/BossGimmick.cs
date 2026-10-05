using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 階層ボスが持つギミック1件（→ ダンジョン攻略システム、03 §4.5.4）。
    ///
    /// 2026年10月・§0.68：対策は「どれか1つを満たせば対策成立」の離散判定から、部隊の**備え**（0〜1）で段階的に効く形へ改めた
    /// （→ Systems.DungeonResolver.Readiness）。備え＝対策の職業が同行していれば GimmickRoleReadiness（0.5）＋
    /// 対策の能力の部隊合計÷RequiredCounterStatThreshold、上限1。伝説級の装備（§0.45）があれば1。
    /// 携行アイテムの対策口（旧 RequiredItemId）は撤去した。
    ///
    /// 定義は SampleData.CreateBossGimmicks が所属フィールド・階層から決めて作り、旧セーブでも読み込み時に作り直す
    /// （→ GameState.FromSaveData）。旧セーブの RequiredCounterRole・RequiredItemId のキーは読み捨てる。
    /// </summary>
    public class BossGimmick
    {
        public BossGimmickType Type { get; set; }

        /// <summary>
        /// このボスで効く対策の職業（ギミックの候補2つのうち1つか両方、→ BossGimmickInfo.RoleCandidates）。
        /// どれか1人でも同行していれば備えに GimmickRoleReadiness が乗る。解析段階「対策情報」以上で画面に出る。
        /// </summary>
        public List<JobClass> CounterRoles { get; set; } = new();

        /// <summary>対策の能力名（"MND"等。→ BossGimmickInfo.CounterStat）。null＝能力による対策は無い。</summary>
        public string? RequiredCounterStat { get; set; }

        /// <summary>RequiredCounterStat の部隊合計がこの値に届けば、能力だけで備えが1（万全）になる。</summary>
        public double RequiredCounterStatThreshold { get; set; }

        /// <summary>
        /// 危険度（1〜5）。損耗が増える型（猛毒・飛行・群れ）の被ダメージの加算に使う
        /// （→ DungeonBalance.UncounteredDamageMultiplierPerDangerLevel）。
        /// </summary>
        public int DangerLevel { get; set; } = 1;
    }
}
