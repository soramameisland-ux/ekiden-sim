using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Game
{
    /// <summary>プレイヤーの操作(画面が呼ぶ入口)。状態(<see cref="TeamState"/>)を書き換える前に、規則(03・04・05・07・08)で検証して、無効なら理由を返す(08 F4)。
    /// 操作は『週の境目』(週の最初の処理が済んだ状態)から、その週が解決される前に行う。</summary>
    public sealed partial class GameSession
    {
        private readonly HashSet<int> _guidedThisWeek = new HashSet<int>();      // 今週、個別指導のメニューを指定した選手(週が解決される前なら取り消せてポイントが戻る。R-06)

        private int Yw { get { return TeamWeek.YearWeek(State); } }

        private int MonthIndex { get { return Yw / 4; } }

        /// <summary>週が解決されたあとに呼ぶ(今週の指定の取り消し権を閉じる)。</summary>
        private void CloseWeekEdits() { _guidedThisWeek.Clear(); }

        // ------------------------------------------------------------ 年間方針・月間計画・グループ(R-04)
        /// <summary>年間方針(重点・注目大会・ピーキング)。<c>plan_auto</c> のときは、今月の推奨プランを新しい重点で引き直す。</summary>
        public OpResult SetAnnualPlan(PlanFocus focus, int targetRace, IReadOnlyList<PeakingMode> peaking)
        {
            if (targetRace < 0 || targetRace > 2) return OpResult.Fail(OpErr.TargetRace);
            if (peaking == null || peaking.Count != AnnualPlan.PeakingKinds) return OpResult.Fail(OpErr.PeakingCount);
            State.Plan.Focus = focus;
            State.Plan.TargetRace = targetRace;
            for (int i = 0; i < AnnualPlan.PeakingKinds; i++) State.Plan.Peaking[i] = peaking[i];
            if (State.Settings.PlanAuto) ApplyRecommendedPlan();
            return OpResult.Success;
        }

        /// <summary>今月のテーマを、年間方針の推奨プラン(全グループ共通)にそろえる。</summary>
        public OpResult ApplyRecommendedPlan()
        {
            string theme = PlanRules.AutoPlan(State.Plan.Focus, MonthIndex);
            for (int g = 0; g < 3; g++) State.MonthlyPlan[g] = theme;
            return OpResult.Success;
        }

        /// <summary>グループ 0=A / 1=B / 2=C の今月のテーマ。<c>th.custom</c> は <see cref="SetCustomWeeks"/> で週ごとのメニューを決めてから。</summary>
        public OpResult SetMonthlyPlan(int group, string theme)
        {
            if (group < 0 || group > 2) return OpResult.Fail(OpErr.GroupRange);
            if (theme == "th.custom")
            {
                if (State.CustomWeeks == null || State.CustomWeeks[group] == null) return OpResult.Fail(OpErr.CustomFirst);
            }
            else if (!Master.Themes.ContainsKey(theme)) return OpResult.Fail(OpErr.ThemeUnknown, theme);
            State.MonthlyPlan[group] = theme;
            return OpResult.Success;
        }

        /// <summary>自由編集(<c>th.custom</c>): 4 週ぶんの (ポイント練習 2 つ + 走行量)。03 §5.5 の検証を通す。</summary>
        public OpResult SetCustomWeeks(int group, IReadOnlyList<MenuSpec> weeks)
        {
            if (group < 0 || group > 2) return OpResult.Fail(OpErr.GroupRange);
            if (weeks == null || weeks.Count != 4) return OpResult.Fail(OpErr.WeeksFour);
            for (int i = 0; i < 4; i++)
            {
                List<Msg> errs = PlanRules.ValidateMenu(Master, weeks[i].Key1, weeks[i].Key2, weeks[i].Mileage);
                if (errs.Count > 0) return OpResult.Fail(OpErr.CustomWeek, i + 1, errs[0]);
            }
            if (State.CustomWeeks == null) State.CustomWeeks = new MenuSpec[3][];
            State.CustomWeeks[group] = weeks.Select(w => new MenuSpec(w.Key1, w.Key2, w.Mileage)).ToArray();
            State.MonthlyPlan[group] = "th.custom";
            return OpResult.Success;
        }

        /// <summary>推奨の編成を適用(力の高い順に A 10 人・B 10 人・残りは C)。年度初めの 4 月だけ。</summary>
        public OpResult ApplyRecommendedGroups()
        {
            if (MonthIndex != 0) return OpResult.Fail(OpErr.RecommendApril);
            State.AssignRecommendedGroups();
            State.Refresh(assignGroups: false);
            return OpResult.Success;
        }

        /// <summary>選手のグループを変える(別グループの組の絆は月末に減る)。</summary>
        public OpResult MoveToGroup(int runnerId, int group)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) return OpResult.Fail(OpErr.RunnerNone);
            if (group < 0 || group > 2) return OpResult.Fail(OpErr.GroupRange);
            r.Group = group;
            State.Refresh(assignGroups: false);
            return OpResult.Success;
        }

        // ------------------------------------------------------------ 個別指導(R-06)
        /// <summary>メニューの指定(1 ポイント / 人。今週から月末まで)。調整メニューの上書き・安全網の対象外になるときは確認を求める(<paramref name="confirmed"/> で再度呼ぶ)。
        /// 今週すでに指定した選手への指定は、前の指定を取り消してポイントを戻してから行う。</summary>
        public OpResult GuideMenu(int runnerId, MenuSpec menu, bool confirmed = false)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) return OpResult.Fail(OpErr.RunnerNone);
            bool again = _guidedThisWeek.Contains(runnerId) && State.IndividualMenus.ContainsKey(runnerId);
            int points = State.GuidanceLeft + (again ? 1 : 0);
            List<Msg> errs = PlanRules.ValidateMenu(Master, menu.Key1, menu.Key2, menu.Mileage, r.IsInjured);
            if (errs.Count > 0) return OpResult.Fail(errs[0]);
            KeyValuePair<bool, Msg> can = PlanRules.CanGuide(Master, points, "menu", r.IsInjured, false, Tuple.Create(menu.Key1, menu.Key2, menu.Mileage));
            if (!can.Key) return OpResult.Fail(can.Value);
            var warn = new List<Msg>();
            if (TeamWeek.TaperIndex(State, r, Yw) >= 0) warn.Add(OpErr.WarnOverrideTaper.Make());
            if (State.Settings.AutoSafetyF > 0 && r.Fatigue >= State.Settings.AutoSafetyF) warn.Add(OpErr.WarnNoSafety.Make());
            if (warn.Count > 0 && !confirmed) return OpResult.Confirm(warn);
            if (again) State.GuidanceLeft++;
            State.GuidanceLeft--;
            State.IndividualMenus[runnerId] = new IndividualMenu { UntilAbsWeek = State.AbsWeek - Yw % 4 + 4, Menu = new MenuSpec(menu.Key1, menu.Key2, menu.Mileage) };
            _guidedThisWeek.Add(runnerId);
            return OpResult.Success;
        }

        /// <summary>メニューの指定を取り消す。今週の指定(まだ解決されていない)ならポイントが戻る。</summary>
        public OpResult CancelGuide(int runnerId)
        {
            if (!State.IndividualMenus.Remove(runnerId)) return OpResult.Fail(OpErr.GuideNotSet);
            if (_guidedThisWeek.Remove(runnerId)) State.GuidanceLeft++;
            return OpResult.Success;
        }

        /// <summary>面談(1 ポイント / 人。やる気 +1。1 人につき月 1 回)。</summary>
        public OpResult Talk(int runnerId)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) return OpResult.Fail(OpErr.RunnerNone);
            KeyValuePair<bool, Msg> can = PlanRules.CanGuide(Master, State.GuidanceLeft, "talk", r.IsInjured, State.Talked.Contains(runnerId));
            if (!can.Key) return OpResult.Fail(can.Value);
            r.Motivation = Math.Min(5, r.Motivation + 1);
            State.Talked.Add(runnerId);
            State.GuidanceLeft--;
            return OpResult.Success;
        }

        // ------------------------------------------------------------ 登録(04 §4。SC-21)
        /// <summary>大会の登録メンバー。締切(大会の 4 週前)から大会の週まで、何度でも直せる(故障した登録メンバーの差し替えも同じ)。
        /// 出走人数に足りないと棄権になるので確認を求める。</summary>
        public OpResult SetEntries(RaceKind kind, IReadOnlyList<int> ids, bool confirmed = false)
        {
            int k = (int)kind;
            RaceRule rule = RaceTables.Rule(kind);
            int yw = Yw;
            if (yw < TeamWeek.EntryDeadlineWeeks[k] || yw > Calendar.RaceWeeks[k]) return OpResult.Fail(OpErr.EntryWindow);
            if (ids.Count > rule.Entry) return OpResult.Fail(OpErr.EntryMax, rule.Entry);
            if (ids.Distinct().Count() != ids.Count) return OpResult.Fail(OpErr.EntryDup);
            int intl = 0;
            foreach (int id in ids)
            {
                Runner r = State.FindRunner(id);
                if (r == null) return OpResult.Fail(OpErr.EntryUnknown);
                if (!TeamRacing.CanRegister(r)) return OpResult.Fail(OpErr.EntrySevere);
                if (r.Overseas) intl++;
            }
            if (intl > rule.IntlReg) return OpResult.Fail(OpErr.EntryIntl, rule.IntlReg);
            if (ids.Count < rule.Run && !confirmed) return OpResult.Confirm(new[] { OpErr.WarnEntryShort.Make(rule.Run) });
            State.Entries[k].Clear();
            State.Entries[k].AddRange(ids);
            DropStaleOrders();
            return OpResult.Success;
        }

        /// <summary>おまかせ登録(04 §4.2)を適用する。</summary>
        public OpResult AutoEntries(RaceKind kind)
        {
            int k = (int)kind, yw = Yw;
            if (yw < TeamWeek.EntryDeadlineWeeks[k] || yw > Calendar.RaceWeeks[k]) return OpResult.Fail(OpErr.EntryWindow);
            State.Entries[k].Clear();
            State.Entries[k].AddRange(TeamRacing.AutoRegister(State, kind));
            DropStaleOrders();
            return OpResult.Success;
        }

        // ------------------------------------------------------------ 特別招聘(07 §3.3)
        /// <summary>特別招聘券の使用の宣言(取り消せない。入学は年度末)。スーパールーキーの出身県は <paramref name="pref"/>(0 = おまかせ)。</summary>
        public OpResult DeclareTicket(TicketKind kind, int pref = 0)
        {
            TicketState t = State.Tickets;
            int year = Year, month = MonthIndex;
            int active = State.Runners.Count(r => r.RecruitSource == RecruitSource.Ticket);
            if (t.Pending != null) return OpResult.Fail(OpErr.TicketPending);
            if (t.LastUsedYear == year) return OpResult.Fail(OpErr.TicketYearDone);
            if (t.Stock <= 0) return OpResult.Fail(OpErr.TicketNone);
            if (active + 1 > TicketK.K_TICKET_ACTIVE_MAX) return OpResult.Fail(OpErr.TicketActiveMax, TicketK.K_TICKET_ACTIVE_MAX);
            if (month < TicketK.K_TICKET_DECLARE_FROM || month > TicketK.K_TICKET_DECLARE_TO) return OpResult.Fail(OpErr.TicketMonth);
            if (pref < 0 || pref > 47 || (kind == TicketKind.Intl && pref != 0)) return OpResult.Fail(OpErr.TicketPref);
            t.Declare(year, kind, pref);
            return OpResult.Success;
        }

        // ------------------------------------------------------------ 施設・スタッフ(R-11)
        /// <summary>施設・スタッフを 1 つ強化する(資金が足りるとき。上限は監督ランクで決まる)。key = <see cref="FacilityPolicy.All"/> のどれか。新設の施設・追加のスタッフ(<see cref="FacilityPolicy.Extras"/>。Lv0 → 1)は、監督ランク <see cref="WorldK.NewMinRank"/> 以上で置ける。</summary>
        public OpResult UpgradeFacility(string key)
        {
            if (Array.IndexOf(FacilityPolicy.All, key) < 0) return OpResult.Fail(OpErr.FacilityUnknown, key);
            FacilityLevels f = State.Fac;
            int lv = FacilityPolicy.Level(f, key);
            int rank = School.Rank();
            if (lv == 0 && rank < WorldK.NewMinRank) return OpResult.Fail(OpErr.FacilityRank, WorldK.NewMinRank);
            int cap = WorldMath.FacilityCap(rank);
            if (lv >= cap) return OpResult.Fail(OpErr.FacilityCap, cap);
            double cost = WorldMath.UpgradeCost(lv);
            if (State.Funds < cost) return OpResult.Fail(OpErr.FacilityFunds, cost.ToString("F0"));
            FacilityPolicy.SetLevel(ref f, key, lv + 1);
            if (key == "manager") State.GuidanceLeft += WorldMath.GuidancePoints(lv + 1) - WorldMath.GuidancePoints(lv);       // 月の途中で主務が上がったら、増えた分をすぐ使える
            State.Fac = f;
            State.Funds = Rounding.HalfUp(State.Funds - cost);
            SyncSchool();
            return OpResult.Success;
        }

        /// <summary>推奨の強化をすべて実行する(寮 → スカウト → コーチ → … の順に、上限まで。予備費 500 万円を残す)。戻り値は強化した件数。</summary>
        public int UpgradeRecommended()
        {
            FacilityLevels f = State.Fac;
            double funds = State.Funds;
            int before = LevelSum(f);
            FacilityPolicy.Upgrade(ref f, ref funds, School.Rank());
            State.Fac = f;
            State.Funds = Rounding.HalfUp(funds);
            SyncSchool();
            return LevelSum(f) - before;
        }

        private static int LevelSum(FacilityLevels f) { return f.Coach + f.Trainer + f.Nutri + f.Dorm + f.Track + f.Hill + f.Gym + f.Medical + f.Scout + f.Altitude + f.Mental + f.Camp + f.Manager; }
    }
}
