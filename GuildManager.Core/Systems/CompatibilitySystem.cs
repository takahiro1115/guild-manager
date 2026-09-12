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
    /// - 同パーティでクエスト達成→小さく上昇、苦戦敗退・戦線崩壊→やや下降、
    ///   戦死が発生した場合は居合わせた生存者同士が大きく下降する。
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
        /// クエスト解決結果（達成/失敗）を、同パーティで出撃した全ペアの相性に反映する。
        /// 達成時のみ「容姿秀麗」保持者が関与するペアの上昇量に倍率がかかる
        /// （失敗時は下降のみのため倍率を適用しない）。
        /// </summary>
        public void ApplyQuestOutcome(GameState state, Party party, bool achieved)
        {
            ForEachPair(party.Members, (a, b) =>
            {
                int delta = achieved
                    ? (int)Math.Round(CompatibilityBalance.AchievementGain * GetBeautifulMultiplier(a, b))
                    : -CompatibilityBalance.FailureLoss;

                AdjustCompatibility(state, a.Id, b.Id, delta);
            });
        }

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
