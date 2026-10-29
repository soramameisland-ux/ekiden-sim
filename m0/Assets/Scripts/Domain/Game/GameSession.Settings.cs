using System;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Game
{
    /// <summary>設定(02 <c>player_team.settings</c>)を変える入口(08 R-13。SC-04・SC-40)。画面が <c>State.Settings</c> を直接書かないようにする。
    /// 設定は保存される(<c>GameSnapshot</c>)。どれも決定性には影響しない(レース・育成の結果は変えず、止まる場所や自動で行うことを変える)。</summary>
    public sealed partial class GameSession
    {
        /// <summary>観戦モード(04 §9.3)。Full = 当日で止まって指示・檄を出す / Digest = 要約 / ResultOnly = 結果だけ。</summary>
        public static readonly string[] SpectateModes = { "Full", "Digest", "ResultOnly" };

        /// <summary>安全網の閾値(03 §5。75 / 85 / 0 = 無効)。</summary>
        public static readonly int[] AutoSafetyChoices = { 75, 85, 0 };

        public OpResult SetSpectateMode(string mode)
        {
            if (Array.IndexOf(SpectateModes, mode) < 0) return OpResult.Fail(OpErr.SettingInvalid, "spectate_mode");
            State.Settings.SpectateMode = mode;
            return OpResult.Success;
        }

        /// <summary>月間計画のおまかせ(<c>plan_auto</c>)。オンにしたときは、今月の推奨プランをすぐ引き直す。オフにしたときは今の計画のまま。</summary>
        public OpResult SetPlanAuto(bool on)
        {
            State.Settings.PlanAuto = on;
            return on ? ApplyRecommendedPlan() : OpResult.Success;
        }

        public OpResult SetEventAuto(bool on) { State.Settings.EventAuto = on; return OpResult.Success; }

        public OpResult SetScoutAuto(bool on) { State.Settings.ScoutAuto = on; return OpResult.Success; }

        public OpResult SetAutoSafety(int f)
        {
            if (Array.IndexOf(AutoSafetyChoices, f) < 0) return OpResult.Fail(OpErr.SettingInvalid, "auto_safety_f");
            State.Settings.AutoSafetyF = f;
            return OpResult.Success;
        }
    }
}
