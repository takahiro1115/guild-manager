using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 冒険者データモデル。仕様書 03 §2 参照。
    /// 性格・相性はまだ実装しない（→ docs/06_タスクリスト.md Phase 3 で追加予定）。
    /// </summary>
    public class Adventurer
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "名無し";
        public int Age { get; set; } = 18;
        public JobClass JobClass { get; set; }

        /// <summary>
        /// 性別。仕様書 03 §2.4参照（v1.8改訂で新設）。現時点ではステータス・成長・戦闘の
        /// いずれにも影響を与えない。氏名生成（NameGenerator）と、将来の立ち絵システム
        /// 連携（次フェーズ、post-MVP）のための土台として先行して追加したフィールドである。
        /// </summary>
        public Gender Gender { get; set; }

        /// <summary>
        /// 配置（前衛/後衛）。仕様書 03 §4.2 参照。冒険者個人に紐づく永続状態で、
        /// クエストをまたいで保持される。生成時に PlacementRules.GetDefault(JobClass) で
        /// 職業に応じた初期値を設定する想定（→ SampleData・RecruitmentSystem）。
        /// 配置は職業で固定されない。どの職業でも自由に前衛/後衛を選べる
        /// （ユーザー決定：「魔法使いや僧侶も前衛になることができる。職業で固定になることはない」）。
        /// 役割から外れた配置のペナルティは個人CP補正（→ Balance/PlacementBalance）で表現する。
        /// </summary>
        public Placement Placement { get; set; } = Placement.Front;

        /// <summary>
        /// 配置を変更する。職業による制約は無いため常に成功するが、Party.TryAdd等の
        /// 既存の「Try」系メソッドと同じ形（bool戻り値）に揃えてある。
        /// </summary>
        public bool TrySetPlacement(Placement placement)
        {
            Placement = placement;
            return true;
        }

        // ---- 能力値（実効値 1〜100）。仕様書 03 §2.2 ----
        public int STR { get; set; }
        public int AGI { get; set; }
        public int VIT { get; set; }
        public int MND { get; set; }
        public int DEX { get; set; }
        public int LDR { get; set; }

        /// <summary>
        /// v1.2改訂：予約フィールドから活性化し、個人CP・成長ロール対象・総合PA算出に参加する
        /// （→ 03 §2.2・§4.2・§10）。
        /// </summary>
        public int INT { get; set; }

        // ---- 潜在能力 PA（各ステータスの成長上限。1〜100）。仕様書 03 §2.2 ----
        // 実効値はデフォルトでは0から始まるため、PAのデフォルトは範囲の最大値にしておき、
        // 明示的に潜在能力を絞らない限り既存の挙動（成長上限なし相当）を壊さないようにする。
        public int PA_STR { get; set; } = 100;
        public int PA_AGI { get; set; } = 100;
        public int PA_VIT { get; set; } = 100;
        public int PA_MND { get; set; } = 100;
        public int PA_DEX { get; set; } = 100;
        public int PA_LDR { get; set; } = 100;
        public int PA_INT { get; set; } = 100;

        /// <summary>総合PA＝7つのPAの平均（v1.2改訂：INT活性化に伴い6値→7値平均。採用試験・スカウト評価用。仕様書 03 §2.2）。</summary>
        public double TotalPA => (PA_STR + PA_AGI + PA_VIT + PA_MND + PA_DEX + PA_LDR + PA_INT) / 7.0;

        // ---- 生涯ピーク値（顧問効果算出の基準。仕様書 03 §2.2新規・§7）。 ----
        // 衰微処理・古傷（Trait）による実効値低下では変更されない。成長ロール（経路1・経路2）で
        // 実効値が上昇した時のみ、GrowthSystemが対応するPeakフィールドを更新する。
        //
        // 各プロパティのgetterは Max(記録済みのピーク, 現在の実効値) を返すことで、
        // 「現在値を下回るピークは有り得ない」という不変条件を自動的に保証する
        // （＝生成直後で一度も成長していない冒険者は、Peak==現在の実効値になる。
        // SampleData・RecruitmentSystemで別途Peakを初期化する必要はない）。
        private int _peakSTR, _peakVIT, _peakAGI, _peakDEX, _peakMND, _peakINT, _peakLDR;
        public int PeakSTR { get => Math.Max(_peakSTR, STR); set => _peakSTR = value; }
        public int PeakVIT { get => Math.Max(_peakVIT, VIT); set => _peakVIT = value; }
        public int PeakAGI { get => Math.Max(_peakAGI, AGI); set => _peakAGI = value; }
        public int PeakDEX { get => Math.Max(_peakDEX, DEX); set => _peakDEX = value; }
        public int PeakMND { get => Math.Max(_peakMND, MND); set => _peakMND = value; }
        public int PeakINT { get => Math.Max(_peakINT, INT); set => _peakINT = value; }
        public int PeakLDR { get => Math.Max(_peakLDR, LDR); set => _peakLDR = value; }

        // ---- 特性（Trait）。仕様書 03 §5.3・§4.3 参照。 ----

        /// <summary>この冒険者が保持している特性のIdリスト。重複するIdは持てない。</summary>
        public List<string> TraitIds { get; set; } = new();

        public bool HasTrait(string traitId) => TraitIds.Contains(traitId);

        /// <summary>特性を付与する。既に持っていれば何もせずfalseを返す（重複禁止）。</summary>
        public bool TryAddTrait(string traitId)
        {
            if (TraitIds.Contains(traitId)) return false;
            TraitIds.Add(traitId);
            return true;
        }

        /// <summary>
        /// 特性による効果（StatPercentReduction）を反映した実効値を返す
        /// （→ 03 §4.3・§5.3）。個人CP計算・最大HP計算の両方から使う共通ヘルパー。
        /// </summary>
        public double GetEffectiveStat(string statName)
        {
            int baseValue = statName switch
            {
                "STR" => STR,
                "VIT" => VIT,
                "AGI" => AGI,
                "DEX" => DEX,
                "MND" => MND,
                "INT" => INT,
                "LDR" => LDR,
                _ => throw new ArgumentException($"未知のステータス: {statName}")
            };

            double totalReduction = SumTraitEffect(TraitEffectType.StatPercentReduction, statName);

            // 減少しすぎて0以下にならないよう下限をクランプ（暫定：最大80%減まで）
            double multiplier = Math.Max(1.0 + totalReduction, 0.2);
            return baseValue * multiplier;
        }

        /// <summary>
        /// 保有する特性のうち、指定した効果種別（・対象ステータス）に該当する効果量の合計を返す
        /// （→ 03 §5.3）。GetEffectiveStat・QuestResolverの索敵/致死判定補正が共通で使う。
        /// targetStatをnullにすると対象ステータスを問わず合算する（SurvivalThresholdModifier等、
        /// TargetStatを使わない効果種別向け）。
        /// 乗数として使う効果種別（CompatibilityGainMultiplier）は加算ではなく積で合成すべきため、
        /// このヘルパーの対象外（呼び出し側で個別に扱う）。
        /// </summary>
        public double SumTraitEffect(TraitEffectType effectType, string? targetStat = null)
        {
            double total = 0;
            foreach (var traitId in TraitIds)
            {
                var def = TraitCatalog.FindById(traitId);
                if (def == null) continue;

                foreach (var effect in def.Effects)
                {
                    if (effect.EffectType == effectType && (targetStat == null || effect.TargetStat == targetStat))
                        total += effect.Value;
                }
            }
            return total;
        }

        /// <summary>年齢帯（仕様書 03 §3.0 の定義表）。表の範囲外は近い側の帯に丸める。</summary>
        public AgeBand AgeBand =>
            Age <= 21 ? AgeBand.GrowthPeriod :
            Age <= 27 ? AgeBand.PrimePeriod :
            Age <= 34 ? AgeBand.MaturePeriod :
            AgeBand.LimitPeriod;

        /// <summary>40歳年度末で強制引退したか（仕様書 03 §3.7）。→ AgingSystem が設定する。
        /// 引退した冒険者は GameState.Adventurers から GameState.RetiredAdventurers へ移される。
        /// このフラグ自体は移動後も参照できるよう残す（防御的なガード・監査用）。</summary>
        public bool IsRetired { get; set; } = false;

        // ---- 引退記録（仕様書 03 §3.7つづき）。§7顧問制度が未実装の間の「引退済み・顧問候補」データ。 ----

        /// <summary>引退時点の年齢。強制引退は常に40だが、将来の任意引退等に備えて記録する。</summary>
        public int? RetiredAtAge { get; set; }

        /// <summary>引退した週番号（GameState.WeekNumber）。§7実装時の参考情報として保持。</summary>
        public int? RetiredAtWeek { get; set; }

        /// <summary>退職金（週給×12週。仕様書 03 §7）が支給済みか。</summary>
        public bool SeverancePaid { get; set; } = false;

        /// <summary>戦死した週番号（GameState.WeekNumber）。null＝戦死していない（仕様書 03 §4.3.1）。
        /// 戦死者記録として保持し、GameState.Adventurers から GameState.FallenAdventurers へ移される。</summary>
        public int? FellAtWeek { get; set; }

        // ---- 動的・コンディション属性。仕様書 03 §2.3 ----

        /// <summary>
        /// 最大HP = 実効VIT×2 + 50（仕様書 03 §2.3）＋装備の最大HP加算（→ §4.2.2）。
        /// 特性（例：古傷）による実効値低下、防具・アクセサリーの固定加算の両方を反映する。
        /// </summary>
        public int MaxHP => (int)(GetEffectiveStat("VIT") * 2) + 50 + GetEquipmentBonus(EquipmentEffectType.MaxHpBonus);

        // ---- 装備（Weapon/Armor/Accessory1/Accessory2）。仕様書 03 §4.2.2 参照。 ----

        /// <summary>装備中の武器のId。null＝未装備。</summary>
        public string? EquippedWeaponId { get; set; }

        /// <summary>装備中の防具のId。null＝未装備。</summary>
        public string? EquippedArmorId { get; set; }

        /// <summary>装備中のアクセサリー1のId。null＝未装備。</summary>
        public string? EquippedAccessory1Id { get; set; }

        /// <summary>装備中のアクセサリー2のId。null＝未装備。</summary>
        public string? EquippedAccessory2Id { get; set; }

        /// <summary>
        /// 指定したスロットの現在の装備Idを返す（スロット横断の汎用アクセサ。
        /// EquipmentSystem・UI側の装備状況表示の両方から使う）。
        /// </summary>
        public string? GetEquippedId(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Weapon => EquippedWeaponId,
            EquipmentSlot.Armor => EquippedArmorId,
            EquipmentSlot.Accessory1 => EquippedAccessory1Id,
            EquipmentSlot.Accessory2 => EquippedAccessory2Id,
            _ => null,
        };

        /// <summary>指定したスロットへ装備Idを設定する（スロット横断の汎用アクセサ。EquipmentSystem用）。</summary>
        public void SetEquippedId(EquipmentSlot slot, string? itemId)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon: EquippedWeaponId = itemId; break;
                case EquipmentSlot.Armor: EquippedArmorId = itemId; break;
                case EquipmentSlot.Accessory1: EquippedAccessory1Id = itemId; break;
                case EquipmentSlot.Accessory2: EquippedAccessory2Id = itemId; break;
            }
        }

        /// <summary>
        /// 装備中の4枠（武器・防具・アクセサリー1・アクセサリー2）のうち、指定した効果種別を
        /// 持つものの効果量合計を返す（→ 03 §4.2.2）。個人CP計算・最大HP計算の両方から使う。
        /// </summary>
        public int GetEquipmentBonus(EquipmentEffectType effectType)
        {
            int total = 0;
            foreach (var slot in new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 })
            {
                var item = ItemCatalog.FindById(GetEquippedId(slot));
                if (item != null && item.EffectType == effectType)
                    total += item.EffectValue;
            }
            return total;
        }

        public int CurrentHP { get; set; }
        public int Satisfaction { get; set; } = 70;
        public InjurySeverity Injury { get; set; } = InjurySeverity.None;

        // ---- 満足度・契約交渉（仕様書 03 §5.1・§5.2）。→ SatisfactionSystem が更新する。 ----

        /// <summary>直近出撃してからの連続週数（出撃した週に0へリセット）。出場機会ペナルティ判定に使う（§5.1）。</summary>
        public int WeeksSinceLastDeployment { get; set; } = 0;

        /// <summary>満足度20未満で立つ交渉警告フラグ（§5.2「昇給要求」「移籍検討」）。</summary>
        public bool NeedsNegotiation { get; set; } = false;

        /// <summary>交渉警告が立ってから経過した週数。2週を超えて未対応だと契約解除される。</summary>
        public int NegotiationWeeksElapsed { get; set; } = 0;

        /// <summary>負傷が治るまでの残り週数。0ならNoneに戻る（→ 03 §3.6 負傷回復処理）。</summary>
        public int InjuryWeeksRemaining { get; set; } = 0;

        public int WeeklyWage { get; set; }

        /// <summary>
        /// 複数週クエストに派遣中かどうか（仕様書 03 §4.0.1）。拘束期間が満了し
        /// QuestDispatchSystemが解決するまでtrueのまま。派遣中は他クエストへの
        /// 再編成・訓練場への配置ができない（IsAvailableに反映）。中断（呼び戻し）は実装しない。
        /// </summary>
        public bool IsDispatched { get; set; } = false;

        /// <summary>出撃可能かどうか（重傷・引退済み・派遣中なら不可）。疲労（Fatigue）は廃止済み（→ 03 §3.5改）。</summary>
        public bool IsAvailable => Injury != InjurySeverity.Severe && !IsRetired && !IsDispatched;
    }
}
