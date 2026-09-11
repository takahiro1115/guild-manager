using Godot;
using System.Linq;
using System.Text;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;

/// <summary>
/// Phase 2 最小UI。「編成→週送り→結果→資金」の輪を一周させるためだけの画面。
/// 加齢・満足度・施設・格付け等はまだ扱わない（→ docs/06_タスクリスト.md Phase 3以降）。
///
/// 既知の割り切り（MVPの簡略化）：
///  - 週送りのたびにクエスト一覧・冒険者一覧の選択状態はリセットされる（毎週選び直す）。
///  - 重傷（出撃不可）の冒険者も選択自体は止めていない（ラベルで表示するのみ）。
/// </summary>
public partial class MainDashboard : Control
{
    private const int MaxPartySize = 4;

    private GameState _state = null!;
    private QuestResolver _questResolver = null!;
    private EconomySystem _economySystem = null!;
    private InjuryRecoverySystem _injuryRecoverySystem = null!;

    private Label _weekLabel = null!;
    private Label _goldLabel = null!;
    private ItemList _questList = null!;
    private ItemList _adventurerList = null!;
    private RichTextLabel _resultLog = null!;
    private Button _nextWeekButton = null!;

    public override void _Ready()
    {
        _weekLabel = GetNode<Label>("%WeekLabel");
        _goldLabel = GetNode<Label>("%GoldLabel");
        _questList = GetNode<ItemList>("%QuestList");
        _adventurerList = GetNode<ItemList>("%AdventurerList");
        _resultLog = GetNode<RichTextLabel>("%ResultLog");
        _nextWeekButton = GetNode<Button>("%NextWeekButton");

        _adventurerList.SelectMode = ItemList.SelectModeEnum.Multi;
        _adventurerList.MultiSelected += OnAdventurerMultiSelected;
        _nextWeekButton.Pressed += OnNextWeekPressed;

        _state = new GameState
        {
            Adventurers = SampleData.CreateStarterAdventurers(),
            AvailableQuests = SampleData.CreateStarterQuests(),
        };
        // シード固定の乱数。同じシードなら毎回同じ結果になる（デバッグしやすくするため）。
        _questResolver = new QuestResolver(new SeededRng(42));
        _economySystem = new EconomySystem();
        _injuryRecoverySystem = new InjuryRecoverySystem();

        RefreshAll();

        if (_questList.ItemCount > 0)
            _questList.Select(0);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Space })
        {
            OnNextWeekPressed();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 冒険者一覧の選択制御。
    /// ・5人目以降が選ばれそうになったら取り消す
    /// ・重傷（出撃不可）の冒険者は選択させない（→ 03 §3.6：本来は毎週回復するが未実装のため応急処置）
    /// </summary>
    private void OnAdventurerMultiSelected(long index, bool selected)
    {
        if (!selected) return;

        var adventurer = _state.Adventurers[(int)index];
        if (!adventurer.IsAvailable)
        {
            _adventurerList.Deselect((int)index);
            AppendLog($"[color=gray]{adventurer.Name} は重傷のため出撃できません。[/color]");
            return;
        }

        var selectedItems = _adventurerList.GetSelectedItems();
        if (selectedItems.Length > MaxPartySize)
        {
            _adventurerList.Deselect((int)index);
        }
    }

    /// <summary>
    /// 「次週へ」の本体。
    /// パーティを選んでいれば遠征を解決し、選んでいなければ「休養」として扱う。
    /// どちらの場合も、週給引き落とし・負傷回復・週数の進行は必ず行う
    /// （→ 03 §3.6：全員負傷中でも時間を進めて回復を待てるようにするため）。
    /// </summary>
    private void OnNextWeekPressed()
    {
        int thisWeek = _state.WeekNumber;
        var selectedQuestIndices = _questList.GetSelectedItems();
        var selectedAdventurerIndices = _adventurerList.GetSelectedItems();
        bool wantsToDispatch = selectedAdventurerIndices.Length > 0;

        if (wantsToDispatch)
        {
            if (selectedQuestIndices.Length == 0)
            {
                AppendLog("[color=orange]クエストを選択してください。[/color]");
                return;
            }

            var quest = _state.AvailableQuests[selectedQuestIndices[0]];
            var party = new Party();
            foreach (int idx in selectedAdventurerIndices)
            {
                party.TryAdd(_state.Adventurers[idx]);
            }

            var result = _questResolver.Resolve(party, quest);
            _economySystem.ApplyReward(_state, result.RewardGold);
            LogResult(thisWeek, quest, result);
        }
        else
        {
            AppendLog($"[color=gray]第{thisWeek}週：今週は誰も出撃せず、静養に努めた。[/color]");
        }

        // 出撃の有無にかかわらず、時間は必ず進む。
        _economySystem.ApplyWeeklyWages(_state);
        _injuryRecoverySystem.ProcessWeeklyRecovery(_state);
        _state.WeekNumber++;

        RefreshAll();
    }

    private void LogResult(int weekNumber, Quest quest, WeekResolutionResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[b]第{weekNumber}週：{quest.Name}[/b]");
        sb.AppendLine($"遭遇: {result.Encounter} / 結果: {result.Outcome} (Ratio={result.Ratio:F2})");
        sb.AppendLine(result.QuestAchieved
            ? $"達成！報酬 {result.RewardGold} G"
            : "任務失敗。報酬なし。");

        foreach (var kv in result.HpLostByAdventurer)
        {
            var adv = _state.Adventurers.FirstOrDefault(a => a.Id == kv.Key);
            if (adv != null)
                sb.AppendLine($" - {adv.Name}: HP -{kv.Value}（残りHP {adv.CurrentHP}/{adv.MaxHP}）");
        }

        AppendLog(sb.ToString());
    }

    private void AppendLog(string bbcodeText)
    {
        _resultLog.AppendText(bbcodeText + "\n");
    }

    private void RefreshAll()
    {
        _weekLabel.Text = $"週: {_state.WeekNumber}";
        _goldLabel.Text = $"所持金: {_state.Gold} G";

        _questList.Clear();
        foreach (var q in _state.AvailableQuests)
        {
            _questList.AddItem($"[{q.Rank}] {q.Name}（難易度{q.Difficulty} / 報酬{q.RewardGold}G）");
        }

        _adventurerList.Clear();
        foreach (var a in _state.Adventurers)
        {
            string status = a.Injury == InjurySeverity.Severe
                ? $"【重傷・出撃不可・回復まで{a.InjuryWeeksRemaining}週】"
                : "";
            _adventurerList.AddItem($"{a.Name}（{a.JobClass}） HP{a.CurrentHP}/{a.MaxHP} {status}");
        }
    }
}
