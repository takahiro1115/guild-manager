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
/// 「採用する」は選択中の1名を雇用してポップアップを開いたままにする（複数採用可、→ 03 §2.4）。
/// 「見送る」は残り全員を見送ってポップアップを閉じる。
///
/// 第1週の新春ドラフト（→ OpenDraft、03 §2.4、2026年9月）も同じポップアップで行う：契約金0G・
/// 規定人数（2名）を採用し終えた時点で自動的に閉じる。「見送る」は出さない。
/// </summary>
public partial class RecruitmentPopup : PopupPanel
{
	private ItemList _candidateList = null!;
	private Button _hireButton = null!;
	private Button _declineButton = null!;
	private Label _statusLabel = null!;
	private Label _titleLabel = null!;
	private string _defaultTitle = "";

	private GameState _state = null!;
	private RecruitmentSystem _recruitmentSystem = null!;
	private List<RecruitmentOffer> _candidates = new();
	private bool _decided;

	/// <summary>新春ドラフト中（→ OpenDraft）なら非null。通常の新春採用試験ではnull。</summary>
	private RecruitmentDraft _draft;

	/// <summary>採用処理が完了してポップアップが閉じた（＝次週へ進めてよい）ことを通知する。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_candidateList = GetNode<ItemList>("%CandidateList");
		_hireButton = GetNode<Button>("%HireButton");
		_declineButton = GetNode<Button>("%DeclineButton");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_titleLabel = GetNode<Label>("%TitleLabel");
		_defaultTitle = _titleLabel.Text;

		_hireButton.Pressed += OnHirePressed;
		_declineButton.Pressed += OnDeclinePressed;
		PopupHide += OnPopupHide;
	}

	/// <summary>
	/// 第1週の新春ドラフト（→ 03 §2.4、RecruitmentSystem.StartInitialDraft）を開始する。契約金は無料、
	/// 規定人数（RecruitmentBalance.DraftHireCount）を採用し終えるまで閉じられない（「見送る」も出さない）。
	/// 採用した新人は職業ごとの初期装備を着て加入する（→ RecruitmentSystem.TryDraftHire）。
	/// </summary>
	public void OpenDraft(GameState state, RecruitmentSystem recruitmentSystem)
	{
		_state = state;
		_recruitmentSystem = recruitmentSystem;
		_draft = recruitmentSystem.StartInitialDraft(state, GetScoutMasterBonus());
		_candidates = _draft.Offers;
		_decided = false;
		ApplyMode();

		RefreshList();
		PopupCentered();
	}

	/// <summary>ドラフト中は見出しを差し替え、「見送る」を隠す。通常の採用試験では元に戻す。</summary>
	private void ApplyMode()
	{
		_declineButton.Visible = _draft == null;
		Title = _draft == null ? "新春採用試験" : "新春ドラフト";
		if (_draft == null)
			_titleLabel.Text = _defaultTitle;
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
		_state = state;
		_recruitmentSystem = recruitmentSystem;
		_candidates = recruitmentSystem.GenerateCandidates(state, GetScoutMasterBonus(), candidateCount);
		_decided = false;
		_draft = null;
		ApplyMode();

		RefreshList();
		PopupCentered();
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
		_state = state;
		_recruitmentSystem = recruitmentSystem;
		_candidates = new List<RecruitmentOffer>(offers);
		_decided = false;
		_draft = null;
		ApplyMode();

		RefreshList();
		PopupCentered();
	}

	/// <summary>
	/// スカウト顧問が任命されていれば、そのボーナスを返す（未任命なら0。→ 03 §7.3）。
	/// v1.4改訂：冒険者支援室がLv0（未建設）の間はボーナスを発生させない（防御的チェック。
	/// 通常はAdvisorSystem.TryAssignScoutMaster側のガードにより、Lv0のままAssignedScoutMasterが
	/// 設定されることは無い）。
	/// </summary>
	private double GetScoutMasterBonus()
	{
		if (_state.GetFacilityLevel(FacilityType.RecruitmentOffice) < 1) return 0;
		if (_state.AssignedScoutMaster == null) return 0;

		var scoutMaster = _state.RetiredAdventurers.FirstOrDefault(a => a.Id == _state.AssignedScoutMaster.Value);
		return scoutMaster == null ? 0 : AdvisorSystem.GetScoutMasterBonus(scoutMaster);
	}

	private void RefreshList()
	{
		_candidateList.Clear();
		foreach (var offer in _candidates)
		{
			var c = offer.Candidate;
			if (_draft != null)
			{
				// ドラフトは契約金0G。加入時に着る初期装備（→ StarterEquipment）も添えて選びやすくする。
				var (weaponId, armorId) = StarterEquipment.GetLoadout(c.JobClass);
				_candidateList.AddItem(
					$"{c.Name}（{c.JobClass}）{c.Age}歳 総合PA{c.TotalPA:F0} 契約金 0 G 週給{c.WeeklyWage}G " +
					$"初期装備: {ItemCatalog.FindById(weaponId)?.Name}＋{ItemCatalog.FindById(armorId)?.Name}");
				continue;
			}
			_candidateList.AddItem(
				$"{c.Name}（{c.JobClass}）{c.Age}歳 総合PA{c.TotalPA:F0} 契約金{offer.SigningBonus}G 週給{c.WeeklyWage}G");
		}

		if (_draft != null)
			_titleLabel.Text = $"【新春ドラフト】契約金無料：新人を{RecruitmentBalance.DraftHireCount}名採用してください（残り{_draft.HiresRemaining}名）";

		_statusLabel.Text = $"現役枠の空き: {_recruitmentSystem.GetOpenSlotCount(_state)}　所持金: {_state.Gold} G";
	}

	private void OnHirePressed()
	{
		var selected = _candidateList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "採用する候補者を選択してください。";
			return;
		}

		var offer = _candidates[selected[0]];

		if (_recruitmentSystem.GetOpenSlotCount(_state) <= 0)
		{
			_statusLabel.Text = "現役枠がいっぱいで採用できません（→ 03 §6：施設拡張は未実装）。";
			if (_draft != null)
				FinishAndClose(); // ドラフト中に枠が尽きたら、それ以上は選びようがないので締める（閉じられなくなるのを防ぐ）
			return;
		}
		if (_draft != null)
		{
			// ドラフトは契約金0G：所持金は見ない（資金不足で採用できない状態を作らない）。
			_recruitmentSystem.TryDraftHire(_state, _draft, offer);
			RefreshList();
			if (_draft.IsComplete)
				FinishAndClose();
			return;
		}

		if (_state.Gold < offer.SigningBonus)
		{
			_statusLabel.Text = $"契約金 {offer.SigningBonus}G が足りません（所持金 {_state.Gold}G）。";
			return;
		}

		_recruitmentSystem.TryHire(_state, offer);
		_candidates.RemoveAt(selected[0]);
		RefreshList();

		// 候補者を使い切った、または枠が埋まったら自動的に締める（それ以上は選びようがないため）。
		if (_candidates.Count == 0 || _recruitmentSystem.GetOpenSlotCount(_state) <= 0)
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
			Callable.From(() => PopupCentered()).CallDeferred();
			return;
		}

		Closed.Invoke();
	}
}
