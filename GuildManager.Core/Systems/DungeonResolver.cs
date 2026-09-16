using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 階層ボス討伐の解決エンジン（→ ダンジョン攻略システム）。
    ///
    /// 直接指揮UIは作らない：編成と携行品を決めて送り出せば、あとは完全自動で判定し
    /// 構造化された結果（→ DungeonResult）を返す（既存のQuestResolverと同じ方針）。
    ///
    /// 勝敗を分けるのは「事前にどれだけ調べ、どれだけ対策を積んだか」：
    ///  - ギミック対策：各ギミックについて「対策ロール／対策ステータス合算／対策アイテム」の
    ///    いずれかを満たしていれば対策成立（OR条件。唯一解のパズルにしないため）。
    ///  - 未対策ペナルティ：未対策1件につき危険度に比例して被ダメージ倍率が積み上がる。
    ///    即死級（InstantKill）を未対策で踏むとHPを全損する＝無調査での突撃は壊滅する。
    ///  - 完全解析ボーナス：解析率1.0（→ ScoutingResolver）で与ダメージが上乗せされる。
    ///
    /// 討伐クエスト（QuestResolver）との違い：こちらはHP下限0で、0に到達した冒険者は
    /// 強制除籍（恒久ロスト）になる（→ DungeonResult.ForceRetiredAdventurerIds）。
    /// </summary>
    public class DungeonResolver
    {
        private readonly IRng _rng;

        public DungeonResolver(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 部隊を階層ボスへ差し向け、討伐を解決する。撃破した場合は boss.IsDefeated を立て、
        /// boss.CurrentHp を0にする（撤退時はボスのHPを元に戻し、次回は仕切り直しとする）。
        /// </summary>
        public DungeonResult Resolve(Party party, FloorBoss boss)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティはダンジョンに出せません。");

            var result = new DungeonResult();

            // ---- ギミック対策の判定 ----
            foreach (var gimmick in boss.Gimmicks)
            {
                if (IsCountered(gimmick, party))
                    result.CounteredGimmicks.Add(gimmick.Type);
                else
                    result.UncounteredGimmicks.Add(gimmick.Type);
            }

            result.DamageMultiplier = CalculateDamageMultiplier(boss, result);

            // ---- 火力判定（ボスのHPを削り切れるか） ----
            double partyPower = party.Members.Sum(m => QuestScoreCalculator.MemberScore(m, QuestType.Subjugation));

            // 完全解析なら弱点を突ける（→ ScoutingResolver で解析率を1.0まで上げた場合）。
            result.FullIntelBonusApplied = ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete;
            if (result.FullIntelBonusApplied)
                partyPower *= 1.0 + DungeonBalance.FullIntelDamageBonus;

            result.PartyPower = partyPower;
            result.RequiredPower = boss.Floor * DungeonBalance.PartyPowerRequirementPerFloor;
            result.Outcome = partyPower >= result.RequiredPower ? DungeonOutcome.Victory : DungeonOutcome.Retreat;

            if (result.Outcome == DungeonOutcome.Victory)
            {
                boss.CurrentHp = 0;
                boss.IsDefeated = true;
            }

            ApplyHpLoss(result, party);

            // 携行アイテムは使い切り（→ QuestResolver.Resolve と同じ扱い）。
            party.ConsumableItemIds.Clear();

            return result;
        }

        /// <summary>
        /// ギミック1件が対策できているか。3つの対策口（職業・ステータス合算・携行アイテム）の
        /// いずれかを満たせば対策成立（OR条件）。対策口が1つも設定されていないギミックは
        /// 「対策不能」として常に未対策になる（ボス定義側の設定漏れを黙って握り潰さないため）。
        /// public static にしてあるのは編成画面のプレビューとテストから直接使えるようにするため。
        /// </summary>
        public static bool IsCountered(BossGimmick gimmick, Party party)
        {
            if (gimmick.RequiredCounterRole.HasValue &&
                party.Members.Any(m => m.JobClass == gimmick.RequiredCounterRole.Value))
                return true;

            if (!string.IsNullOrEmpty(gimmick.RequiredCounterStat))
            {
                double sum = party.Members.Sum(m => m.GetEffectiveStat(gimmick.RequiredCounterStat!));
                if (sum >= gimmick.RequiredCounterStatThreshold)
                    return true;
            }

            if (!string.IsNullOrEmpty(gimmick.RequiredItemId) &&
                party.ConsumableItemIds.Contains(gimmick.RequiredItemId!))
                return true;

            return false;
        }

        /// <summary>
        /// 未対策ギミックによる被ダメージ倍率。未対策1件につき危険度に比例して積み上がる。
        /// すべて対策済みなら1.0（＝通常の討伐並みの損害で済む）。
        /// </summary>
        private static double CalculateDamageMultiplier(FloorBoss boss, DungeonResult result)
        {
            double multiplier = 1.0;

            foreach (var gimmick in boss.Gimmicks)
            {
                if (result.CounteredGimmicks.Contains(gimmick.Type))
                    continue;

                multiplier += gimmick.DangerLevel * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel;
            }

            return multiplier;
        }

        /// <summary>
        /// HP消費の適用。撃破できたかどうかでベースのレンジが変わり、未対策ギミックの
        /// 倍率が乗る。即死級を未対策で踏んだ場合はレンジを無視して全損させる。
        ///
        /// HP下限は0：0に到達した冒険者は強制除籍（恒久ロスト）となる
        /// （→ DungeonResult.ForceRetiredAdventurerIds。世界観上は「秘薬で一命を取り留めたが
        /// アルベールが登録を抹消した」という扱い）。
        /// </summary>
        private void ApplyHpLoss(DungeonResult result, Party party)
        {
            bool instantKillTriggered = result.UncounteredGimmicks.Contains(BossGimmickType.InstantKill);

            var (minPct, maxPct) = result.Outcome == DungeonOutcome.Victory
                ? (DungeonBalance.BaseHpLossPctMin, DungeonBalance.BaseHpLossPctMax)
                : (DungeonBalance.RetreatHpLossPctMin, DungeonBalance.RetreatHpLossPctMax);

            foreach (var member in party.Members)
            {
                int lossPct;
                if (instantKillTriggered)
                {
                    // 即死級ギミックは対策の有無が生死を分ける（倍率ではなく固定で全損させる）。
                    lossPct = DungeonBalance.InstantKillUncounteredHpLossPct;
                }
                else
                {
                    lossPct = _rng.NextInt(minPct, maxPct);
                    lossPct = (int)Math.Clamp(Math.Round(lossPct * result.DamageMultiplier), 0, 100);
                }

                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(0, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;

                if (newHp == 0)
                    result.ForceRetiredAdventurerIds.Add(member.Id);
            }
        }
    }
}
