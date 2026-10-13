using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 月報の小窓（2026年10月・§0.71、→ Core の MonthlyReport）。「次の月へ」で月を進めるたびに開き、どの画面にいても月の結果を見せる。
/// §0.81：タブで分け、部隊・冒険者が何をしてどうなったかを見せる。
///  - 概要：来月の作戦（§0.98。潜行の部隊が主に勝てずに戻った月に、次の手を選ぶボタン）、見出し（月・所持金と機嫌の増減）、
///    主な出来事、来月への注意（項目ごとに、その画面へ移るボタン）
///  - 部隊：部隊ごとの方針・メンバー・週ごとの行動（出撃の結果・待機の理由）・月の結果・今のHP
///  - 冒険者：1人1行の表（所属・過ごし方の内訳・成長・今の状態）
///  - 週ごとの記録：計算の内訳つきの細かい記録（その月を進めた直後だけ。セーブには残さない）
///  - 【閉じる】【閉じて次の月へ】（割り込み〈採用試験・依頼の到着・クリア・敗北〉がある月は次の月へ進めない）
///  - 画面上部の【📅 月報】から開くと、過去の月報（直近12か月、→ GameState.MonthlyReports）を選んで見返せる
/// シーンは使わずコードで組む（MainDashboard が1つ作って使い回す）。
/// </summary>
public partial class MonthlyReportPopup : Window
{
	/// <summary>閉じた（移動・次の月へを含む）。</summary>
	public event Action Closed = delegate { };

	/// <summary>【閉じて次の月へ】が押された（閉じたあとに呼ぶ）。</summary>
	public event Action NextMonthRequested = delegate { };

	/// <summary>注意の項目から画面へ移る（閉じたあとに呼ぶ）。</summary>
	public event Action<MonthlyNoteTarget> JumpRequested = delegate { };

	/// <summary>来月の作戦の手を選んだ（§0.98。小窓は閉じない。方針の書き換えは MainDashboard が Core で行う）。</summary>
	public event Action<MonthlyPlan, PlanChoice> PlanChosen = delegate { };

	/// <summary>今月の遺物を鑑定してゲームの状態が変わった（§0.99。MainDashboard が画面を描き直す）。</summary>
	public event Action StateChanged = delegate { };

	private Func<GameState> _getState = null!;
	private AppraisalSystem _appraisal = null!;
	private VBoxContainer _relicsBox = null!;
	private VBoxContainer _poachBox = null!;
	/// <summary>今月の遺物の欄で直前に鑑定した結果（欄を作り直しても残す。月報を開き直したら消す）。</summary>
	private string _relicResultText = "";
	private MonthlyReport _shownReport;

	private OptionButton _historyOption = null!;
	private TabContainer _tabs = null!;
	private RichTextLabel _summary = null!;
	private VBoxContainer _notesBox = null!;
	private VBoxContainer _plansBox = null!;
	/// <summary>作戦のボタンを出すか（月を進めた直後だけ。過去の月報では文だけ）。</summary>
	private bool _plansInteractive;
	private RichTextLabel _squads = null!;
	private RichTextLabel _adventurers = null!;
	private RichTextLabel _weekly = null!;
	private Button _closeButton = null!;
	private Button _nextButton = null!;

	private const int WeeklyTab = 3;

	private List<MonthlyReport> _history = new();

	public override void _Ready()
	{
		Title = "📅 月報";
		Exclusive = true;
		Unresizable = false;
		Size = new Vector2I(1180, 820);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += () => CloseWith(null);

		var panel = new PanelContainer { AnchorRight = 1, AnchorBottom = 1 };
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(panel);
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 14);
		panel.AddChild(margin);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		margin.AddChild(root);

		_historyOption = new OptionButton { Visible = false };
		_historyOption.ItemSelected += index => ShowEntry(_history[_history.Count - 1 - (int)index], null);
		root.AddChild(_historyOption);

		_tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		root.AddChild(_tabs);

		// 概要：見出し・主な出来事 ＋ 来月への注意（画面へ移るボタンつき）
		var overview = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		overview.AddThemeConstantOverride("separation", 8);
		_summary = NewText();
		overview.AddChild(_summary);
		_plansBox = new VBoxContainer();
		_plansBox.AddThemeConstantOverride("separation", 4);
		overview.AddChild(_plansBox);
		overview.MoveChild(_plansBox, 0); // 来月の作戦は月報の先頭に（§0.98）
		_poachBox = new VBoxContainer();
		_poachBox.AddThemeConstantOverride("separation", 4);
		overview.AddChild(_poachBox);
		overview.MoveChild(_poachBox, 1); // 引き抜きの話（§0.102）は来月の作戦の次（月を止めて答えを求めるので上に）
		_relicsBox = new VBoxContainer();
		_relicsBox.AddThemeConstantOverride("separation", 4);
		overview.AddChild(_relicsBox); // 今月の遺物（§0.99）は主な出来事のあと、来月への注意の前
		_notesBox = new VBoxContainer();
		_notesBox.AddThemeConstantOverride("separation", 4);
		overview.AddChild(_notesBox);
		AddTab("概要", overview);

		_squads = NewText();
		AddTab("部隊", _squads);
		_adventurers = NewText();
		AddTab("冒険者", _adventurers);
		_weekly = NewText();
		AddTab("週ごとの記録", _weekly);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 10);
		_closeButton = new Button { Text = "閉じる", CustomMinimumSize = new Vector2(140, 38) };
		_closeButton.Pressed += () => CloseWith(null);
		_nextButton = new Button { Text = "📅 閉じて次の月へ", CustomMinimumSize = new Vector2(220, 38) };
		_nextButton.Pressed += () => CloseWith(() => NextMonthRequested.Invoke());
		buttons.AddChild(_closeButton);
		buttons.AddChild(_nextButton);
		root.AddChild(buttons);
	}

	/// <summary>今月の遺物（§0.99）を鑑定するための、今のゲームの状態と鑑定の仕組み（MainDashboard が渡す。ロードで状態が入れ替わるので関数で受ける）。</summary>
	public void Initialize(Func<GameState> getState, AppraisalSystem appraisal)
	{
		_getState = getState;
		_appraisal = appraisal;
	}

	private static RichTextLabel NewText() =>
		new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

	/// <summary>タブを1つ足す（中身はスクロールできるようにする）。</summary>
	private void AddTab(string title, Control content)
	{
		var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			pad.AddThemeConstantOverride($"margin_{side}", 8);
		pad.AddChild(content);
		scroll.AddChild(pad);
		_tabs.AddChild(scroll);
		_tabs.SetTabTitle(_tabs.GetTabCount() - 1, title);
	}

	/// <summary>月を進めた直後の月報を開く。weeklyBbcode は週ごとの記録（null ならそのタブを隠す）。</summary>
	public void ShowMonth(MonthlyReport report, string weeklyBbcode, bool allowNext)
	{
		_historyOption.Visible = false;
		_plansInteractive = true;
		_relicResultText = "";
		ShowEntry(report, weeklyBbcode);
		_nextButton.Visible = allowNext;
		PopupCentered(Size);
		(allowNext ? _nextButton : _closeButton).GrabFocus();
	}

	/// <summary>過去の月報（古い順の一覧）を見返す。最新から表示し、上の選択で切り替える。</summary>
	public void ShowHistory(IReadOnlyList<MonthlyReport> reports)
	{
		_history = reports.ToList();
		_plansInteractive = false;
		_historyOption.Clear();
		for (int i = _history.Count - 1; i >= 0; i--)
			_historyOption.AddItem($"{GameCalendar.FormatMonth(_history[i].LastWeek)}{(_history[i].MonthCompleted ? "" : "（途中経過）")}");
		_historyOption.Visible = _history.Count > 0;
		if (_history.Count > 0)
		{
			_historyOption.Selected = 0;
			ShowEntry(_history[^1], null);
		}
		else
		{
			_summary.Clear();
			_summary.AppendText("[color=gray]まだ月報は無い。「次の月へ」で月を進めると、ここに直近12か月分が残る。[/color]");
			ClearNotes();
			ClearBox(_plansBox);
			ClearBox(_poachBox);
			ClearBox(_relicsBox);
			_squads.Clear();
			_adventurers.Clear();
			_weekly.Clear();
			_tabs.SetTabHidden(WeeklyTab, true);
			_tabs.CurrentTab = 0;
		}
		_nextButton.Visible = false;
		PopupCentered(Size);
		_closeButton.GrabFocus();
	}

	private void ShowEntry(MonthlyReport report, string weeklyBbcode)
	{
		Title = report.MonthCompleted ? "📅 月報" : "📅 途中経過";
		_tabs.CurrentTab = 0;
		_summary.Clear();
		_summary.AppendText(SummaryText(report));

		FillPlans(report);
		_shownReport = report;
		FillPoach();
		FillContract();
		FillSponsor();
		FillRelics();
		ClearNotes();
		if (report.Notes.Count > 0)
		{
			_notesBox.AddChild(new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = "[b]📌 来月への注意[/b]" });
			foreach (var note in report.Notes)
			{
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 8);
				row.AddChild(new Label { Text = $"・{note.Text}", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
				if (note.Target != MonthlyNoteTarget.None)
				{
					var target = note.Target;
					var jump = new Button { Text = TargetLabel(target), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
					jump.Pressed += () => CloseWith(() => JumpRequested.Invoke(target));
					row.AddChild(jump);
				}
				_notesBox.AddChild(row);
			}
		}

		_squads.Clear();
		_squads.AppendText(SquadsText(report));
		_adventurers.Clear();
		_adventurers.AppendText(AdventurersText(report));

		bool hasWeekly = !string.IsNullOrEmpty(weeklyBbcode);
		_tabs.SetTabHidden(WeeklyTab, !hasWeekly);
		_weekly.Clear();
		if (hasWeekly)
			_weekly.AppendText(weeklyBbcode);
	}

	private void ClearNotes() => ClearBox(_notesBox);

	private static void ClearBox(Container box)
	{
		foreach (var child in box.GetChildren())
		{
			box.RemoveChild(child);
			child.QueueFree();
		}
	}

	/// <summary>
	/// 来月の作戦（§0.98）：主に勝てずに戻った潜行の部隊ごとに、何があったかと次の手（ボタンと見込み）。
	/// 同じ主で2回目からは1行にたたむ。過去の月報では選んだ手を変えられないので、文だけにする。
	/// </summary>
	private void FillPlans(MonthlyReport report)
	{
		ClearBox(_plansBox);
		if (report.Plans.Count == 0) return;
		_plansBox.AddChild(new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = "[b]🧭 来月の作戦[/b]" });
		foreach (var plan in report.Plans)
		{
			string head = $"「{plan.PartyName}」は{plan.BossLabel}で{plan.Situation}";
			if (plan.Repeated || !_plansInteractive)
			{
				string tail = plan.Repeated ? "。作戦は大迷宮の方針で変えられる" : "";
				_plansBox.AddChild(new Label { Text = $"・{(plan.Repeated ? "また" : "")}{head}{tail}", AutowrapMode = TextServer.AutowrapMode.WordSmart });
				continue;
			}

			_plansBox.AddChild(new Label { Text = $"・{head}。次の手は？", AutowrapMode = TextServer.AutowrapMode.WordSmart });
			if (plan.Advice.Length > 0)
				_plansBox.AddChild(new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = $"[color=orange]　{plan.Advice}[/color]" });
			var buttons = new List<Button>();
			foreach (var option in plan.Options)
			{
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 8);
				var button = new Button { Text = option.Label, CustomMinimumSize = new Vector2(240, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
				row.AddChild(button);
				row.AddChild(new Label { Text = option.Detail, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
				_plansBox.AddChild(row);
				buttons.Add(button);
				var choice = option.Choice;
				button.Pressed += () =>
				{
					PlanChosen.Invoke(plan, choice);
					foreach (var b in buttons)
						b.Disabled = true;
					button.Text = $"✔ {option.Label}";
				};
			}
		}
	}

	/// <summary>
	/// 今月の遺物（§0.99）：この月に持ち帰って、まだ鑑定していない遺物。銅・銀は【まとめて鑑定（予算内）】、金・虹は1つずつ【鑑定】。
	/// 鑑定の結果はこの欄の下に出す。月を進めた直後だけボタンを出し、過去の月報では数だけ。
	/// </summary>
	private void FillRelics()
	{
		ClearBox(_relicsBox);
		if (_shownReport == null || _shownReport.RelicIds.Count == 0) return;
		if (!_plansInteractive || _getState == null)
		{
			_relicsBox.AddChild(new Label { Text = $"🔬 この月に持ち帰った遺物：{_shownReport.RelicIds.Count}個" });
			return;
		}

		var state = _getState();
		var ids = _shownReport.RelicIds.ToHashSet();
		var relics = AppraisalSystem.SortForDisplay(state).Where(r => ids.Contains(r.Id)).ToList();
		int others = state.UnidentifiedItems.Count - relics.Count;
		_relicsBox.AddChild(new RichTextLabel
		{
			BbcodeEnabled = true, FitContent = true, ScrollActive = false,
			Text = $"[b]🔬 今月の遺物[/b]　[color=gray]" +
				(relics.Count > 0 ? $"持ち帰った{_shownReport.RelicIds.Count}個のうち、まだ鑑定していない{relics.Count}個" : $"持ち帰った{_shownReport.RelicIds.Count}個は、すべて鑑定した") +
				(others > 0 ? $"（倉庫にはほかに{others}個）" : "") + "[/color]",
		});

		foreach (var relic in relics.Where(r => !ChoreSystem.IsBulkAppraisable(r)))
		{
			var profile = GuildManager.Core.Balance.RelicBalance.Get(relic.Rarity);
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			row.AddChild(new RichTextLabel
			{
				BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Text = $"　[color={profile.ColorName}][{profile.Label}][/color] {relic.Name}　[color=gray]第{relic.OriginFloor}層[/color]",
			});
			var button = new Button { Text = $"鑑定する（{relic.AppraisalCost}G）", Disabled = state.Gold < relic.AppraisalCost, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
			string id = relic.Id;
			button.Pressed += () =>
			{
				var s = _getState();
				if (_appraisal.Appraise(s, id) is not { } result) return;
				_relicResultText = InventoryPanel.BulkAppraisalText(new[] { result }, s.MasterMood) + $"\n[color=cyan]アルベール『{result.FlavorText}』[/color]";
				AfterAppraisal();
			};
			row.AddChild(button);
			_relicsBox.AddChild(row);
		}

		var bulk = relics.Where(ChoreSystem.IsBulkAppraisable).ToList();
		if (bulk.Count > 0)
		{
			var plan = ChoreSystem.PlanAppraisal(state, bulk.Select(r => r.Id));
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			string counts = string.Join("・", bulk.GroupBy(r => r.Rarity).OrderByDescending(g => g.Key)
				.Select(g => $"{GuildManager.Core.Balance.RelicBalance.Get(g.Key).Label}{g.Count()}"));
			row.AddChild(new Label
			{
				Text = $"　銅・銀 {bulk.Count}個（{counts}）" + (plan.OverBudget > 0 ? $"。予算に収まるのは{plan.Items.Count}個（週給の{GuildManager.Core.Balance.EconomyBalance.ChoreReserveWageWeeks}週分 {plan.Reserve}G を残す）" : ""),
				AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			});
			var button = new Button
			{
				Text = plan.Items.Count > 0 ? $"🔬 まとめて鑑定（{plan.Items.Count}個・{plan.TotalCost}G）" : "🔬 予算が足りない",
				Disabled = plan.Items.Count == 0, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				TooltipText = $"所持金に週給の{GuildManager.Core.Balance.EconomyBalance.ChoreReserveWageWeeks}週分（{plan.Reserve}G）を残して鑑定する",
			};
			var bulkIds = bulk.Select(r => r.Id).ToList();
			button.Pressed += () =>
			{
				var s = _getState();
				var results = ChoreSystem.AppraiseBulk(s, _appraisal, bulkIds);
				if (results.Count == 0) return;
				_relicResultText = InventoryPanel.BulkAppraisalText(results, s.MasterMood);
				AfterAppraisal();
			};
			row.AddChild(button);
			_relicsBox.AddChild(row);
		}

		if (_relicResultText.Length > 0)
			_relicsBox.AddChild(new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = _relicResultText });
	}

	/// <summary>
	/// 引き抜きの話（§0.102）：返事を待っている話ごとに、昇給・ボーナスで引き留めるか、送り出すかのボタン。月を進めた直後だけボタンを出す
	/// （過去の月報では出さない）。答えは Core（IncidentSystem.ResolvePoach）。期限までに答えなければ移籍する。
	/// </summary>
	private void FillPoach()
	{
		ClearBox(_poachBox);
		if (!_plansInteractive || _getState == null) return;
		var state = _getState();
		if (state.PoachOffers.Count == 0) return;
		_poachBox.AddChild(new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = "[b]📨 引き抜きの話[/b]" });
		foreach (var offer in state.PoachOffers.ToList())
		{
			var a = state.FindAdventurer(offer.AdventurerId);
			if (a == null) continue;
			int bonus = a.WeeklyWage * GuildManager.Core.Balance.SatisfactionBalance.BonusWeeksEquivalent;
			int raised = (int)Math.Round(a.WeeklyWage * GuildManager.Core.Balance.SatisfactionBalance.MinRaiseMultiplier);
			_poachBox.AddChild(new Label
			{
				Text = $"・{a.Name}（満足度{a.Satisfaction}）に{offer.GuildName}から誘いが来ている。{GameCalendar.FormatMonth(offer.DeadlineWeek)} 第{GameCalendar.WeekOfMonth(offer.DeadlineWeek)}週までに応えないと移籍する",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			});
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var buttons = new List<Button>
			{
				new() { Text = $"昇給して引き留める（週給 {a.WeeklyWage}→{raised}G）" },
				new() { Text = $"ボーナスで引き留める（{bonus}G）", Disabled = state.Gold < bonus },
				new() { Text = "送り出す" },
			};
			var choices = new[] { PoachChoice.Raise, PoachChoice.Bonus, PoachChoice.Release };
			for (int i = 0; i < buttons.Count; i++)
			{
				var choice = choices[i];
				var button = buttons[i];
				button.Pressed += () =>
				{
					IncidentSystem.ResolvePoach(_getState(), offer, choice, new SatisfactionSystem());
					foreach (var b in buttons) b.Disabled = true;
					button.Text = $"✔ {button.Text}";
					StateChanged.Invoke();
				};
				row.AddChild(button);
			}
			_poachBox.AddChild(row);
		}
	}

	/// <summary>
	/// 契約更改（§0.103）：年のはじめに、給与をどう上げるかを3つから選ぶ（引き抜きの話の欄の下に足す）。月を進めた直後だけ。
	/// 見込み（週給が週あたりいくら増えるか・誰が上がるか）は Core（GuildFinanceSystem）。答えなければ月の終わりに据え置き。
	/// </summary>
	private void FillContract()
	{
		if (!_plansInteractive || _getState == null) return;
		var state = _getState();
		if (state.ContractReviewPendingYear is not int year) return;
		var top = GuildFinanceSystem.MeritTop(state);
		_poachBox.AddChild(new RichTextLabel
		{
			BbcodeEnabled = true, FitContent = true, ScrollActive = false,
			Text = $"[b]📝 {year}年目の契約更改[/b]　[color=gray]給与をどう上げるか（答えなければ、この月の終わりに据え置く）[/color]",
		});
		var buttons = new List<Button>();
		foreach (var policy in new[] { ContractPolicy.Merit, ContractPolicy.Even, ContractPolicy.Freeze })
		{
			string detail = policy switch
			{
				ContractPolicy.Merit => $"功績の上位 {top.Count}人（{string.Join("・", top.Select(a => a.Name))}）だけ週給+{GuildManager.Core.Balance.EconomyBalance.ContractMeritRaise:P0}・満足度↑。ほかの子は満足度↓、同じ部隊で差がつくと相性↓",
				ContractPolicy.Even => $"全員の週給+{GuildManager.Core.Balance.EconomyBalance.ContractEvenRaise:P0}・満足度少し↑",
				_ => "お金はかからないが、全員の満足度↓（お金が好きな子は大きく↓）",
			};
			int increase = GuildFinanceSystem.WageIncrease(state, policy);
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var button = new Button { Text = $"{GuildFinanceSystem.PolicyLabel(policy)}（週給 +{increase}G/週）", CustomMinimumSize = new Vector2(300, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
			row.AddChild(button);
			row.AddChild(new Label { Text = detail, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
			_poachBox.AddChild(row);
			buttons.Add(button);
			var chosen = policy;
			button.Pressed += () =>
			{
				GuildFinanceSystem.ApplyContractReview(_getState(), chosen);
				foreach (var b in buttons) b.Disabled = true;
				button.Text = $"✔ {button.Text}";
				StateChanged.Invoke();
			};
		}
	}

	/// <summary>
	/// スポンサー契約（§0.104）：春と秋のはじめに届いた話（控えめ・強気）ごとに、結ぶ・断るボタン（契約更改の欄の下に足す）。月を進めた直後だけ。
	/// 結べるのは1件まで。結ばなければ季節の終わりに流れる（罰なし）。値と結べない理由は Core（FundingSystem）。
	/// </summary>
	private void FillSponsor()
	{
		if (!_plansInteractive || _getState == null) return;
		var state = _getState();
		var offers = FundingSystem.Offers(state);
		if (offers.Count == 0) return;
		_poachBox.AddChild(new RichTextLabel
		{
			BbcodeEnabled = true, FitContent = true, ScrollActive = false,
			Text = "[b]🤝 スポンサー契約の話[/b]　[color=gray]結べるのは1件まで。結ばなければ季節の終わりに流れる。進み具合とほかの期限との重なりは「💰 資金」の画面で見られる[/color]",
		});
		var buttons = new List<Button>();
		foreach (var offer in offers)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var sign = new Button { Text = $"結ぶ（前金 {offer.Advance} G）", Disabled = FundingSystem.SignBlockReason(state, offer) != null, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
			var decline = new Button { Text = "断る", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
			row.AddChild(sign);
			row.AddChild(decline);
			// 損得は資金の画面と同じ3行（§0.115）
			row.AddChild(new RichTextLabel
			{
				BbcodeEnabled = true, FitContent = true, ScrollActive = false,
				Text = $"[b]{(offer.Bold ? "【強気】" : "【控えめ】")}{offer.SponsorName}[/b]：{FundingSystem.QuotaLabel(offer)}（{GameCalendar.FormatMonth(offer.DeadlineWeek)} 第{GameCalendar.WeekOfMonth(offer.DeadlineWeek)}週まで）\n{FundingPanel.OfferTerms(offer)}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			});
			_poachBox.AddChild(row);
			buttons.Add(sign);
			buttons.Add(decline);
			var target = offer;
			sign.Pressed += () =>
			{
				if (!FundingSystem.Sign(_getState(), target)) return;
				foreach (var b in buttons) b.Disabled = true;
				sign.Text = $"✔ {sign.Text}";
				StateChanged.Invoke();
			};
			decline.Pressed += () =>
			{
				if (!FundingSystem.Decline(_getState(), target)) return;
				sign.Disabled = true;
				decline.Disabled = true;
				decline.Text = "✔ 断った";
				StateChanged.Invoke();
			};
		}
	}

	/// <summary>鑑定のあと：画面を描き直し（所持金・倉庫）、この欄も作り直す。</summary>
	private void AfterAppraisal()
	{
		StateChanged.Invoke();
		Callable.From(FillRelics).CallDeferred(); // 押したボタンの合図の中で欄を作り直さない
	}

	private void CloseWith(Action after)
	{
		Hide();
		Closed.Invoke();
		after?.Invoke();
	}

	private static string TargetLabel(MonthlyNoteTarget target) => target switch
	{
		MonthlyNoteTarget.Dungeon => "🏰 大迷宮へ",
		MonthlyNoteTarget.Facility => "🏥 施設へ",
		MonthlyNoteTarget.Adventurer => "📋 冒険者へ",
		MonthlyNoteTarget.Funding => "💰 資金へ",
		_ => "",
	};

	private static string ToneColor(MonthlyTone tone) => tone switch
	{
		MonthlyTone.Good => "lime",
		MonthlyTone.Warning => "orange",
		MonthlyTone.Bad => "#ff6b6b",
		_ => "#e5e7eb",
	};

	private static string Colored(MonthlyLine line) => $"[color={ToneColor(line.Tone)}]{line.Text}[/color]";

	private static string HpColor(int percent) => percent >= 70 ? "lime" : percent >= 40 ? "orange" : "#ff6b6b";

	private static string GainsText(Dictionary<string, int> gains) =>
		gains.Values.Sum() > 0
			? string.Join(" ", gains.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}+{kv.Value}"))
			: "";

	/// <summary>概要：見出し・お金と機嫌・研究の手伝い・主な出来事。</summary>
	public static string SummaryText(MonthlyReport report)
	{
		var sb = new StringBuilder();
		string title = report.MonthCompleted
			? $"{GameCalendar.FormatMonth(report.LastWeek)}の月報"
			: $"{GameCalendar.FormatMonth(report.LastWeek)}の途中経過（第{GameCalendar.WeekOfMonth(report.LastWeek)}週まで）";
		sb.AppendLine($"[font_size=20][b]{title}[/b][/font_size]");
		if (report.StopReasons.Count > 0)
			sb.AppendLine($"[color=orange][b]月の途中で止まった：{string.Join("・", report.StopReasons)}。[/b]「次の月へ」で、この月の残りを進める。[/color]");

		int goldDelta = report.GoldAfter - report.GoldBefore;
		int moodDelta = report.MoodAfter - report.MoodBefore;
		sb.AppendLine($"所持金 {report.GoldBefore}G → [b]{report.GoldAfter}G[/b]（[color={(goldDelta >= 0 ? "lime" : "orange")}]{goldDelta:+0;-0}G[/color]）" +
			$"　マスターの機嫌 {report.MoodBefore} → [b]{report.MoodAfter}[/b]（[color={(moodDelta >= 0 ? "lime" : "orange")}]{moodDelta:+0;-0}[/color]）");
		// お金の流れ（§0.103）：施設の維持費と、教官・顧問の手当
		var costs = new List<string>();
		if (report.UpkeepPaid > 0) costs.Add($"施設の維持費 [color=orange]−{report.UpkeepPaid}G[/color]");
		if (report.StipendsPaid > 0) costs.Add($"教官・顧問の手当 [color=orange]−{report.StipendsPaid}G[/color]");
		if (report.LoanRepaid > 0) costs.Add($"借金の返済 [color=orange]−{report.LoanRepaid}G[/color]"); // §0.104
		if (costs.Count > 0)
			sb.AppendLine(string.Join("　", costs));
		// 研究の手伝い（§0.73・§0.114）：待機中にアルベールの研究を手伝って貯まった分。進めている研究を早める。
		if (report.ResearchCreditGained > 0 || report.ResearchCreditAfter > 0)
			sb.AppendLine($"研究の手伝い [color=lime]+{report.ResearchCreditGained}[/color]（貯まり {report.ResearchCreditAfter}。{GuildManager.Core.Balance.TrainingBalance.ResearchCreditPerWeek}ごとに、進めている研究が1週早まる）");

		sb.AppendLine();
		sb.AppendLine("[b]📰 主な出来事[/b]");
		if (report.Highlights.Count == 0)
			sb.AppendLine("[color=gray]　目立った出来事は無かった。部隊・冒険者のタブで、それぞれの1か月を見られる。[/color]");
		foreach (var line in report.Highlights)
			sb.AppendLine($"　・{Colored(line)}");
		// 年末のギルドの順位表（§0.94）：年の最後の月報だけ
		if (report.YearStanding != null)
		{
			sb.AppendLine();
			sb.AppendLine(TournamentPanel.StandingText(report.YearStanding, $"🏅 {report.YearStanding.Year}年目　年末のギルドの順位表"));
		}
		return sb.ToString().TrimEnd('\n');
	}

	/// <summary>部隊：部隊ごとに方針・メンバー・週ごとの行動・月の結果・今のHP。</summary>
	private static string SquadsText(MonthlyReport report)
	{
		if (report.Squads.Count == 0)
			return "[color=gray]メンバーのいる部隊は無い（部隊・冒険者の画面で部隊を組む）。[/color]";
		var sb = new StringBuilder();
		foreach (var squad in report.Squads)
		{
			string order = squad.Order == SquadOrder.None
				? "[color=orange]方針なし[/color]"
				: $"{DungeonPanel.OrderIcon(squad.Order)}{DungeonPanel.OrderName(squad.Order)}" + (squad.FieldName.Length > 0 ? $"（{squad.FieldName}）" : "");
			sb.AppendLine($"[font_size=18][b]{squad.Name}[/b][/font_size]　{order}　[color=gray]HP[/color] [color={HpColor(squad.HpPercent)}]{squad.HpPercent}%[/color]");
			sb.AppendLine($"[color=gray]{string.Join("・", squad.Members)}[/color]");
			foreach (var week in squad.Weeks)
				sb.AppendLine($"　[color=gray]第{week.WeekOfMonth}週[/color]　{Colored(week)}");
			sb.AppendLine($"　[b]結果[/b]：{squad.Result}");
			sb.AppendLine();
		}
		return sb.ToString().TrimEnd('\n');
	}

	/// <summary>冒険者：1人1行の表（名前・所属・過ごし方・成長・今の状態）。</summary>
	private static string AdventurersText(MonthlyReport report)
	{
		if (report.Adventurers.Count == 0)
			return "[color=gray]この月報には冒険者ごとの記録が無い。[/color]";
		var sb = new StringBuilder();
		sb.Append("[table=5]");
		foreach (var head in new[] { "名前", "所属", "過ごし方", "成長", "今の状態" })
			sb.Append($"[cell][color=gray]{head}　[/color][/cell]");
		foreach (var a in report.Adventurers.OrderBy(a => a.Squad.Length == 0).ThenBy(a => a.Squad))
		{
			string gains = GainsText(a.Gains);
			string status = $"HP [color={HpColor(a.HpPercent)}]{a.HpPercent}%[/color]" +
				(a.Status.Count > 0 ? "　" + string.Join("　", a.Status.Select(Colored)) : "");
			sb.Append($"[cell][b]{a.Name}[/b]　[color=gray]{AdventurerPanel.JobLabel(a.Job)}・{a.Age}歳[/color]　[/cell]");
			sb.Append($"[cell]{(a.Squad.Length > 0 ? a.Squad : "[color=gray]—[/color]")}　[/cell]");
			sb.Append($"[cell]{a.Activities}　[/cell]");
			sb.Append($"[cell]{(gains.Length > 0 ? $"[color=lime]{gains}[/color]" : "[color=gray]—[/color]")}　[/cell]");
			sb.Append($"[cell]{status}[/cell]");
		}
		sb.Append("[/table]");
		return sb.ToString();
	}
}
