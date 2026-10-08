using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 依頼掲示板（→ 03 §4.9・§4.10・§0.64）。季節のはじめに依頼が届いた週は自動で開き、大迷宮画面の「📜 依頼掲示板」からいつでも開ける。
///
/// 掲示中の依頼は「受ける／断る」、受けた納品は「納める」、受けた派遣は相手を選んで「派遣する」（ひと季節で帰ってくる、§0.85）。撃破・完全解析は
/// 週次決算で自動で判定する。文言・条件・報酬の値はすべて Core（CommissionSystem・DungeonAnomalySystem）から取る。
/// 行は依頼の数だけ毎回作り直す（数が週ごとに変わるため）。
/// </summary>
public partial class CommissionPopup : PopupPanel
{
	private RichTextLabel _headerLabel = null!;
	private RichTextLabel _anomalyLabel = null!;
	private VBoxContainer _commissionList = null!;
	private RichTextLabel _statusLabel = null!;

	private GameState _state = null!;
	private CommissionSystem _commissionSystem = null!;

	/// <summary>ポップアップが閉じたことを通知する（所持金・在庫・在籍者の変化を画面に反映させるため）。</summary>
	public event Action Closed = delegate { };

	/// <summary>週報ログへの追記を依頼する（納品・派遣の達成を記録するため。BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	public override void _Ready()
	{
		_headerLabel = GetNode<RichTextLabel>("%HeaderLabel");
		_anomalyLabel = GetNode<RichTextLabel>("%AnomalyLabel");
		_commissionList = GetNode<VBoxContainer>("%CommissionList");
		_statusLabel = GetNode<RichTextLabel>("%StatusLabel");
		GetNode<Button>("%CloseButton").Pressed += Hide;
		PopupHide += () => Closed.Invoke();
	}

	public void Open(GameState state, CommissionSystem commissionSystem)
	{
		_state = state;
		_commissionSystem = commissionSystem;
		_statusLabel.Clear();
		Refresh();
		PopupCentered();
	}

	private void Refresh()
	{
		int accepted = CommissionSystem.AcceptedCount(_state);
		_headerLabel.Clear();
		_headerLabel.AppendText(
			$"[b]受けている依頼 {accepted}/{CommissionBalance.MaxAccepted}件[/b]　" +
			$"[color=gray]季節のはじめに{CommissionBalance.OffersPerSeason}件届く。断っても罰は無いが、受けた依頼を期限までに果たせないと" +
			$"マスターの機嫌が{CommissionBalance.FailureMoodLoss}下がる。撃破・完全解析は週の決算で自動で判定する。[/color]");

		_anomalyLabel.Clear();
		_anomalyLabel.AppendText(_state.Anomaly is { } anomaly
			? $"[color=orange]⚠ 迷宮の異変：{DungeonAnomalySystem.DescribeStatus(_state, anomaly)}[/color]\n[color=gray]{DungeonAnomalySystem.Describe(_state, anomaly)}[/color]"
			: "[color=gray]⚠ 迷宮の異変：今は無い（季節の4週目に予告される）。[/color]");

		// 押したボタンの行もここで作り直すため、外してから解放する（QueueFree だけだと同じフレームの間は古い行が残る）。
		foreach (var child in _commissionList.GetChildren())
		{
			_commissionList.RemoveChild(child);
			child.QueueFree();
		}

		var commissions = _state.Commissions.OrderByDescending(c => c.Accepted).ThenBy(c => c.DeadlineWeek).ToList();
		if (commissions.Count == 0)
		{
			_commissionList.AddChild(new Label { Text = "掲示中・受けている依頼は無い。次の季節のはじめに届く。" });
			return;
		}

		_commissionList.AddThemeConstantOverride("separation", 8);
		foreach (var c in commissions)
			_commissionList.AddChild(BuildRow(c));
	}

	private Control BuildRow(GuildCommission c)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", BuildRowStyle(c));
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 8);
		panel.AddChild(margin);
		var vbox = new VBoxContainer();
		margin.AddChild(vbox);

		int left = c.WeeksLeft(_state.WeekNumber);
		string state = c.Accepted ? "[color=lime]受けている[/color]" : "[color=khaki]掲示中[/color]";
		string patron = CommissionSystem.DescribePatronProgress(_state, c.ClientId);
		var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		text.AppendText(
			$"[b]{CommissionSystem.ClientName(c)}[/b]　{CommissionSystem.TypeLabel(c.Type)}　{state}　" +
			$"[color={(left <= CommissionBalance.DeadlineWarningWeeks ? "orange" : "gray")}]期限 {GameCalendar.Format(c.DeadlineWeek)}（残り{left}週）[/color]\n" +
			$"「{c.Text}」\n" +
			$"条件：{CommissionSystem.DescribeCondition(_state, c)}\n" +
			$"報酬：{CommissionSystem.DescribeReward(c)}" +
			(patron.Length > 0 ? $"　[color=gray]（{patron}）[/color]" : ""));
		if (!c.Accepted && CommissionBalance.FindClient(c.ClientId) is { } client)
			text.AppendText($"\n[color=cyan]アルベール{client.AlbertLine}[/color]");
		vbox.AddChild(text);

		var buttons = new HBoxContainer();
		vbox.AddChild(buttons);

		if (!c.Accepted)
		{
			var accept = new Button { Text = "受ける", Disabled = !CommissionSystem.CanAccept(_state, c) };
			if (accept.Disabled)
				accept.TooltipText = $"受けられるのは{CommissionBalance.MaxAccepted}件まで。";
			accept.Pressed += () =>
			{
				if (CommissionSystem.TryAccept(_state, c))
					SetStatus($"[color=lime]{CommissionSystem.ClientName(c)}の依頼を受けた。[/color]");
				Refresh();
			};
			var decline = new Button { Text = "断る" };
			decline.Pressed += () =>
			{
				CommissionSystem.Decline(_state, c);
				SetStatus($"[color=gray]{CommissionSystem.ClientName(c)}の依頼を断った。[/color]");
				Refresh();
			};
			buttons.AddChild(accept);
			buttons.AddChild(decline);
		}
		else if (c.Type == CommissionType.Deliver)
		{
			var deliver = new Button { Text = "納める", Disabled = !CommissionSystem.CanDeliver(_state, c) };
			if (deliver.Disabled)
				deliver.TooltipText = "在庫が足りない。採取で集めるか、倉庫で売らずに残しておくこと。";
			deliver.Pressed += () =>
			{
				if (_commissionSystem.TryDeliver(_state, c) is { } done)
					ReportCompletion(done);
				Refresh();
			};
			buttons.AddChild(deliver);
		}
		else if (c.Type == CommissionType.Loan)
		{
			var candidates = CommissionSystem.GetLoanCandidates(_state, c);
			var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			foreach (var a in candidates)
				picker.AddItem($"{a.Name}（{a.JobClass}・{a.Age}歳・{c.StatName}{AdventurerStatAccessorView(a, c.StatName!)}）");
			if (candidates.Count == 0)
				picker.AddItem("条件を満たす待機中の冒険者がいない");
			picker.Disabled = candidates.Count == 0;

			var loan = new Button
			{
				Text = $"派遣する（{CommissionBalance.LoanWeeks}週で帰ってくる）",
				Disabled = candidates.Count == 0,
				TooltipText = "派遣中は名簿に残るが、出撃・訓練・大会には出られない（週給は先方持ち）。\n帰ってくると、派遣先で鍛えた能力が伸び、特性が付くこともある。",
			};
			loan.Pressed += () =>
			{
				int index = picker.Selected;
				if (index < 0 || index >= candidates.Count) return;
				if (_commissionSystem.TryLoan(_state, c, candidates[index]) is { } done)
					ReportCompletion(done);
				Refresh();
			};
			// 候補の6能力をまとめて見せる（条件の能力だけでは、誰を送るか選びにくいため）
			var detail = new Label { Modulate = new Color(0.7f, 0.7f, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
			void ShowDetail(long index)
			{
				detail.Text = index >= 0 && index < candidates.Count
					? $"STR {candidates[(int)index].STR}／AGI {candidates[(int)index].AGI}／VIT {candidates[(int)index].VIT}／MND {candidates[(int)index].MND}／DEX {candidates[(int)index].DEX}／INT {candidates[(int)index].INT}／LDR {candidates[(int)index].LDR}"
					: "";
			}
			picker.ItemSelected += ShowDetail;
			ShowDetail(picker.Selected);
			buttons.AddChild(picker);
			buttons.AddChild(loan);
			vbox.AddChild(detail);
		}
		else
		{
			string achieved = CommissionSystem.IsAchieved(_state, c) ? "（条件を満たした。今週の決算で達成になる）" : "";
			buttons.AddChild(new Label { Text = $"大迷宮の結果で週の決算に判定する{achieved}", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		}

		return panel;
	}

	private static readonly Color[] ClientPalette =
	{
		new("#60a5fa"), new("#f472b6"), new("#fbbf24"), new("#34d399"), new("#c084fc"), new("#fb923c"),
	};

	/// <summary>行の背景：依頼人ごとの色の左帯。受けている依頼は帯を太く明るく、掲示中は細く暗くする。</summary>
	private static StyleBoxFlat BuildRowStyle(GuildCommission c)
	{
		int hash = 0;
		foreach (char ch in c.ClientId)
			hash = hash * 31 + ch;
		var color = ClientPalette[(hash & 0x7fffffff) % ClientPalette.Length];
		var style = new StyleBoxFlat
		{
			BgColor = new Color(1, 1, 1, c.Accepted ? 0.07f : 0.03f),
			BorderColor = c.Accepted ? color : color.Darkened(0.45f),
			BorderWidthLeft = c.Accepted ? 5 : 3,
		};
		style.SetCornerRadiusAll(3);
		return style;
	}

	/// <summary>派遣の候補の表示用に、条件の能力の素の値を引く（Core の素の値＝装備の補正なし）。</summary>
	private static int AdventurerStatAccessorView(Adventurer a, string stat) => stat switch
	{
		"STR" => a.STR,
		"AGI" => a.AGI,
		"VIT" => a.VIT,
		"MND" => a.MND,
		"DEX" => a.DEX,
		"LDR" => a.LDR,
		_ => a.INT,
	};

	private void ReportCompletion(CommissionCompletion done)
	{
		foreach (var line in CommissionLog.CompletionLines(done))
			LogRequested.Invoke(line);
		SetStatus($"[color=lime]{done.ClientName}の依頼を果たした（+{done.Gold}G ほか）。[/color]");
	}

	private void SetStatus(string bbcode)
	{
		_statusLabel.Clear();
		_statusLabel.AppendText(bbcode);
	}
}

/// <summary>依頼の達成・失敗・到着の週報の文面（MainDashboard と CommissionPopup で共通）。</summary>
public static class CommissionLog
{
	public static IEnumerable<string> CompletionLines(CommissionCompletion done)
	{
		string what = done.LoanedAdventurerName != null
			? $"{done.LoanedAdventurerName}を{done.ClientName}へひと季節派遣し、"
			: "";
		string bonus = done.BonusMaterialId != null
			? $"・{MaterialBalance.Find(done.BonusMaterialId)?.Name ?? done.BonusMaterialId}×{done.BonusMaterialCount}"
			: "";
		yield return $"[color=gold][b]📜 {what}{done.ClientName}の依頼（{CommissionSystem.TypeLabel(done.Commission.Type)}）を果たした！[/b][/color]" +
			$"[color=lime] 報酬 +{done.Gold}G・{(done.Relic != null ? "未鑑定の遺物「" + done.Relic.Name + "」" : "遺物")}{bonus}・機嫌{done.MoodApplied:+0;-0;+0}（{done.ClientName}の依頼 達成{done.ClientCompletions}件）[/color]";
		if (done.PatronUnique != null)
			yield return $"[color=gold][font_size=20][b]🎁 {done.ClientName}から信頼の証「{done.PatronUnique.Name}」が届いた！（保管庫へ）[/b][/font_size][/color]";
	}
}
