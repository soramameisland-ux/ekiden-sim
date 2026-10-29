using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.Views;

namespace Ekiden.Domain.Game
{
    /// <summary>次の大会の調子の見込み(08 R-05 ⑥)の読み取り API。状態は変えない(<see cref="Forecaster.Run"/> は選手を複製して進める)。</summary>
    public sealed partial class GameSession
    {
        public const int OutlookWeeksMax = 8;                      // 大会が何週以内のとき見込みを出すか(R-05 ⑥)

        /// <summary>練習の安全網の閾値(設定の <c>auto_safety_f</c>。無効のときも既定値を返す = 無効かどうかは別に見る)。</summary>
        private int SafetyF { get { int f = State.Settings.AutoSafetyF; return f > 0 ? f : TrainingMaster.K_SAFETY_F; } }

        /// <summary>登録の見込み: 登録のある大会はその登録、まだなら(締切の前)おまかせ登録。1 回の画面の組み立てのあいだ使い回す。</summary>
        private sealed class EntryOutlook
        {
            private readonly Func<RaceKind, List<int>> _auto;
            private readonly TeamState _s;
            private readonly HashSet<int>[] _sets = new HashSet<int>[3];

            public EntryOutlook(TeamState s, Func<RaceKind, List<int>> auto) { _s = s; _auto = auto; }

            public bool Entered(int kind, int runnerId)
            {
                if (_sets[kind] == null) _sets[kind] = new HashSet<int>(_s.Entries[kind].Count > 0 ? _s.Entries[kind] : _auto((RaceKind)kind));
                return _sets[kind].Contains(runnerId);
            }
        }

        private EntryOutlook NewEntryOutlook() { return new EntryOutlook(State, k => TeamRacing.AutoRegister(State, k)); }

        /// <summary>次の大会が 8 週以内にあり自校に出場権があるとき、その大会と大会の週までの週数。</summary>
        private bool OutlookTarget(out RaceKind kind, out int weeksAhead)
        {
            int yw = Yw, weeks, k1;
            Calendar.NextRace(yw, out weeks, out k1);
            kind = (RaceKind)(k1 - 1);
            weeksAhead = Calendar.RaceWeeks[k1 - 1] - yw;
            return weeksAhead >= 0 && weeksAhead <= OutlookWeeksMax && PrepareYear().Entrants(kind).Contains(School.Id);
        }

        /// <summary>選手の、大会 <paramref name="kind"/> の週の調子の見込み。大会の週をすぎた・8 週より先・出場権がない大会では null。</summary>
        public FormOutlookView FormOutlook(int runnerId, RaceKind kind)
        {
            Runner r = State.FindRunner(runnerId);
            if (r == null) return null;
            return OutlookOf(r, kind, null, NewEntryOutlook());          // 登録のある大会はその登録(調整あり)、なければおまかせ登録の見込み
        }

        /// <summary>
        /// <paramref name="currentMonth"/> が null でなければ、今月の残りの週をそのメニュー(4 週ぶん)で置き換えて見込む(月間計画のテーマの比較用)。
        /// 今月の週は 個別指導 &gt; 調整 &gt; 月間計画、来月以降は 個別指導 &gt; 調整 &gt; 推奨プラン(未設定の月)。
        /// <paramref name="assumeEntered"/> = この大会に登録した場合の見込み(エントリーの画面で、登録を決める材料)。
        /// </summary>
        private FormOutlookView OutlookOf(Runner r, RaceKind kind, Menu[] currentMonth, EntryOutlook entries, bool assumeEntered = false)
        {
            TeamState s = State;
            int k = (int)kind, yw = Yw, t = Calendar.RaceWeeks[k];
            if (t < yw || t - yw > OutlookWeeksMax) return null;
            if (!PrepareYear().Entrants(kind).Contains(School.Id)) return null;
            var v = new FormOutlookView { Kind = kind, WeeksAhead = t - yw, Injured = r.IsInjured };
            if (t == yw && _weekResolved)                                       // 大会の週の練習はもう解決された: 調子は確定している
            {
                v.Determined = true;
                v.Form[r.Form + 2] = 1.0;
                v.PGood = !r.IsInjured && r.Form >= 1 ? 1.0 : 0.0;
                v.FatigueAtRace = r.Fatigue;
                return v;
            }
            var planned = new List<PlannedWeek>();
            for (int w = yw; w <= t; w++)
            {
                int off = w - yw, month = w / 4, wk = w % 4;
                string theme = month == yw / 4 ? s.MonthlyPlan[r.Group] : PlanRules.AutoPlan(s.Plan.Focus, month);
                Menu menu;
                if (month == yw / 4 && currentMonth != null) menu = currentMonth[wk];
                else if (month == yw / 4 && theme == "th.custom" && s.CustomWeeks != null) { MenuSpec c = s.CustomWeeks[r.Group][wk]; menu = new Menu(c.Key1, c.Key2, c.Mileage); }
                else menu = Master.Themes[theme][wk];
                bool individual = false;
                IndividualMenu im;
                if (r.IsInjured) menu = Master.Injured;
                else if (s.IndividualMenus.TryGetValue(r.Id, out im) && im.UntilAbsWeek > s.AbsWeek + off) { menu = new Menu(im.Menu.Key1, im.Menu.Key2, im.Menu.Mileage); individual = true; }
                else
                {
                    int taper = TeamWeek.TaperIndex(s, w, kk => (assumeEntered && kk == k) || entries.Entered(kk, r.Id));
                    if (taper >= 0) { menu = Master.Taper[taper]; v.Tapered = true; }
                }
                planned.Add(new PlannedWeek { Menu = menu, MonthIndex = month, Camp = theme == "th.camp", Individual = individual });       // 合宿の印は 03 §5.1 の MenuFor と同じ(テーマだけで決まる)
            }
            ForecastResult fr = Forecaster.Run(Master, r, planned, s.Fac, s.Settings.AutoSafetyF > 0, r.BondAvg, s.ModsFor(r), SafetyF);
            v.Form = fr.Form != null ? fr.Form : new double[5];
            if (fr.End != null && fr.End.IsInjured) v.Form = new[] { 1.0, 0.0, 0.0, 0.0, 0.0 };         // 大会の週にまだ故障中なら、調子は −2(03 §6。軽傷は出られる / 中傷以上は出られない)
            v.PInjury = fr.PInjury;
            v.PGood = fr.Form == null ? 0.0 : (1.0 - fr.PInjury) * (v.Form[3] + v.Form[4]);
            v.FatigueAtRace = fr.End.Fatigue;
            return v;
        }
    }
}
