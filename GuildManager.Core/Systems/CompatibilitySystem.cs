using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 相性（Compatibility）の管理。仕様書 03 §5.3.1 参照。
    ///
    /// - 冒険者2人1組ごとに0〜100の相性値を持つ（未登録のペアは初期値50＝中立）。
    /// - 同じ部隊で大迷宮の任務を達成して生還→上昇（ボス撃破+3、潜行・調査・採取+1。2026年9月再配線）、
    ///   強制除籍（戦死扱い）が発生した場合は居合わせた生存者同士が大きく下降する。
    /// - 「容姿秀麗」特性を持つ者が関与するペアは、上昇量のみ倍率がかかる
    ///   （下降には影響しない、→ TraitEffectType.CompatibilityGainMultiplier）。
    /// - 30未満で「険悪」と判定し、SatisfactionSystem側の満足度ペナルティに使われる。
    /// </summary>
    public class CompatibilitySystem
    {
        private readonly IRng _rng;

        public CompatibilitySystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>常に (小さいGuid, 大きいGuid) の順になるよう正規化したキーを返す。</summary>
        public static (Guid, Guid) NormalizeKey(Guid a, Guid b) =>
            a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        /// <summary>ペアの相性値を取得する。未登録なら初期値50（中立）を返す。</summary>
        public static int GetCompatibility(GameState state, Guid idA, Guid idB) =>
            state.Compatibility.TryGetValue(NormalizeKey(idA, idB), out var value)
                ? value
                : CompatibilityBalance.InitialValue;

        /// <summary>相性値が険悪判定（30未満）かどうか。</summary>
        public static bool IsHostile(GameState state, Guid idA, Guid idB) =>
            GetCompatibility(state, idA, idB) < CompatibilityBalance.HostileThreshold;

        /// <summary>
        /// 大迷宮の任務結果を、同じ部隊で生還した全ペアの相性に反映する（2026年9月：旧 ApplyQuestOutcome を
        /// 大迷宮へ再配線。→ DungeonExpeditionSystem の週次解決から呼ぶ）。
        /// 任務を達成した時だけ、任務種別ごとの上昇量（→ GetExpeditionGain：ボス撃破+3、潜行・調査・採取+1）を
        /// 加算する（上限100）。「容姿秀麗」保持者が関与するペアは上昇量に倍率がかかる。
        /// 達成できなかった場合（撤退・潰走・進軍なし・素材なし）は何もしない。
        /// 強制除籍でロースターを離れた者は対象外（生還者同士のペアのみ）。
        /// </summary>
        /// <returns>規定の上昇量（倍率適用前）。上昇が起きなかった（未達成・生還者が2人未満）場合は0。</returns>
        public int ApplyExpeditionOutcome(GameState state, Party party, DungeonMissionType missionType, bool isSuccess)
        {
            if (!isSuccess)
                return 0;

            var survivors = party.Members.Where(m => state.Adventurers.Contains(m)).ToList();
            if (survivors.Count < 2)
                return 0;

            int gain = GetExpeditionGain(missionType);
            ForEachPair(survivors, (a, b) =>
                AdjustCompatibility(state, a.Id, b.Id, (int)Math.Round(gain * GetBeautifulMultiplier(a, b))));
            return gain;
        }

        /// <summary>任務種別ごとの相性上昇量（→ compatibility_advisor.csv ExpeditionGain_*）。</summary>
        public static int GetExpeditionGain(DungeonMissionType missionType) => missionType switch
        {
            DungeonMissionType.BossAssault => CompatibilityBalance.ExpeditionGainBossVictory,
            DungeonMissionType.Survey => CompatibilityBalance.ExpeditionGainSurvey,
            DungeonMissionType.Gathering => CompatibilityBalance.ExpeditionGainGathering,
            _ => CompatibilityBalance.ExpeditionGainTraversal,
        };

        /// <summary>
        /// 戦死が発生した時の余波。同パーティの生存者同士の相性を大きく下降させ、
        /// 各生存者に確率で「トラウマ」特性を付与する（→ 03 §5.1・§5.3.1）。
        /// </summary>
        public void ApplyDeathAftermath(GameState state, Party party, Guid fallenId)
        {
            var survivors = party.Members.Where(m => m.Id != fallenId).ToList();

            ForEachPair(survivors, (a, b) =>
                AdjustCompatibility(state, a.Id, b.Id, -CompatibilityBalance.DeathWitnessLoss));

            foreach (var survivor in survivors)
            {
                if (_rng.NextInt(1, 100) <= CompatibilityBalance.TraumaGrantChancePercent)
                    survivor.TryAddTrait(TraitCatalog.TraumaId);
            }
        }

        /// <summary>
        /// 「容姿秀麗」の倍率を返す。a・bのいずれか（両方の場合は両方分）が
        /// CompatibilityGainMultiplier効果を持てば、その分だけ積として合成する
        /// （加算ではなく乗数のため、Adventurer.SumTraitEffectは使わない）。
        /// </summary>
        private static double GetBeautifulMultiplier(Adventurer a, Adventurer b)
        {
            double multiplier = 1.0;
            foreach (var person in new[] { a, b })
            {
                foreach (var traitId in person.TraitIds)
                {
                    var def = TraitCatalog.FindById(traitId);
                    if (def == null) continue;

                    foreach (var effect in def.Effects)
                        if (effect.EffectType == TraitEffectType.CompatibilityGainMultiplier)
                            multiplier *= effect.Value;
                }
            }
            return multiplier;
        }

        private static void AdjustCompatibility(GameState state, Guid idA, Guid idB, int delta)
        {
            var key = NormalizeKey(idA, idB);
            int current = state.Compatibility.TryGetValue(key, out var value) ? value : CompatibilityBalance.InitialValue;
            state.Compatibility[key] = Math.Clamp(current + delta, CompatibilityBalance.MinValue, CompatibilityBalance.MaxValue);
        }

        /// <summary>パーティメンバーの全2人組（重複無し）に対してactionを実行する。</summary>
        private static void ForEachPair(IReadOnlyList<Adventurer> members, Action<Adventurer, Adventurer> action)
        {
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                    action(members[i], members[j]);
        }
    }
}
