using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 訓練施設配置の運用ルール。仕様書 03 §3.1〜3.4（経路2・運用ルール確定）・
    /// §3.5改（訓練週のHP処理）・§6（v1.3改訂：訓練場・道場の4分割）参照。
    ///
    /// v1.3改訂：旧・単一の訓練場・道場（TrainingGround）を、戦士訓練所（WarriorHall）・
    /// 教会（Church）・魔法研究所（MageLab）・斥候所（ScoutPost）の4施設に分割した。
    /// GameState.TrainingAssignments が「どの施設に配置されているか」まで持つ
    /// Dictionary&lt;Guid, FacilityType&gt; になったことに伴い、枠数判定・配置操作を
    /// 施設ごとに独立させている。
    ///
    /// - 枠数：GetSlotCapacity(state, facility)。各訓練施設のLvに連動する
    ///   （→ FacilityBalance.GetTrainingSlotCapacity。Lv1＝1名は分割前と同じ値）。
    ///   満杯時の追加配置は Party.TryAdd と同様に失敗（false）で表現する。
    /// - 費用：配置されている限り毎週発生する都度払い（宿舎枠のような無料保有ではない）。
    ///   週給引落しと同じタイミングで ProcessWeeklyTraining を呼ぶ想定。
    /// - HP：今週出撃していない配置者のみ、CurrentHPを微減させる（下限1）。
    ///   InjurySeverityやフェーズ3の致死判定（§4.3）には一切接続しない
    ///   （訓練は「回復しない・地味に削れる」だけの低リスクな経路）。
    ///   出撃した週・単純待機の週とは排他（それぞれ戦闘のHP処理・RestRecoverySystemが担当）。
    /// </summary>
    public class TrainingSystem
    {
        /// <summary>
        /// 既定の乱数シード（parameterless コンストラクタ用）。他のSystemクラス（GrowthSystem等）が
        /// 構成ルート側（MainDashboard.cs）で固定シードのSeededRngを注入されているのと同じ考え方で、
        /// 呼び出し側がIRngを気にしなくても決定論的に動く既定値を用意しておく
        /// （→ Rng.IRng「決定性・再現性」の方針。TickCount等の非決定的な既定値は使わない）。
        /// </summary>
        private const int DefaultRngSeed = 831;

        private readonly IRng _rng;

        /// <summary>
        /// 既定コンストラクタ。特性伝授ロール（→ ProcessWeeklyTraitTransmission）にのみ乱数を使う。
        /// 既存の呼び出し側（TryAssign・ProcessWeeklyTraining等）は乱数を一切使わないため、
        /// 挙動に影響しない。
        /// </summary>
        public TrainingSystem() : this(new SeededRng(DefaultRngSeed)) { }

        public TrainingSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>指定した訓練施設の現在Lvに連動する枠数上限。</summary>
        public int GetSlotCapacity(GameState state, FacilityType facility) =>
            FacilityBalance.GetTrainingSlotCapacity(state.GetFacilityLevel(facility));

        /// <summary>
        /// 冒険者を指定した訓練施設に配置する。既にその施設に配置済みなら何もせず成功扱い。
        /// 既に「別の」施設に配置済みの場合は、一旦解除してから新しい施設に付け替える
        /// （1人が同時に複数の訓練施設へ重複配置されることは無い）。
        /// 対象施設の枠が埋まっていれば配置せず false を返す（Party.TryAdd のパターンに倣う）。
        /// </summary>
        public bool TryAssign(GameState state, Guid adventurerId, FacilityType facility)
        {
            if (state.TrainingAssignments.TryGetValue(adventurerId, out var current))
            {
                if (current == facility)
                    return true; // 既に同じ施設に配置済み

                Unassign(state, adventurerId); // 別施設への付け替え：まず現在の枠を解放する
            }

            if (CountAssigned(state, facility) >= GetSlotCapacity(state, facility))
                return false;

            state.TrainingAssignments[adventurerId] = facility;
            return true;
        }

        /// <summary>訓練施設への配置を解除する（配置先の施設を問わない）。</summary>
        public void Unassign(GameState state, Guid adventurerId) =>
            state.TrainingAssignments.Remove(adventurerId);

        /// <summary>指定した訓練施設に現在配置されている人数。</summary>
        public int CountAssigned(GameState state, FacilityType facility) =>
            state.TrainingAssignments.Values.Count(f => f == facility);

        /// <summary>
        /// 週次決算処理。訓練施設に配置されている冒険者から週次費用を徴収し、
        /// 今週出撃していない配置者のCurrentHPを微減させる（下限1・致死判定と非接続）。
        /// どの施設に配置されているかは対象外（全施設共通の処理。→ 03 §6「訓練週のHP微減・
        /// 回復なし・下限1は全施設共通で維持する」）。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="dispatchedAdventurerIds">今週出撃したパーティのメンバーID（出撃なしの週は空集合）。</param>
        public void ProcessWeeklyTraining(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            if (state.TrainingAssignments.Count == 0)
                return;

            foreach (var adventurer in state.Adventurers)
            {
                if (!state.TrainingAssignments.ContainsKey(adventurer.Id))
                    continue;

                // 配置されている限り、費用は都度払い（出撃の有無に関わらず発生する）。
                state.Gold -= TrainingBalance.WeeklyCost;

                if (dispatchedAdventurerIds.Contains(adventurer.Id))
                    continue; // 今週は出撃扱い。訓練固有のHP処理は行わない（§3.5改：排他）。

                adventurer.CurrentHP = Math.Max(TrainingBalance.MinHp, adventurer.CurrentHP - TrainingBalance.WeeklyHpCost);
            }
        }

        // ---- 年齢帯（15〜27歳）のみが伝授ロールの対象（→ 特性伝授刷新仕様）。 ----
        private const int TraitTransmissionMinAge = 15;
        private const int TraitTransmissionMaxAge = 27;

        /// <summary>
        /// 教官からの週次特性伝授ロール（奥義継承。→ 03 §7.1、2026年9月・§0.34で教官深化 Step 1 として再調整）。
        /// 訓練施設に配置され、かつ配置先施設に教官（引退済み冒険者。→ GameState.
        /// AssignedTrainers）が就いている15〜27歳の冒険者ごとに、教官の特性のうち
        /// 障害特性でなく（→ TraitDefinition.IsCurseOrInjury）・伝授可能（→ TraitDefinition.IsTransmittable）で・
        /// 生徒が未所持のものを伝授候補とし、候補があれば1回ロールする（ポロッと覚える形式：狙って伝授先を選べない）。
        ///
        /// 伝授確率＝TrainingBalance.TraitInheritanceBaseChance（5%）＋教官が師匠肌なら MentorTraitInheritanceBonus（+15%）。
        /// 候補が複数あれば教官の特性枠の並び順で最初の1つ（1週1件まで）。
        /// 特性スロットが満杯（→ Adventurer.MaxTraitCount）の生徒はロールしない（伝授は通常特性の侵食をしない。
        /// 手動忘却で枠を空けてもらう、→ 03 §5.3.2）。
        /// </summary>
        public List<TraitTransmissionEvent> ProcessWeeklyTraitTransmission(GameState state)
        {
            var events = new List<TraitTransmissionEvent>();
            if (state.TrainingAssignments.Count == 0 || state.AssignedTrainers.Count == 0)
                return events;

            foreach (var adventurer in state.Adventurers)
            {
                if (adventurer.Age < TraitTransmissionMinAge || adventurer.Age > TraitTransmissionMaxAge)
                    continue;
                if (adventurer.TraitIds.Count >= Adventurer.MaxTraitCount)
                    continue; // 特性スロット満杯

                if (!state.TrainingAssignments.TryGetValue(adventurer.Id, out var facility))
                    continue; // 訓練施設未配置

                if (!state.AssignedTrainers.TryGetValue(facility, out var trainerId) || trainerId == null)
                    continue; // 教官未配置

                var trainer = state.RetiredAdventurers.FirstOrDefault(a => a.Id == trainerId.Value);
                if (trainer == null)
                    continue;

                string? teachableTraitId = GetTransmittableTraitIds(trainer)
                    .FirstOrDefault(id => !adventurer.HasTrait(id));

                if (teachableTraitId == null)
                    continue; // 教官が伝授可能な特性を（生徒が未所持な形で）持っていない

                int roll = _rng.NextInt(1, 100);
                if (roll > (int)Math.Round(GetInheritanceChance(trainer) * 100))
                    continue;

                if (adventurer.TryAddTrait(teachableTraitId))
                    events.Add(new TraitTransmissionEvent(adventurer, trainer, teachableTraitId));
            }

            return events;
        }

        /// <summary>
        /// 教官が伝授しうる特性のId（教官の特性枠の並び順）：障害特性でなく（IsCurseOrInjury == false）、
        /// 伝授可能（IsTransmittable）なもの。生徒ごとの「未所持」の絞り込みは呼び出し側で行う。
        /// 伝授ロール（→ ProcessWeeklyTraitTransmission）と施設画面の「伝授可能：…」表示（→ FacilityPanel、§0.35）が共通で使う。
        /// </summary>
        public static IReadOnlyList<string> GetTransmittableTraitIds(Adventurer trainer) =>
            trainer.TraitIds
                .Select(TraitCatalog.FindById)
                .Where(def => def != null && !def.IsCurseOrInjury && def.IsTransmittable)
                .Select(def => def!.Id)
                .ToList();

        /// <summary>
        /// 教官1人あたりの伝授確率（0〜1）＝基礎確率＋師匠肌ボーナス（→ TrainingBalance、03 §7.1）。
        /// 2026年9月・§0.34：旧式にあった「教官の対象ステータス平均に比例した加算（最大+2%）」は廃止した。
        /// </summary>
        public static double GetInheritanceChance(Adventurer trainer) =>
            TrainingBalance.TraitInheritanceBaseChance
            + (trainer.HasTrait(TraitCatalog.MentorId) ? TrainingBalance.MentorTraitInheritanceBonus : 0);
    }
}
