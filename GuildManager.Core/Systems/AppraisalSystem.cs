using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 未鑑定遺物（レリック）の鑑定エンジン（→ 03 §4.7）。アルベールに鑑定費用を払って
    /// 未鑑定品の中身を確定させ、武具・素材・資金のいずれかへ変換する。
    ///
    /// 週次決算を介さず、ボタンを押した瞬間に完結する即時処理（→ ResearchSystem と同じ流儀。
    /// 乱数だけはこのクラスが持つ IRng から引くため、テストからは固定Rngで完全に再現できる）。
    ///
    /// 遺物の生成（どの希少度の遺物が出土するか）も、鑑定と同じ希少度体系を使うため
    /// このクラスの <see cref="CreateRelic"/> に集約してある（→ GatheringResolver・
    /// DungeonExpeditionSystem の双方から呼ばれる）。
    ///
    /// 指示書では Appraise の引数に RelicBalance・BalanceData を受け取る形が示されていたが、
    /// 本リポジトリのバランス値クラスは静的（static class、CSVをプロセス内で1度だけ読む）で
    /// あり、インスタンスとして渡せない。ResearchSystem が ResearchBalance を静的参照するのと
    /// 同じく、ここでも RelicBalance を直接参照する。
    /// </summary>
    public class AppraisalSystem
    {
        private readonly IRng _rng;

        public AppraisalSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 指定の未鑑定品を鑑定できるか（存在し、かつ所持金が鑑定費用以上か）。
        /// UI（InventoryPanel）の鑑定ボタンの活性判定に使う。
        /// </summary>
        public static bool CanAppraise(GameState state, string itemId)
        {
            var item = Find(state, itemId);
            return item != null && state.Gold >= item.AppraisalCost;
        }

        /// <summary>指定Idの未鑑定品。存在しなければnull。</summary>
        public static UnidentifiedItem? Find(GameState state, string itemId) =>
            state.UnidentifiedItems.FirstOrDefault(i => i.Id == itemId);

        /// <summary>
        /// 未鑑定品を1個鑑定する。鑑定費用を引き落とし、未鑑定リストから取り除き、抽選した中身を
        /// ギルド資産（保管庫・素材・所持金）へ反映して結果を返す。
        /// 対象が存在しない・資金不足なら何もせず null を返す（Try*系と同じ流儀）。
        /// </summary>
        public AppraisalResult? Appraise(GameState state, string itemId)
        {
            var item = Find(state, itemId);
            if (item == null || state.Gold < item.AppraisalCost)
                return null;

            state.Gold -= item.AppraisalCost;
            state.UnidentifiedItems.Remove(item);

            var result = RollContents(state, item);
            result.AppraisalCost = item.AppraisalCost;
            result.Rarity = item.Rarity;
            result.FlavorText = BuildFlavorText(result.Type, item.Rarity);
            // 鑑定完了でマスターの機嫌が上がる（→ 03 §8.1、2026年9月新設。週次決算を待たず即時）。
            result.MoodGained = MasterMoodSystem.ApplyAppraisal(state);
            return result;
        }

        /// <summary>
        /// 中身の抽選と、ギルド資産への反映。希少度ごとの比率（武具率／素材率／換金率）で種別を決め、
        /// 種別ごとの抽選プールから中身を引く（→ Balance.RelicBalance）。
        /// </summary>
        private AppraisalResult RollContents(GameState state, UnidentifiedItem item)
        {
            var profile = RelicBalance.Get(item.Rarity);
            int typeRoll = _rng.NextInt(1, 100);

            if (typeRoll <= profile.EquipmentRate)
                return AppraiseAsEquipment(state, item, profile);

            if (typeRoll <= profile.EquipmentRate + profile.MaterialRate)
            {
                // 出土地・出土階層で抽選対象になる素材（→ materials.csv）が1件も無い場合
                // （未定義のフィールドId等）は、遺物が無に帰さないよう換金へ振り替える。
                var result = TryAppraiseAsMaterial(state, item, profile);
                if (result != null)
                    return result;
            }

            return AppraiseAsGold(state, profile);
        }

        private AppraisalResult AppraiseAsEquipment(GameState state, UnidentifiedItem item, RelicBalance.RarityProfile profile)
        {
            var pool = profile.EquipmentPool;
            string catalogId = pool[_rng.NextInt(0, pool.Count - 1)];

            // プールのIdはRelicBalanceの読み込み時にItemCatalog照合済みのため、ここでnullにはならない。
            var definition = ItemCatalog.FindById(catalogId)!;
            // 希少度を個体へ刻む：売却額（→ 03 §4.8）と保管庫UIでの扱いが希少度で変わるため。
            var equipment = EquipmentItem.FromCatalog(definition, state.WeekNumber, BuildOriginText(item), item.Rarity);
            state.Armory.Add(equipment);

            return new AppraisalResult
            {
                ItemName = definition.Name,
                Type = AppraisalResultType.Equipment,
                ResultEquipment = equipment,
            };
        }

        /// <summary>素材の抽選。出土地・出土階層に対応する素材が1件も無ければnull（呼び出し側が換金へ振り替える）。</summary>
        private AppraisalResult? TryAppraiseAsMaterial(GameState state, UnidentifiedItem item, RelicBalance.RarityProfile profile)
        {
            var eligible = MaterialBalance.GetEligibleMaterials(item.OriginFieldId, item.OriginFloor);
            if (eligible.Count == 0)
                return null;

            var material = eligible[_rng.NextInt(0, eligible.Count - 1)];
            int count = _rng.NextInt(profile.MaterialCountMin, profile.MaterialCountMax);
            state.AddMaterial(material.Id, count);

            return new AppraisalResult
            {
                ItemName = material.Name,
                Type = AppraisalResultType.Material,
                ResultMaterialId = material.Id,
                ResultMaterialCount = count,
            };
        }

        private AppraisalResult AppraiseAsGold(GameState state, RelicBalance.RarityProfile profile)
        {
            int gold = _rng.NextInt(profile.GoldRewardMin, profile.GoldRewardMax);
            state.Gold += gold;

            return new AppraisalResult
            {
                ItemName = "古代硬貨",
                Type = AppraisalResultType.Gold,
                ResultGold = gold,
            };
        }

        // ==================== 遺物の生成（出土） ====================

        /// <summary>
        /// 未鑑定遺物を1個生成する（→ GatheringResolver・DungeonExpeditionSystem）。
        /// 希少度は「1〜100の乱数＋出土階層の深度ボーナス＋任務ボーナス」を閾値と比べて決まる
        /// （→ RelicBalance.RollRarity）。<paramref name="minimumRarity"/> を指定すると、
        /// ロール結果がそれ未満だった場合に引き上げる（階層ボス撃破の最低保証）。
        /// </summary>
        public static UnidentifiedItem CreateRelic(
            IRng rng, string fieldId, int floor, int rollBonus = 0, ItemRarity? minimumRarity = null)
        {
            var rarity = RelicBalance.RollRarity(rng.NextInt(1, 100), floor, rollBonus);
            if (minimumRarity.HasValue && rarity < minimumRarity.Value)
                rarity = minimumRarity.Value;

            return new UnidentifiedItem
            {
                Name = RelicNames.Roll(rarity, rng.NextInt(0, 99)),
                Rarity = rarity,
                OriginFieldId = fieldId,
                OriginFloor = Math.Max(1, floor),
                AppraisalCost = RelicBalance.GetAppraisalCost(rarity),
            };
        }

        // ==================== 演出用テキスト ====================

        /// <summary>「森 第12層の遺物を鑑定」のような入手経路の短い説明（→ EquipmentItem.AcquiredFrom）。</summary>
        private static string BuildOriginText(UnidentifiedItem item)
        {
            string field = string.IsNullOrEmpty(item.OriginFieldId) ? "大迷宮" : item.OriginFieldId;
            return $"{field} 第{item.OriginFloor}層の遺物を鑑定";
        }

        /// <summary>
        /// アルベールの鑑定コメント（→ AppraisalResult.FlavorText）。バランス値ではなく演出の文言のため
        /// CSVではなくコード側に置く（→ Models.RelicNames と同じ扱い）。
        /// </summary>
        private static string BuildFlavorText(AppraisalResultType type, ItemRarity rarity) => (type, rarity) switch
        {
            (AppraisalResultType.Equipment, ItemRarity.Legendary) =>
                "……信じられん。古代エルフの工房印だ。こんな物が今も地中で眠っていたとは！ 大事に使いたまえ、もう二度と出んぞ。",
            (AppraisalResultType.Equipment, ItemRarity.Epic) =>
                "ほう、いい仕事だ。刃こぼれ一つない……当時の鍛冶師の腕を褒めてやりたいね。すぐ誰かに持たせるといい。",
            (AppraisalResultType.Equipment, ItemRarity.Rare) =>
                "うん、実用品だ。銘こそ無いが造りは堅実。保管庫に入れておこう。",
            (AppraisalResultType.Equipment, _) =>
                "……まあ、使える。磨けばそれなりだ。無いよりはずっといい。",

            (AppraisalResultType.Material, ItemRarity.Legendary) =>
                "これは研究素材としては破格だ！ 頼む、これは売らないでくれ。私の研究室で使わせてほしい。",
            (AppraisalResultType.Material, ItemRarity.Epic) =>
                "おお、良質だ。しかもこの量……研究がいくつか前に進むぞ。",
            (AppraisalResultType.Material, ItemRarity.Rare) =>
                "封のなかは素材だったか。悪くない、悪くないとも。研究室へ回しておこう。",
            (AppraisalResultType.Material, _) =>
                "中身は素材だな。まあ、こんなものだろう。腐らせる前に使ってしまおう。",

            (AppraisalResultType.Gold, ItemRarity.Legendary) =>
                "……古代硬貨だ。しかもこの発行年！ 好事家が目の色を変えて買うぞ。よくぞ持ち帰った！",
            (AppraisalResultType.Gold, ItemRarity.Epic) =>
                "古代硬貨だな。状態も良い。両替商に持ち込めば、しばらくは懐の心配をせずに済む。",
            (AppraisalResultType.Gold, ItemRarity.Rare) =>
                "中身は硬貨か。まあ、鑑定代は取り返せた。悪い買い物ではなかったな。",
            _ =>
                "……うむ、小銭だ。こういう日もある。次に期待したまえ。",
        };

        /// <summary>
        /// 鑑定結果の1行サマリー（→ UI・週報ログ）。「鉄の剣（武具）」「月光草 ×4（素材）」
        /// 「古代硬貨 1,200G（換金）」のように、種別ごとに自然な表記へ整える。
        /// </summary>
        public static string BuildResultSummary(AppraisalResult result) => result.Type switch
        {
            AppraisalResultType.Equipment => $"{result.ItemName}（武具）",
            AppraisalResultType.Material => $"{result.ItemName} ×{result.ResultMaterialCount}（素材）",
            _ => $"{result.ItemName} {result.ResultGold}G（換金）",
        };

        /// <summary>希少度順→出土階層の深い順に並べた未鑑定品の一覧（UIの表示順を1箇所に集約する）。</summary>
        public static IReadOnlyList<UnidentifiedItem> SortForDisplay(GameState state) =>
            state.UnidentifiedItems
                .OrderByDescending(i => i.Rarity)
                .ThenByDescending(i => i.OriginFloor)
                .ThenBy(i => i.Name, StringComparer.Ordinal)
                .ToList();
    }
}
