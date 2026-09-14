using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ギルドマスターの後方支援（→ コアシステム刷新仕様「(3) ギルドマスター後方支援機能」）。
    ///
    /// 本作は完全間接マネジメントであり、プレイヤーは戦闘中に直接指揮を執らない。
    /// その代わり「送り出した後にギルド側からできること」を2つだけ用意し、
    /// 出撃の結末に対して介入の余地を残している：
    ///  - 緊急撤退：報酬を捨てて部隊を無傷のまま引き上げさせる。
    ///  - 緊急回復：資材（現状は資金）を消費して部隊のHPを回復し、そのまま続行させる。
    ///
    /// 介入のタイミング：
    /// 本エンジンは「満了週にQuestResolverで一括判定」する設計（→ 03 §4.0.1）のため、
    /// 介入は**解決前**（派遣中の週）に行う。ボス戦のログをステップ再生している最中の
    /// 介入演出は、UI（Godot）側が再生を一時停止してこれらを呼ぶ形で表現する想定。
    /// </summary>
    public class EmergencySupportSystem
    {
        /// <summary>
        /// 緊急撤退。派遣中の部隊を即時帰還させ、クエストを未解決のまま打ち切る。
        /// 判定自体が行われないため、負傷・戦死は一切発生せず、報酬も得られない
        /// （＝「危なくなったら手ぶらで引き上げる」という選択肢）。
        ///
        /// クエストは受注可能一覧へは戻さない（引き受けた依頼を放棄した扱い）。
        /// 名声・脅威度への影響はこのメソッドでは行わない（呼び出し側が必要に応じて
        /// GuildRankSystem・SecuritySystemへ通知する。→ 責務の分離）。
        /// </summary>
        /// <returns>撤退できたらtrue。対象の派遣が存在しなければfalse。</returns>
        public bool TryEmergencyRetreat(GameState state, ActiveDispatch dispatch)
        {
            if (!state.ActiveDispatches.Remove(dispatch))
                return false;

            foreach (var member in dispatch.Party.Members)
                member.IsDispatched = false;

            // 携行アイテムは使わずに持ち帰る（解決していない＝消費していないため）。
            return true;
        }

        /// <summary>
        /// 緊急回復。ギルドの備蓄（現状は資金。→ EmergencyBalance.EmergencyHealCostGold）を
        /// 消費し、対象部隊全員のHPを最大HPの一定割合だけ回復して判定を続行させる。
        /// 1出撃につき1回のみ（→ ActiveDispatch.EmergencyHealUsed）。
        /// </summary>
        /// <returns>
        /// 回復を適用できたらtrue。既に使用済み、資金不足、または対象の派遣が
        /// 存在しない場合は何もせずfalse（Try*系の共通パターン）。
        /// </returns>
        public bool TryEmergencyHeal(GameState state, ActiveDispatch dispatch)
        {
            if (!state.ActiveDispatches.Contains(dispatch))
                return false;
            if (dispatch.EmergencyHealUsed)
                return false;
            if (state.Gold < EmergencyBalance.EmergencyHealCostGold)
                return false;

            state.Gold -= EmergencyBalance.EmergencyHealCostGold;
            dispatch.EmergencyHealUsed = true;

            foreach (var member in dispatch.Party.Members)
            {
                int healAmount = (int)Math.Round(member.MaxHP * EmergencyBalance.EmergencyHealRatio);
                member.CurrentHP = Math.Min(member.MaxHP, member.CurrentHP + healAmount);
            }

            return true;
        }
    }
}
