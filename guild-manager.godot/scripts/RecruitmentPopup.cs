using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 新春採用試験（仕様書 03 §2.4）のモーダルポップアップ。仕様書 03 §9 参照。
///
/// 年1回・新年第1週（2年目以降）にのみ MainDashboard から Open() で呼び出される。
/// 採用処理（採用する/見送るの意思決定）が完了するまで閉じられない
/// （Escape・外側クリック等で意図せず閉じようとした場合は PopupHide を検知して
/// 即座に再表示し、強制的にモーダルとして機能させる）。
///
/// 画面は「部隊・冒険者」画面と同じ左右2ペイン（→ 03 §9、§0.37）：左が候補者カードの一覧、
/// 右が志願者の個人詳細（AdventurerPanel を志願者表示モード〈→ ShowCandidatePreview〉で再利用）。
/// カードの行クリックは右ペインの表示切替だけを行い、採用するかどうかは各カードのチェックボックスで
/// 選ぶ（行クリックでチェックが変わらないよう、両者の判定を分けている）。
/// チェックした全員を「選択した冒険者を採用する」で一括契約し、ポップアップを閉じる。
/// 「今回は見送る」は誰も採用せずに閉じる。
///
/// 第1週の新春ドラフト（→ OpenDraft、03 §2.4）も同じポップアップで行う：契約金0G・
/// ちょうど規定人数（2名）を選んだときだけ確定でき、「見送る」は出さない。
/// </summary>
public partial class RecruitmentPopup : PopupPanel
{
	/// <summary>モーダルの大きさ（1920×1080基準。左ペイン約680＋右ペイン約780＋余白）。</summary>
	private static readonly Vector2I PopupSize = new(1560, 880);

	private static readonly Color CardBorderColor = new(0.3f, 0.3f, 0.35f);
	private static readonly Color CardHighlightColor = new("#38bdf8");
	private static readonly Color CardBackgroundColor = new(0.13f, 0.13f, 0.16f);
	private static readonly Color CardCheckedBackgroundColor = new(0.14f, 0.2f, 0.16f);

	private Label _titleLabel = null!;
	private Label _summaryLabel = null!;
	private Label _guideLabel = null!;
	private VBoxContainer _candidateCards = null!;
	private AdventurerPanel _detailPanel = null!;
	private Label _statusLabel = null!;
	private Button _hireButton = null!;
	private Button _declineButton = null!;

	private GameState _state = null!;
	private RecruitmentSystem _recruitmentSystem = null!;
	private List<RecruitmentOffer> _candidates = new();
	private bool _decided;

	/// <summary>新春ドラフト中（→ OpenDraft）なら非null。通常の新春採用試験ではnull。</summary>
	private RecruitmentDraft _draft;

	/// <summary>採用のチェックが入っている候補者（Adventurer.Id）。</summary>
	private readonly HashSet<Guid> _selectedCandidateIds = new();

	/// <summary>右ペインに表示中の候補者（行クリックで切り替わる。チェックとは独立）。</summary>
	private Guid? _focusedCandidateId;

	/// <summary>候補者Id → カード（枠のハイライト・背景の切り替え用）。</summary>
	private readonly Dictionary<Guid, PanelContainer> _cardsById = new();

	/// <summary>採用処理が完了してポップアップが閉じた（＝次週へ進めてよい）ことを通知する。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_titleLabel = GetNode<Label>("%TitleLabel");
		_summaryLabel = GetNode<Label>("%SummaryLabel");
		_guideLabel = GetNode<Label>("%GuideLabel");
		_candidateCards = GetNode<VBoxContainer>("%CandidateCards");
		_detailPanel = GetNode<AdventurerPanel>("%DetailPanel");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_hireButton = GetNode<Button>("%HireButton");
		_declineButton = GetNode<Button>("%DeclineButton");

		_hireButton.Pressed += OnHirePressed;
		_declineButton.Pressed += OnDeclinePressed;
		PopupHide += OnPopupHide;
	}

	/// <summary>
	/// 第1週の新春ドラフト（→ 03 §2.4、RecruitmentSystem.StartInitialDraft）を開始する。契約金は無料、
	/// ちょうど規定人数（RecruitmentBalance.DraftHireCount）を選ぶまで確定できず、閉じられない（「見送る」も出さない）。
	///
	/// 候補者には開いた時点で職業ごとの初期装備（→ StarterEquipment）を着せておく。右ペインの能力値バーに
	/// 加入時の装備補正（水色）と装備込みの最大HPを出すため。採用時の RecruitmentSystem.TryDraftHire も同じ
	/// 装備を着せ直すだけなので結果は変わらず、選ばれなかった候補はドラフト終了とともに破棄される。
	/// </summary>
	public void OpenDraft(GameState state, RecruitmentSystem recruitmentSystem)
	{
		_draft = recruitmentSystem.StartInitialDraft(state, GetScoutMasterBonus(state));
		foreach (var offer in _draft.Offers)
			StarterEquipment.Equip(offer.Candidate);

		ShowCandidates(state, recruitmentSystem, _draft.Offers);
	}

	/// <summary>
	/// 採用試験を開始する。候補者を生成し、モーダルとして表示する。
	/// </summary>
	/// <param name="candidateCount">
	/// 提示する候補者数。省略時（null）は通常の新春採用試験と同じ人数
	/// （RecruitmentSystem.GenerateCandidatesの既定＝RecruitmentBalance.CandidateCount）。
	/// 第1週の新春ドラフトはこちらではなく OpenDraft を使う（→ MainDashboard.StartNewGame）。
	/// </param>
	public void Open(GameState state, RecruitmentSystem recruitmentSystem, int? candidateCount = null)
	{
		_draft = null;
		ShowCandidates(state, recruitmentSystem, recruitmentSystem.GenerateCandidates(state, GetScoutMasterBonus(state), candidateCount));
	}

	/// <summary>
	/// 既に生成済みの応募一覧をそのまま提示する（→ コアシステム刷新仕様 Phase 4：
	/// 旧ランク昇格試験の突破時に生成した新人を表示するために使っていた。昇格試験は旧通常クエストと
	/// 共に撤去済み（2026年9月）だが、生成済みの一覧を提示する汎用の入口として残している）。
	/// ここで再生成してしまうと、週報ログに出した人数・顔ぶれと実際の提示内容が
	/// 食い違うため、生成済みのインスタンスを受け取る形にしている。
	/// </summary>
	public void Open(GameState state, RecruitmentSystem recruitmentSystem, IReadOnlyList<RecruitmentOffer> offers)
	{
		_draft = null;
		ShowCandidates(state, recruitmentSystem, new List<RecruitmentOffer>(offers));
	}

	/// <summary>
	/// 候補一覧を差し替えて表示する（ドラフト／通常の共通部分）。選択は空から始め、先頭の候補を右ペインに出す。
	/// ドラフトでは candidates に draft.Offers をそのまま渡す（TryDraftHire が同じリストから採用者を取り除く）。
	/// </summary>
	private void ShowCandidates(GameState state, RecruitmentSystem recruitmentSystem, List<RecruitmentOffer> candidates)
	{
		_state = state;
		_recruitmentSystem = recruitmentSystem;
		_candidates = candidates;
		_decided = false;
		_selectedCandidateIds.Clear();
		_focusedCandidateId = _candidates.Count > 0 ? _candidates[0].Candidate.Id : null;

		ApplyMode();
		BuildCandidateCards();
		ShowFocusedCandidate();
		RefreshSelectionState();
		PopupCentered(PopupSize);
	}

	/// <summary>ドラフトと通常の採用試験で、見出し・案内・「見送る」の有無を切り替える。</summary>
	private void ApplyMode()
	{
		bool isDraft = _draft != null;
		_declineButton.Visible = !isDraft;
		Title = isDraft ? "新春ドラフト" : "新春採用試験";
		_titleLabel.Text = isDraft ? "【新春ドラフト】候補者一覧" : "【新春採用試験】候補者一覧";
		_guideLabel.Text = isDraft
			? $"契約金無料：志願者から{RequiredDraftHires()}名を選抜してください。行をクリックすると右に能力の詳細を表示します（採用の選択は左端のチェックボックス）。"
			: "採用する志願者にチェックを入れてください（複数可。宿舎の空き枠と所持金の範囲まで）。契約金は採用時に一括で支払います。" +
			  "行をクリックすると右に能力の詳細を表示します。";
	}

	/// <summary>
	/// スカウト顧問が任命されていれば、そのボーナスを返す（未任命なら0。→ 03 §7.3）。
	/// v1.4改訂：冒険者支援室がLv0（未建設）の間はボーナスを発生させない（防御的チェック。
	/// 通常はAdvisorSystem.TryAssignScoutMaster側のガードにより、Lv0のままAssignedScoutMasterが
	/// 設定されることは無い）。
	/// </summary>
	private static double GetScoutMasterBonus(GameState state)
	{
		if (state.GetFacilityLevel(FacilityType.RecruitmentOffice) < 1) return 0;
		if (state.AssignedScoutMaster == null) return 0;

		var scoutMaster = state.RetiredAdventurers.FirstOrDefault(a => a.Id == state.AssignedScoutMaster.Value);
		return scoutMaster == null ? 0 : AdvisorSystem.GetScoutMasterBonus(scoutMaster);
	}

	// ==== 左ペイン：候補者カード ====

	private void BuildCandidateCards()
	{
		foreach (var child in _candidateCards.GetChildren())
		{
			_candidateCards.RemoveChild(child);
			child.QueueFree();
		}
		_cardsById.Clear();

		foreach (var offer in _candidates)
		{
			var card = BuildCandidateCard(offer);
			_cardsById[offer.Candidate.Id] = card;
			_candidateCards.AddChild(card);
		}
	}

	/// <summary>
	/// 候補者1名分のカード：［チェックボックス］［前後衛・職業・氏名・年齢／契約金・初期装備・総合PA・週給／特性］。
	/// カード本体（PanelContainer）の左クリックで右ペインへ表示する。チェックボックスは自分でクリックを消費するため、
	/// チェックの操作は行選択に伝わらず、行クリックもチェックを変えない。
	/// </summary>
	private PanelContainer BuildCandidateCard(RecruitmentOffer offer)
	{
		var c = offer.Candidate;
		var id = c.Id;

		var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = "クリックで右に詳細を表示" };
		card.GuiInput += e =>
		{
			if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
				FocusCandidate(id);
		};

		var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Pass };
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_bottom", 6);
		card.AddChild(margin);

		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
		row.AddThemeConstantOverride("separation", 10);
		margin.AddChild(row);

		var check = new CheckBox { TooltipText = "チェックで採用候補に加える" };
		check.Toggled += pressed => OnCandidateToggled(id, pressed);
		row.AddChild(check);

		var info = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		info.AddThemeConstantOverride("separation", 2);
		row.AddChild(info);

		// 1行目：前後衛バッジ・職業・氏名・年齢
		var headRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
		headRow.AddThemeConstantOverride("separation", 8);
		info.AddChild(headRow);

		bool isFront = PlacementRules.GetDefault(c.JobClass) == Placement.Front;
		headRow.AddChild(MakeLabel(isFront ? "【前衛】" : "【後衛】", 14,
			isFront ? new Color(0.4f, 0.8f, 1.0f) : new Color(0.9f, 0.6f, 1.0f)));
		headRow.AddChild(MakeLabel(AdventurerPanel.JobLabel(c.JobClass), 14, new Color(1f, 0.85f, 0.4f)));
		headRow.AddChild(MakeLabel(c.Name, 16, null));
		headRow.AddChild(MakeLabel($"{c.Age}歳", 14, null));

		// 2行目：契約金・初期装備・総合PA・週給
		string cost = _draft != null ? "契約金: 0 G（無料）" : $"契約金: {offer.SigningBonus:N0} G";
		info.AddChild(MakeLabel(
			$"{cost}　初期装備: {StarterLoadoutText(c)}　総合PA {c.TotalPA:F0}　週給 {c.WeeklyWage} G", 13,
			new Color(0.85f, 0.85f, 0.85f)));

		// 3行目：先天特性
		string traits = c.TraitIds.Count == 0
			? "なし"
			: string.Join("・", c.TraitIds.Select(t => TraitCatalog.FindById(t)?.DisplayName ?? t));
		info.AddChild(MakeLabel($"特性: {traits}", 12, new Color(0.9f, 0.9f, 0.4f)));

		return card;
	}

	/// <summary>加入時の支給装備（ドラフト：職業の初期装備〈→ StarterEquipment〉、通常採用：なし）。</summary>
	private string StarterLoadoutText(Adventurer c)
	{
		if (_draft == null) return "なし（平服）";
		var (weaponId, armorId) = StarterEquipment.GetLoadout(c.JobClass);
		return $"{ItemCatalog.FindById(weaponId)?.Name}＋{ItemCatalog.FindById(armorId)?.Name}";
	}

	private static Label MakeLabel(string text, int fontSize, Color? color)
	{
		var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (color is Color c)
			label.AddThemeColorOverride("font_color", c);
		return label;
	}

	/// <summary>カード枠（表示中＝水色の太枠）と背景（チェック済み＝緑がかった色）を現在の状態に合わせる。</summary>
	private void RefreshCardStyles()
	{
		foreach (var (id, card) in _cardsById)
		{
			bool focused = _focusedCandidateId == id;
			var style = new StyleBoxFlat
			{
				BgColor = _selectedCandidateIds.Contains(id) ? CardCheckedBackgroundColor : CardBackgroundColor,
				BorderColor = focused ? CardHighlightColor : CardBorderColor,
			};
			style.SetBorderWidthAll(focused ? 3 : 1);
			style.SetCornerRadiusAll(4);
			card.AddThemeStyleboxOverride("panel", style);
		}
	}

	// ==== 右ペイン：志願者詳細 ====

	private void FocusCandidate(Guid id)
	{
		_focusedCandidateId = id;
		ShowFocusedCandidate();
		RefreshCardStyles();
	}

	private void ShowFocusedCandidate()
	{
		var offer = _candidates.FirstOrDefault(o => o.Candidate.Id == _focusedCandidateId);
		if (offer != null)
			_detailPanel.ShowCandidatePreview(offer.Candidate);
	}

	// ==== 選択（チェック）とバリデーション ====

	private void OnCandidateToggled(Guid id, bool pressed)
	{
		if (pressed)
			_selectedCandidateIds.Add(id);
		else
			_selectedCandidateIds.Remove(id);
		RefreshSelectionState();
	}

	/// <summary>ドラフトで選ぶべき人数（規定人数の残り。宿舎の空きがそれより少なければ空き枠まで）。</summary>
	private int RequiredDraftHires() =>
		_draft == null ? 0 : Math.Min(_draft.HiresRemaining, _recruitmentSystem.GetOpenSlotCount(_state));

	private List<RecruitmentOffer> SelectedOffers() =>
		_candidates.Where(o => _selectedCandidateIds.Contains(o.Candidate.Id)).ToList();

	/// <summary>
	/// チェックが変わるたびに、ヘッダーの状態サマリー・確定ボタンの活性とテキスト・不可理由を更新する。
	///  - ドラフト：ちょうど規定人数（2名）を選んだときだけ確定できる（契約金0G）
	///  - 通常：1名以上、かつ宿舎の空き枠以内、かつ合計契約金が所持金以内
	/// </summary>
	private void RefreshSelectionState()
	{
		var selected = SelectedOffers();
		int count = selected.Count;
		int totalCost = _draft != null ? 0 : selected.Sum(o => o.SigningBonus);
		int openSlots = _recruitmentSystem.GetOpenSlotCount(_state);

		string reason;
		if (_draft != null)
		{
			int required = RequiredDraftHires();
			_summaryLabel.Text = $"選択中: {count} / {required}名　合計契約金: 0 G　宿舎空き枠: {openSlots}　所持金: {_state.Gold:N0} G";
			reason = count < required ? $"あと{required - count}名選んでください（ちょうど{required}名で確定できます）。"
				: count > required ? $"選びすぎです（採用できるのは{required}名。あと{count - required}名のチェックを外してください）。"
				: "";
			_hireButton.Text = reason == ""
				? $"【 選択した {count} 名を採用する (契約金: 0 G) 】"
				: $"【 {required}名を選んでください（選択中 {count}名） 】";
		}
		else
		{
			_summaryLabel.Text = $"選択中: {count}名　合計契約金: {totalCost:N0} G　宿舎空き枠: {openSlots}　所持金: {_state.Gold:N0} G";
			reason = count == 0 ? "採用する志願者にチェックを入れてください。"
				: count > openSlots ? $"宿舎の空き枠が足りません（空き {openSlots}名 ／ 選択 {count}名）。"
				: totalCost > _state.Gold ? $"所持金が足りません（合計契約金 {totalCost:N0} G ／ 所持金 {_state.Gold:N0} G）。"
				: "";
			_hireButton.Text = $"【 選択した {count} 名を採用する (合計: {totalCost:N0} G) 】";
		}

		_hireButton.Disabled = reason != "";
		_hireButton.TooltipText = reason;
		_statusLabel.Text = reason;
		RefreshCardStyles();
	}

	// ==== 確定・見送り ====

	/// <summary>
	/// チェックした全員を一括で採用して閉じる（一覧の並び順に処理）。ドラフトは TryDraftHire
	/// （契約金0G・初期装備・HP満タン）、通常は TryHire（契約金の引き落とし・名簿追加）。
	/// 押せる時点で人数・空き枠・所持金の検証は済んでいるが、念のため各APIの戻り値も見る。
	/// </summary>
	private void OnHirePressed()
	{
		if (_hireButton.Disabled) return;

		var selected = SelectedOffers(); // ドラフトでは TryDraftHire が _candidates から取り除くため、先に写しを取る
		int hiredCount = selected.Count(offer => _draft != null
			? _recruitmentSystem.TryDraftHire(_state, _draft, offer)
			: _recruitmentSystem.TryHire(_state, offer));

		if (hiredCount < selected.Count)
			GD.PushWarning($"[RecruitmentPopup] 採用できなかった候補がいます（{selected.Count}名中{hiredCount}名採用）。");

		FinishAndClose();
	}

	private void OnDeclinePressed() => FinishAndClose();

	private void FinishAndClose()
	{
		_decided = true;
		Hide();
	}

	/// <summary>
	/// Escape・外側クリックなど、ボタン以外の経路でポップアップが閉じようとした場合のフック。
	/// 意思決定（採用/見送る）が済んでいなければ、閉じさせずに即座に再表示する。
	///
	/// 文字列指定の CallDeferred(nameof(PopupCentered)) はエンジン側のメソッド名
	/// （"popup_centered"）で解決されるため「Method not found」で必ず失敗し、ポップアップが
	/// 消えたまま Closed も来ず「次週へ」が押せなくなっていた。ラムダを Callable にして呼ぶ。
	/// </summary>
	private void OnPopupHide()
	{
		if (!_decided)
		{
			Callable.From(() => PopupCentered(PopupSize)).CallDeferred();
			return;
		}

		Closed.Invoke();
	}
}
