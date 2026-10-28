using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;
using Calendar = Ekiden.Domain.Team.Calendar;

namespace Ekiden.Domain.Game
{
    /// <summary>止まる場所の種類(08 §6.1・§6.3)。</summary>
    public enum StopKind { AnnualStart, MonthPlan, Entry, QualifierEntry, Order, RaceDayPrep, RaceDay, Event, Injury, Warn, MonthReport, YearEndSettle, YearEndTurnover }

    /// <summary>画面に見せる 1 つの停止。種類に応じて必要な項目だけが入る。</summary>
    public sealed class Stop
    {
        public StopKind Kind;
        public string Key;                              // 08 §6.3 の名前(例: <c>Entry:Fall</c>)
        public RaceKind? Race;                          // Entry / Order / RaceDay
        public bool Qualifier;                          // RaceDay が予選会
        public int RunnerId;                            // Injury
        public FiredEvent Event;                        // Event(選択肢は Choices)
        public IReadOnlyList<ChoiceDef> Choices;
        public RaceDay RaceDay;                         // RaceDayPrep: 当日(SC-24)。当日変更・指示を決め、<c>Start</c> 以降でレース進行(SC-25)。『続ける』で残りは決定のまま走る
        public WeekOutcome Week;                        // RaceDay・Event・Injury・Warn・MonthReport・YearEnd*: その週の結果
        public MonthReport Report;                      // MonthReport
        public YearResult Year;                         // YearEndSettle / YearEndTurnover: その年度の大会の結果
        public YearTurnover Turnover;                   // YearEndTurnover: 卒業と退部
        public IReadOnlyList<int> IntakeIds;            // YearEndTurnover: 入学者

        public override string ToString() { return Key; }
    }

    /// <summary>月末レポート(08 R-10)。項目の選び方は <see cref="FlowRules.SelectReportItems"/>(優先度の高い順、同じ種別は最大 3 件、全体で 7 件)。</summary>
    public sealed class MonthReport
    {
        public int MonthIndex;
        public List<WeekOutcome> Weeks = new List<WeekOutcome>();
        public List<FlowRules.ReportItem> Items = new List<FlowRules.ReportItem>();      // 表示する項目
        public int More;                                                                   // 『ほか n 件』
        public List<string> Deferred = new List<string>();                                  // 止まらずにここへ回ったお知らせ(§6.3)
        public double Morale;
    }

    /// <summary>
    /// 進行制御(08 §6 の FlowController): 『続ける』(<see cref="Advance"/>)が唯一の前進の操作。止まる場所(年度初め・月間計画・登録・オーダー・当日・イベントの選択・故障・疲労の警告・
    /// 月末レポート・年度末)は <see cref="FlowRules"/> の決定表のとおり。止まったところで、画面は <see cref="GameSession"/> の操作(<c>SetMonthlyPlan</c> <c>SetEntries</c> <c>GuideMenu</c> <c>UpgradeFacility</c> など)を呼び、
    /// 『続ける』で次へ進む。おまかせ(<c>plan_auto</c> <c>event_auto</c>)に従い、止まらない場所は飛ばす。
    /// 保存できるのは <see cref="CanSave"/> のとき(週の解決の前・年度末の精算のあと)。復元した <see cref="GameSession"/> で新しく作れば、その週の最初の停止から再開する。
    /// </summary>
    public sealed class GameFlow
    {
        public const int WarnFatigue = 70, WarnFatigueCamp = 80, WarnFatigueMax = 85;      // K_UI_WARN_F / K_UI_WARN_F_CAMP / 安全網の閾値
        private enum Phase { Start, Before, Race, After, Month, Settle, Turnover }

        private static readonly string[] RaceNames = { "Fall", "National", "NewYear" };

        public GameSession Session { get; }
        public Stop Current { get; private set; }

        /// <summary>週の練習が解決される直前(オーダー確定後の自動セーブの時点。08 §6.5)。ここで状態を取っておくと、練習の解決のあとの週の途中(当日・結果・イベント・月末レポート)での
        /// 中断セーブの『本体』になる(<see cref="MidWeek"/>・<see cref="SuspendRace"/>・<see cref="ResumeWeek"/>・<see cref="ResumeRace"/>。09 §4.6)。再開した週では出ない。</summary>
        public event Action<GameFlow> ResolvingWeek;

        private readonly Queue<Stop> _queue = new Queue<Stop>();
        private Phase _phase = Phase.Start;
        private WeekOutcome _week;
        private EventSelection _sel;
        private readonly Dictionary<string, string> _chosen = new Dictionary<string, string>();       // イベント ID → 選んだ選択肢
        private List<string> _deferred = new List<string>();
        private readonly List<WeekOutcome> _monthWeeks = new List<WeekOutcome>();
        private readonly List<string> _monthDeferred = new List<string>();
        private HashSet<int> _monthStartInjured = new HashSet<int>();
        private int _warnedMonth = -1;
        private bool _resuming;                             // 中断セーブからの再開: 週の初めの停止は出さず、練習の解決の直前の通知(ResolvingWeek)も出さない
        private RaceDayRecord _resume;                      // 練習の解決のあと、この記録を当日に戻す(<see cref="ResumeRace"/>)
        private RaceDayRecord _dayRecord;                   // 今週の大会が走ったあとの当日の記録(終わったあとの中断のため)

        /// <param name="autoUpgrade">年度末の精算で推奨の強化を自動で行う(検算用。画面のときは false = 強化は画面の操作)。</param>
        /// <param name="autoTickets">招聘の宣言をおまかせで行う(検算用)。</param>
        public GameFlow(GameSession session, bool autoUpgrade = false, bool autoTickets = false)
        {
            Session = session;
            session.AutoUpgrade = autoUpgrade;
            session.AutoDeclareTickets = autoTickets;
            if (session.IsYearSettled)
            {
                _phase = Phase.Settle;
                _week = new WeekOutcome { AbsWeek = session.State.AbsWeek };                // 復元したあとは、その週の結果(練習・イベント)は持たない
                _queue.Enqueue(new Stop { Kind = StopKind.YearEndSettle, Key = "YearEnd:Settle", Week = _week, Year = session.YearInProgress.Result });
            }
        }

        private TeamState S { get { return Session.State; } }

        /// <summary>いま保存してよいか(週の解決の前、年度末の精算のあと、入学者発表のあと)。復元して <see cref="GameFlow"/> を作り直すと、その週の最初の停止から再開する。</summary>
        public bool CanSave { get { return _phase == Phase.Start || _phase == Phase.Before || _phase == Phase.Settle || _phase == Phase.Turnover; } }      // Race(当日)は練習が解決されたあとなので保存しない

        /// <summary>練習が解決されたあとの週の途中か(当日・結果・イベント・月末レポートの停止)。このとき普通のセーブはできず(<see cref="CanSave"/> が false)、中断は <see cref="ResolvingWeek"/> で取った状態に戻る。</summary>
        public bool MidWeek { get { return _phase == Phase.Race || _phase == Phase.After || _phase == Phase.Month; } }

        /// <summary>いま中断してよい『大会の週の途中』の当日の記録(09 §4.6 の <c>suspend.race</c>)。当日の画面・レース進行中・走ったあとの結果とイベントの画面のとき。なければ null
        /// (そのときの中断は、週の初めの状態(<see cref="CanSave"/>)か、直近の自動セーブに戻る)。本体は <see cref="ResolvingWeek"/> で取った状態。</summary>
        public RaceDayRecord SuspendRace
        {
            get
            {
                if (_phase == Phase.Race && Session.CurrentRaceDay != null) return Session.CurrentRaceDay.Capture();
                if ((_phase == Phase.After || _phase == Phase.Month) && _dayRecord != null) return _dayRecord;
                return null;
            }
        }

        /// <summary>中断セーブから再開する(大会の週の途中でなかったとき)。<see cref="ResolvingWeek"/> で取った状態から復元した <see cref="GameSession"/> で作った新しい流れに、最初の『続ける』の前に呼ぶ。
        /// 週の初めの停止は出さず(中断の前に済んでいる)、練習を同じ結果で解決し直して、その週の結果の停止から続ける(その週に選んだイベントの選択肢は選び直す)。</summary>
        public OpResult ResumeWeek()
        {
            if (_phase != Phase.Start || Current != null || _queue.Count > 0) return OpResult.Fail("再開できるのは、復元したあとの最初の『続ける』の前だけ");
            _resuming = true;
            return OpResult.Success;
        }

        /// <summary>中断セーブから再開する(大会の当日・レース進行中・走ったあと)。<see cref="ResumeWeek"/> に加えて、<paramref name="record"/> の決定を当日に戻して、中断した区間の手前の当日の停止から続ける。
        /// 走り終わったあとの記録なら、当日の画面は出さず、結果とイベントの停止から続ける。</summary>
        public OpResult ResumeRace(RaceDayRecord record)
        {
            if (_phase != Phase.Start || Current != null || _queue.Count > 0) return OpResult.Fail("再開できるのは、復元したあとの最初の『続ける』の前だけ");
            RaceKind? race = Session.RaceThisWeek;
            if (record == null || !race.HasValue || race.Value != record.Kind || S.Settings.SpectateMode != "Full") return OpResult.Fail("記録がこの週の大会と合わない");
            _resuming = true;
            _resume = record;
            return OpResult.Success;
        }

        /// <summary>『続ける』(C-FLOW-01)。次の停止に進んで返す。停止がない週は飛ばして、次の停止まで週を進める(月末レポートが 4 週ごとにあるので、必ず止まる)。</summary>
        public Stop Advance()
        {
            while (true)
            {
                if (_queue.Count > 0) return Current = _queue.Dequeue();
                switch (_phase)
                {
                    case Phase.Start:
                        BuildBeforeStops();
                        _phase = Phase.Before;
                        break;
                    case Phase.Before:
                        _phase = TrainWeek() ? Phase.Race : Phase.After;
                        if (_phase == Phase.After) RaceAndEvents();
                        break;
                    case Phase.Race:
                        if (Session.CurrentRaceDay != null && !Session.CurrentRaceDay.Started) Session.CurrentRaceDay.Complete();       // 当日の画面を『続ける』で閉じたら、決定のまま走る
                        RaceAndEvents();
                        _phase = Phase.After;
                        break;
                    case Phase.After:
                        CloseWeek();
                        _phase = Phase.Month;
                        break;
                    case Phase.Month:
                        EndOfMonthOrWeek();
                        break;
                    case Phase.Settle:
                        Turnover();
                        _phase = Phase.Turnover;
                        break;
                    default:
                        _phase = Phase.Start;
                        break;
                }
            }
        }

        // ------------------------------------------------------------ 週の初め
        private void BuildBeforeStops()
        {
            int yw = TeamWeek.YearWeek(S);
            if (yw % 4 == 0) _monthStartInjured = new HashSet<int>(S.Runners.Where(r => r.IsInjured).Select(r => r.Id));
            YearState ys = Session.PrepareYear();
            var qualifies = new HashSet<string>();
            for (int i = 0; i < 3; i++)
                if (ys.Entrants((RaceKind)i).Contains(Session.School.Id)) qualifies.Add(RaceNames[i]);
            bool seed = ys.Seeds.Contains(Session.School.Id);
            if (_resuming) return;                                                          // 中断セーブからの再開: 週の初めの停止は中断の前に済んでいる
            foreach (string key in FlowRules.StopsBeforeWeek(yw, qualifies, seed, S.Settings.PlanAuto)) _queue.Enqueue(MakeStop(key, null, null));
        }

        private Stop MakeStop(string key, WeekOutcome week, FiredEvent ev)
        {
            string head = key, tail = null;
            int c = key.IndexOf(':');
            if (c >= 0) { head = key.Substring(0, c); tail = key.Substring(c + 1); }
            var st = new Stop { Key = key, Week = week };
            switch (head)
            {
                case "AnnualStart": st.Kind = StopKind.AnnualStart; break;
                case "MonthPlan": st.Kind = StopKind.MonthPlan; break;
                case "Entry":
                    if (tail == "Qualifier") st.Kind = StopKind.QualifierEntry;
                    else { st.Kind = StopKind.Entry; st.Race = (RaceKind)Array.IndexOf(RaceNames, tail); }
                    break;
                case "Order": st.Kind = StopKind.Order; st.Race = (RaceKind)Array.IndexOf(RaceNames, tail); break;
                case "RaceDay":
                    st.Kind = StopKind.RaceDay;
                    if (tail == "Qualifier") st.Qualifier = true; else st.Race = (RaceKind)Array.IndexOf(RaceNames, tail);
                    break;
                case "Event":
                    st.Kind = StopKind.Event;
                    st.Event = ev;
                    st.Choices = Session.ValidChoices(ev, week.OwnRace);
                    break;
                case "Injury": st.Kind = StopKind.Injury; st.RunnerId = int.Parse(tail); break;
                case "Warn": st.Kind = StopKind.Warn; break;
                default: throw new InvalidOperationException("未知の停止 " + key);
            }
            return st;
        }

        // ------------------------------------------------------------ 週の解決
        /// <summary>練習の解決(調子が確定する)。観戦モードがフルで自校が出る大会なら、当日(SC-24)の停止を積んで true。</summary>
        private bool TrainWeek()
        {
            if (!_resuming)
            {
                _dayRecord = null;
                if (ResolvingWeek != null) ResolvingWeek(this);
            }
            _resuming = false;
            var o = new WeekOutcome { AbsWeek = S.AbsWeek };
            o.Reports = Session.ResolveTraining(out o.RunnerIds);
            _week = o;
            RaceKind? race = Session.RaceThisWeek;
            if (!race.HasValue || S.Settings.SpectateMode != "Full")
            {
                if (_resume != null) throw new InvalidOperationException("中断セーブの記録がこの週の大会と合わない");
                return false;
            }
            RaceDay rd = Session.OpenRaceDay(race.Value);
            if (rd == null)
            {
                if (_resume != null) throw new InvalidOperationException("中断セーブの記録の大会に出られない");
                return false;                                                               // 出走人数に足りない(棄権)
            }
            if (_resume != null)
            {
                OpResult back = rd.Restore(_resume);
                if (!back.Ok) throw new InvalidOperationException("中断セーブの当日の記録を戻せない: " + back.Reason);
                bool finished = _resume.NextSection >= rd.Sections;
                _resume = null;
                if (finished) return true;                                                  // 走り終わったあとの再開: 当日の画面は出さず、結果の停止から
            }
            _queue.Enqueue(new Stop { Kind = StopKind.RaceDayPrep, Key = "RaceDayPrep:" + RaceNames[(int)race.Value], Race = race.Value, RaceDay = rd, Week = o });
            return true;
        }

        private void RaceAndEvents()
        {
            int yw = TeamWeek.YearWeek(S), month = yw / 4;
            WeekOutcome o = _week;
            if (Session.CurrentRaceDay != null) _dayRecord = Session.CurrentRaceDay.Capture();       // 走ったあとの中断のために、当日の決定を残す
            o.Race = Session.PlayWeekRaces(out o.OwnRace);
            _sel = Session.SelectWeekEvents(o.OwnRace);
            _week = o;
            var events = _sel.Fired.Select(f => new FlowRules.EventInfo { Id = f.Def.Id, Choices = Session.ValidChoices(f, o.OwnRace).Count }).ToList();
            var injuries = new List<FlowRules.InjuryInfo>();
            for (int i = 0; i < o.Reports.Count; i++)
            {
                if (!o.Reports[i].NewInjury.HasValue) continue;
                Runner r = S.FindRunner(o.RunnerIds[i]);
                injuries.Add(new FlowRules.InjuryInfo { RunnerId = r.Id, Severity = (int)o.Reports[i].NewInjury.Value, Group = r.Group, Registered = S.Entries.Any(e => e.Contains(r.Id)) });
            }
            bool warn = false;
            int absMonth = Calendar.FloorDiv(S.AbsWeek, Calendar.WeeksPerYear) * 12 + month;
            if (yw % 4 == 2 && _warnedMonth != absMonth)
            {
                int thr = month == 4 ? WarnFatigueCamp : WarnFatigue;
                warn = S.Runners.Any(r => !r.IsInjured && r.Fatigue >= thr && r.Fatigue < WarnFatigueMax);
                if (warn) _warnedMonth = absMonth;
            }
            string raceKind = o.OwnRace.HasValue ? RaceNames[o.OwnRace.Value.Kind - 1] : (yw == GameSession.QualifierWeek && !Session.YearInProgress.Seeds.Contains(Session.School.Id) ? "Qualifier" : null);
            List<string> stops, deferred;
            FlowRules.StopsAfterWeek(yw, events, injuries, warn, raceKind, S.Settings.EventAuto, out stops, out deferred);
            _deferred = deferred;
            foreach (string key in stops)
            {
                if (key == "MonthReport" || key == "YearEnd") continue;                      // 月末レポート・年度末は、週の終わりの処理のあとに出す
                FiredEvent ev = key.StartsWith("Event:", StringComparison.Ordinal) ? _sel.Fired.First(f => f.Def.Id == key.Substring(6)) : null;
                _queue.Enqueue(MakeStop(key, o, ev));
            }
        }

        /// <summary>イベントの選択をする(<see cref="Current"/> が Event の停止のとき)。選ばずに『続ける』と既定の選択肢。</summary>
        public OpResult ChooseEvent(string choiceId)
        {
            if (Current == null || Current.Kind != StopKind.Event) return OpResult.Fail("選ぶイベントがない");
            if (!Current.Choices.Any(c => c.Id == choiceId)) return OpResult.Fail("その選択肢は選べない");
            _chosen[Current.Event.Def.Id] = choiceId;
            return OpResult.Success;
        }

        private sealed class FlowChoicePolicy : IChoicePolicy
        {
            private readonly Dictionary<string, string> _chosen;
            private readonly DefaultChoicePolicy _default = new DefaultChoicePolicy();

            public FlowChoicePolicy(Dictionary<string, string> chosen) { _chosen = chosen; }

            public ChoiceDef Choose(EventDef e, IReadOnlyList<ChoiceDef> valid, Pcg32 stream)
            {
                string id;
                if (_chosen.TryGetValue(e.Id, out id)) foreach (ChoiceDef c in valid) if (c.Id == id) return c;
                return _default.Choose(e, valid, stream);
            }
        }

        /// <summary>選択を反映して効果を適用し、週の終わりの処理(月末など)を行う。月末なら月末レポートを積む。</summary>
        private void CloseWeek()
        {
            int yw = TeamWeek.YearWeek(S);
            _week.Events = Session.ResolveWeekEvents(_sel, _week.OwnRace, new FlowChoicePolicy(_chosen));
            _chosen.Clear();
            Session.FinishWeek();
            _monthWeeks.Add(_week);
            foreach (string d in _deferred) _monthDeferred.Add(d);
            if (yw % 4 == 3) _queue.Enqueue(new Stop { Kind = StopKind.MonthReport, Key = "MonthReport", Week = _week, Report = BuildMonthReport(yw / 4) });
        }

        private void EndOfMonthOrWeek()
        {
            int yw = TeamWeek.YearWeek(S);
            _dayRecord = null;
            if (yw % 4 == 3) { _monthWeeks.Clear(); _monthDeferred.Clear(); }
            if (yw == Calendar.WeeksPerYear - 1)
            {
                Session.SettleYear();
                _phase = Phase.Settle;
                _queue.Enqueue(new Stop { Kind = StopKind.YearEndSettle, Key = "YearEnd:Settle", Week = _week, Year = Session.YearInProgress.Result });
                return;
            }
            Session.AdvanceWeek();
            _phase = Phase.Start;
        }

        private void Turnover()
        {
            YearResult res = Session.TurnoverYear();
            _week.YearEnd = res;
            _week.Turnover = Session.LastTurnover;
            Session.AdvanceWeek();
            _monthWeeks.Clear();
            _monthDeferred.Clear();
            _queue.Enqueue(new Stop { Kind = StopKind.YearEndTurnover, Key = "YearEnd:Turnover", Week = _week, Year = res, Turnover = Session.LastTurnover, IntakeIds = Session.LastIntakeIds });
        }

        // ------------------------------------------------------------ 月末レポート(R-10。項目の作り方は暫定: 故障・成長・復帰・お知らせ・覚醒)
        private MonthReport BuildMonthReport(int month)
        {
            var rep = new MonthReport { MonthIndex = month, Morale = S.Morale };
            rep.Weeks.AddRange(_monthWeeks);
            rep.Deferred.AddRange(_monthDeferred);
            var items = new List<FlowRules.ReportItem>();
            var gain = new Dictionary<int, int>();
            foreach (WeekOutcome w in _monthWeeks)
            {
                for (int i = 0; i < w.Reports.Count; i++)
                {
                    int id = w.RunnerIds[i];
                    WeekReport r = w.Reports[i];
                    if (r.NewInjury == InjurySeverity.Severe) items.Add(new FlowRules.ReportItem { Kind = "injury_severe", Key = "inj:" + id, Score = 100 });
                    else if (r.NewInjury == InjurySeverity.Moderate) items.Add(new FlowRules.ReportItem { Kind = "injury_moderate", Key = "inj:" + id, Score = 90 });
                    gain.TryGetValue(id, out int g);
                    gain[id] = g + r.Levelups;
                }
                foreach (FiredEvent f in w.Events)
                {
                    if (f.Def.Category == EventCategory.Awakening) items.Add(new FlowRules.ReportItem { Kind = "awaken", Key = f.Def.Id + ":" + (f.R == null ? 0 : f.R.Id), Score = 1 });
                    else if (!f.Def.Interactive) items.Add(new FlowRules.ReportItem { Kind = "notice", Key = f.Def.Id, Score = 1 });
                }
            }
            foreach (var kv in gain.OrderBy(k => k.Key))
                if (kv.Value >= 2) items.Add(new FlowRules.ReportItem { Kind = "big_growth", Key = "gain:" + kv.Key, Score = kv.Value });
            foreach (int id in _monthStartInjured.OrderBy(x => x))
            {
                Runner r = S.FindRunner(id);
                if (r != null && !r.IsInjured) items.Add(new FlowRules.ReportItem { Kind = "return", Key = "ret:" + id, Score = 1 });
            }
            rep.Items = FlowRules.SelectReportItems(items);
            rep.More = items.Count - rep.Items.Count;
            return rep;
        }
    }
}
