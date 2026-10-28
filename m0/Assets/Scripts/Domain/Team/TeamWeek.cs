using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Team
{
    /// <summary>自校の 1 週の処理(06 §7.1): ①全選手の週の解決(03 §6)→ ②絆の週の更新(03 §7.1)→ ③大会(04。呼ぶ側 <c>GameSession</c>)と大会後の処理(<see cref="TeamRacing.ApplyResult"/>)→
    /// ④EventPhase(06 §7)→ ⑤第 4 週なら月次処理(03 §7)→ ⑥3 月の第 4 週は年度末処理(05 §12。<c>GameSession</c>)。規則は 03・04・06 の関数をそのまま呼ぶ。</summary>
    public sealed class TeamWeek
    {
        public static readonly int[] EntryDeadlineWeeks = { 21, 24, 32 };       // 大会の 4 週前(第一戦 25・第二戦 28・第三戦 36)

        private readonly TrainingMaster _m;
        private readonly WeekResolver _res;

        public TeamWeek(TrainingMaster master) { _m = master; _res = new WeekResolver(master); }

        public static int YearWeek(TeamState s) { return Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear); }

        // ------------------------------------------------------------ 週の最初
        /// <summary>週の最初: 年度の最初なら前年度の記録を送る(おまかせなら推奨の編成も)・月の最初ならスカウトポイントと指導ポイントと月のテーマ・期限切れの補正と役割を外す・派生値を付け直す・エントリー締切なら登録(おまかせ登録を初期値に)。
        /// <see cref="Game.GameSession"/> は前の週の終わり(新規ゲームでは最初)に呼ぶので、保存の境目はこれが済んだ状態になる。</summary>
        public void BeginWeek(TeamState s)
        {
            int yw = YearWeek(s), month = yw / 4, wk = yw % 4;
            if (yw == 0) s.RollYear();
            if (wk == 0)
            {
                s.GuidanceLeft = 4;                                                         // 月初に 4 へ戻る(繰り越さない。02 §4.4)
                s.Talked.Clear();
                s.IndividualMenus.Clear();                                                  // 個別指導のメニューは月末まで(03 §5.7)
                if (month >= 2 && month <= 10) s.ScoutPts += 4;                              // 6〜2 月に月 4 ポイント(05 §7.1)
                if (month == 2) s.ScoutPts += 4 * (s.CoachRank / 3);                         // ランクによる追加分は 6 月に一括
                if (s.Settings.PlanAuto) ApplyAutoPlan(s, month);                           // plan_auto: 月間計画を推奨プランで自動に確定(08 R-04)
            }
            s.ExpireTimed();
            if (yw == 0 && s.Settings.PlanAuto) s.AssignRecommendedGroups();                 // おまかせ: 年度初めに推奨の編成を適用(手動のときは画面の『推奨の編成を適用』)
            s.Refresh(assignGroups: false);                                                // グループは保存される編成(08 R-04)
            for (int k = 0; k < 3; k++)
                if (yw == EntryDeadlineWeeks[k]) { s.Entries[k].Clear(); s.Entries[k].AddRange(TeamRacing.AutoRegister(s, (RaceKind)k)); }
        }

        /// <summary>推奨プラン(08 R-04)を全グループに適用する。プレイヤーが決めるときは、画面が <see cref="TeamState.MonthlyPlan"/> を書き換える。</summary>
        private static void ApplyAutoPlan(TeamState s, int month)
        {
            string theme = PlanRules.AutoPlan(s.Plan.Focus, month);
            for (int g = 0; g < 3; g++) s.MonthlyPlan[g] = theme;
        }

        // ------------------------------------------------------------ 練習
        /// <summary>その週のその選手のメニュー(03 §5.1 の優先順位): 故障中 > 個別指定 > ピーキング > 安全網(解決の中)> 月間計画。camp = 合宿の週(テーマが th.camp)。</summary>
        public Menu MenuFor(TeamState s, Runner r, out bool camp, out bool individual)
        {
            int yw = YearWeek(s), month = yw / 4, wk = yw % 4;
            string theme = s.MonthlyPlan[r.Group];
            camp = theme == "th.camp";
            individual = false;
            IndividualMenu im;
            if (!r.IsInjured && s.IndividualMenus.TryGetValue(r.Id, out im))
            {
                individual = true;
                return new Menu(im.Menu.Key1, im.Menu.Key2, im.Menu.Mileage);
            }
            int taper = TaperIndex(s, r, yw);
            if (taper >= 0) return _m.Taper[taper];
            if (theme == "th.custom" && s.CustomWeeks != null)
            {
                MenuSpec w = s.CustomWeeks[r.Group][wk];
                return new Menu(w.Key1, w.Key2, w.Mileage);
            }
            return _m.Themes[theme][wk];
        }

        /// <summary>ピーキング(03 §5.3): その大会の登録メンバーに、大会週 t の 3 週前から <c>TAPER[1..4]</c>(重なるときは後ろから詰める)。Short は t−1 と t だけ(<c>TAPER[3..4]</c>)。None は適用しない。-1 = 適用なし。</summary>
        public static int TaperIndex(TeamState s, Runner r, int yw)
        {
            for (int k = 0; k < 3; k++)
            {
                if (!s.Entries[k].Contains(r.Id)) continue;
                PeakingMode mode = s.Plan.Peaking[k];
                if (mode == PeakingMode.None) continue;
                int t = Calendar.RaceWeeks[k];
                int prev = k > 0 ? Calendar.RaceWeeks[k - 1] : -10;
                int start = Math.Max(t - (mode == PeakingMode.Standard ? 3 : 1), prev + 1);
                if (yw >= start && yw <= t) return 4 - (t - start + 1) + (yw - start);
            }
            return -1;
        }

        /// <summary>①全選手の週の解決(補正を受け取る)とカウンターの更新(06 §10)、②絆の週の更新(03 §7.1)。</summary>
        public List<WeekReport> TrainAll(TeamState s, IRngFactory rngs)
        {
            int yw = YearWeek(s), month = yw / 4;
            var reports = new List<WeekReport>();
            bool safety = s.Settings.AutoSafetyF > 0;
            int safetyF = safety ? s.Settings.AutoSafetyF : TrainingMaster.K_SAFETY_F;
            foreach (Runner r in s.Runners)
            {
                bool wasInjured = r.IsInjured;
                bool camp, individual;
                Menu menu = MenuFor(s, r, out camp, out individual);
                WeekReport rep = _res.Resolve(r, menu, s.Fac, month, rngs, r.Id, s.AbsWeek, camp: camp, safety: safety, individual: individual, bondAvg: r.BondAvg, mods: s.ModsFor(r), safetyF: safetyF);
                reports.Add(rep);
                if (!r.GiftRevealed && s.AbsWeek - r.EnrollWeek >= Individuality.K_GIFT_REVEAL_WEEKS)      // 素養は入部から K_GIFT_REVEAL_WEEKS 週で判明(表示だけが変わる。10 §8)
                {
                    r.GiftRevealed = true;
                    rep.GiftRevealed = r.Gifts.Length > 0;
                }
                // 追跡の値(06 §10・03 §7.2)
                if (rep.NewInjury.HasValue)
                {
                    r.InjuryAge = 0;
                    r.AddCtr(Counters.Injuries, 1);
                    r.Counters[Counters.InjuryFree] = 0;
                    if (rep.NewInjury.Value == InjurySeverity.Severe) r.Motivation = Math.Max(1, r.Motivation - 1);      // M2: 重傷
                    s.IndividualMenus.Remove(r.Id);                                                                    // 故障した選手の個別指定は破棄(I-20)
                }
                else if (r.IsInjured) { r.InjuryAge = Math.Min(99, r.InjuryAge + 1); r.Counters[Counters.InjuryFree] = 0; }
                else { r.InjuryAge = 99; r.AddCtr(Counters.InjuryFree, 1); }
                if ((menu.Key1 == "ks.hill" || menu.Key2 == "ks.hill") && !wasInjured) r.AddCtr(Counters.HillWeeks, 1);
                if (camp) r.CampMaxFatigue = Math.Max(r.CampMaxFatigue, r.Fatigue);
                r.HighFatigueWeeks = r.Fatigue >= 85 ? r.HighFatigueWeeks + 1 : 0;
                if (r.HighFatigueWeeks >= 2) r.OverloadPending = true;                                                  // M1
                r.MonthGain += rep.Levelups;
            }
            // 絆(03 §7.1): 同じグループで、2 人とも故障中でない組 +10(同じ県 +11、同じ高校 +12)
            for (int i = 0; i < s.Runners.Count; i++)
                for (int j = i + 1; j < s.Runners.Count; j++)
                {
                    Runner a = s.Runners[i], b = s.Runners[j];
                    if (a.Group != b.Group || a.IsInjured || b.IsInjured) continue;
                    int d = 10 + (a.Pref != 0 && a.Pref == b.Pref ? 1 : 0) + (a.HighSchool != 0 && a.HighSchool == b.HighSchool ? 1 : 0);
                    s.AddBond(a, b, d);
                }
            return reports;
        }

        // ------------------------------------------------------------ イベント
        public List<FiredEvent> RunEvents(TeamState s, EventPhase phase, RaceInfo? race, IChoicePolicy policy)
        {
            List<FiredEvent> fired = phase.Run(s, race, policy);
            foreach (Runner r in s.Runners) r.RaceFlags = default(RaceFlags);              // race.* は大会週の EventPhase だけ
            return fired;
        }

        // ------------------------------------------------------------ 月次
        /// <summary>第 4 週の終わり(03 §7): 絆の減衰・士気の再計算(05 §10.4)・やる気の規則 M1・M4・M5・M6・夏合宿(8 月)のカウンター。</summary>
        public void MonthEnd(TeamState s)
        {
            int yw = YearWeek(s), month = yw / 4;
            var ids = new Dictionary<int, Runner>();
            foreach (Runner r in s.Runners) ids[r.Id] = r;
            foreach (int[] b in s.Bonds.Items())                                           // 異なるグループの組で絆 > 0 は −10
            {
                Runner a = ids[b[0]], c = ids[b[1]];
                if (a.Group != c.Group) s.Bonds.Add(a.Id, c.Id, -10);
            }
            double avg = 0;
            foreach (Runner r in s.Runners) avg += r.Motivation;
            avg /= Math.Max(1, s.Runners.Count);
            s.Morale = Math.Max(0, Math.Min(100, Rounding.HalfUp(25 * (avg - 1)) + ResultBonus(s)));
            foreach (Runner r in s.Runners)
            {
                if (r.OverloadPending) { r.Motivation = Math.Max(1, r.Motivation - 1); r.OverloadPending = false; }     // M1
                if (s.Morale >= 70 && r.Motivation <= 2) r.Motivation++;                                                // M4
                if (s.Morale <= 30 && r.Motivation >= 4) r.Motivation--;                                                // M5
                if (r.MonthGain >= 2 && r.Motivation <= 3) r.Motivation++;                                              // M6
                r.MonthGain = 0;
                if (month == 4)                                                                                         // 8 月: 夏合宿を過ごした
                {
                    r.AddCtr(Counters.Camps, 1);
                    if (r.CampMaxFatigue >= 85) r.AddCtr(Counters.CampOverload, 1);
                    r.CampMaxFatigue = 0;
                }
            }
        }

        /// <summary>士気の結果加点(05 §10.4): 年度のいずれかの大会で優勝 +10 / 入賞(3 位以内)+5 / 前年度より全大会で順位を下げた −5 / それ以外 0。</summary>
        public static int ResultBonus(TeamState s)
        {
            int best = 99, held = 0, worse = 0;
            for (int k = 0; k < 3; k++)
            {
                if (s.Rank[k] == 99) continue;
                held++;
                best = Math.Min(best, s.Rank[k]);
                if (s.Rank[k] > s.LastRank[k]) worse++;
            }
            if (best == 1) return 10;
            if (best <= 3) return 5;
            return held > 0 && worse == held ? -5 : 0;
        }
    }
}
