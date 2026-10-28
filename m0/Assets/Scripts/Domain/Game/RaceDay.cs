using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Race;
using Ekiden.Domain.Team;

namespace Ekiden.Domain.Game
{
    /// <summary>当日の 1 区間の見え方(08 SC-24)。余裕の目安(◎〇△)は維持のときの失速の危険度 q から(04 §7.4)。</summary>
    public sealed class LegView
    {
        public int Section;
        public RaceRunner Runner;
        public double Distance;
        public SectionType Type;
        public double Fit;                  // 区間適性値 P(特殊能力の加算を含む)
        public char FitMark;                // P 70 以上 ◎ / 60 以上 〇 / 未満 △
        public char PaceMark;               // 余裕の目安 ◎ 攻める / 〇 維持 / △ 抑える
        public Instruction Recommended;     // 目安どおりの指示
        public Instruction Instr;           // いまの指示
        public double ExpectedSec;          // いまの指示での期待時間(雑音・ブレーキ・展開は含まない。04 §5.3)
        public bool Swapped;                // 提出時の配置から当日変更で替わった
    }

    /// <summary>走行中の『異変』(ブレーキ・失速)。区間の途中(1/3 か 2/3 の地点)で通知される(04 §9.2)。</summary>
    public sealed class Incident
    {
        public int Section;
        public string Kind;                 // "brake" / "stall"(両方起きたときは大きい方)
        public double Loss;                 // 檄を使わないときの損失(基準タイムに対する割合)
        public double Pos;                  // 1/3 か 2/3
        public int GekiLeft;
    }

    /// <summary>中継所の順位表の 1 行。</summary>
    public sealed class StandingRow
    {
        public int TeamId, Rank, CumDs, GapDs, SectionDs;
        public bool IsOwn;
    }

    /// <summary>
    /// 大会の当日(04 §5.3・§9。08 SC-24・SC-25): 調子と環境が確定したあとの、当日変更・指示・檄。
    /// 初期値は『おまかせ』(不調以下の先発を最適な控えに自動で置き換え、指示は全区間維持、檄は未決 = 自動)で、<see cref="GameSession.PlayWeekRaces"/> がそのまま走らせれば、おまかせの当日と同じ。
    /// 確定の前なら何度でも変えられる。乱数は指示・檄・当日変更で引き方が変わらない(04 §12)ので、いまの決定で全チームの結果を何度でも再計算できる(<see cref="Preview"/>)。
    /// レース進行(<see cref="Start"/> 以降)は、区間の前に指示(<see cref="SetInstruction"/>)、異変が出たら檄(<see cref="UseGeki"/>)、<see cref="Advance"/> で次の中継所へ。通過した区間の決定は変えられない。
    /// </summary>
    public sealed class RaceDay
    {
        private readonly TeamRacing.DayPlan _plan;
        private readonly TeamState _state;
        private readonly RaceRule _rule;
        private readonly Func<RaceTeam, List<TeamResult>> _simulate;
        private readonly int[] _pre;                        // 提出時の配置(区間 → Day の添字)
        private readonly int[] _idx;                        // いまの配置
        private readonly Instruction[] _instr;
        private readonly bool?[] _geki;                     // 区間ごとの檄(null = 未決 = 自動)
        private int _gekiSpent;

        public RaceKind Kind { get { return _plan.Kind; } }
        public int RaceId { get { return _plan.RaceId; } }
        public Env[] Environment { get { return _plan.Envs; } }
        public int Sections { get { return _idx.Length; } }
        public int SwapLimit { get { return _rule.Swap; } }
        public int GekiLimit { get { return _rule.Geki; } }

        /// <summary>何区間まで通過したか(レース進行)。</summary>
        public int Resolved { get; private set; }

        public bool Started { get; private set; }

        /// <summary>画面で何かを変えた(おまかせのままではない)。</summary>
        public bool Touched { get; private set; }

        internal RaceDay(TeamRacing.DayPlan plan, TeamState state, Func<RaceTeam, List<TeamResult>> simulate)
        {
            _plan = plan;
            _state = state;
            _rule = RaceTables.Rule(plan.Kind);
            _simulate = simulate;
            int n = plan.PreSwap.Length;
            _pre = (int[])plan.PreSwap.Clone();
            _idx = (int[])plan.PreSwap.Clone();
            _instr = new Instruction[n];
            for (int i = 0; i < n; i++) _instr[i] = Instruction.Keep;
            _geki = new bool?[n];
            ApplyRecommendedSwaps();
        }

        private void ApplyRecommendedSwaps()
        {
            Array.Copy(_pre, _idx, _idx.Length);
            if (_rule.Swap > 0) DayPlanner.ApplySwaps(_plan.Day, _idx, Kind, _plan.Envs, "auto", _rule.Swap);
        }

        // ------------------------------------------------------------ 見え方
        public IReadOnlyList<RaceRunner> Lineup { get { return _idx.Select(j => _plan.Day[j]).ToList(); } }

        /// <summary>控え(出走しない登録メンバー。欠場した選手・補充要員のうち出られるもの)。当日変更の選択肢。</summary>
        public IReadOnlyList<RaceRunner> Bench { get { return BenchIndices().Select(j => _plan.Day[j]).ToList(); } }

        public IReadOnlyList<Instruction> Instructions { get { return _instr; } }

        private List<int> BenchIndices()
        {
            var b = new List<int>();
            for (int j = 0; j < _plan.Day.Count; j++) if (Array.IndexOf(_idx, j) < 0) b.Add(j);
            return b;
        }

        public int SwapsUsed
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _idx.Length; i++) if (_idx[i] != _pre[i]) n++;
                return n;
            }
        }

        public int SwapsLeft { get { return _rule.Swap - SwapsUsed; } }

        public int GekiLeft { get { return _rule.Geki - _gekiSpent; } }

        public LegView View(int section)
        {
            IReadOnlyList<SectionDef> secs = RaceTables.Course(Kind);
            SectionDef sec = secs[section];
            int longest = 0;
            for (int i = 1; i < secs.Count; i++) if (secs[i].Distance > secs[longest].Distance) longest = i;
            RaceRunner r = _plan.Day[_idx[section]];
            Env env = RaceMath.DayEnv(Kind, _plan.Envs, section);
            double p = RaceMath.SectionRating(r, sec.Type, section == longest, section == secs.Count - 1);
            var g = DayPlanner.Gauge(r, sec.Distance, sec.Type, env.Temp);
            return new LegView
            {
                Section = section, Runner = r, Distance = sec.Distance, Type = sec.Type, Fit = p, FitMark = p >= 70.0 ? '◎' : (p >= 60.0 ? '〇' : '△'),
                PaceMark = g.mark[0], Recommended = g.instr, Instr = _instr[section], ExpectedSec = DayPlanner.LegEv(r, section, Kind, env, _instr[section]), Swapped = _idx[section] != _pre[section],
            };
        }

        /// <summary>控え <paramref name="benchIndex"/> を section に入れたときの期待時間(いまの指示で)。いまの先発との差が当日変更の判断材料。</summary>
        public double BenchExpectedSec(int section, int benchIndex)
        {
            RaceRunner b = _plan.Day[BenchIndices()[benchIndex]];
            return DayPlanner.LegEv(b, section, Kind, RaceMath.DayEnv(Kind, _plan.Envs, section), _instr[section]);
        }

        // ------------------------------------------------------------ 当日変更・指示(SC-24)
        /// <summary>当日変更: section の先発を控え(<see cref="Bench"/> の benchIndex 番目)と入れ替える。入れ替わった選手は同じ区間を走る。上限(第一戦 1 / 第二戦 2 / 第三戦 4)。</summary>
        public OpResult Swap(int section, int benchIndex)
        {
            if (Started) return OpResult.Fail("レースが始まったあとは当日変更できない");
            if (section < 0 || section >= Sections) return OpResult.Fail("その区間はない");
            List<int> bench = BenchIndices();
            if (benchIndex < 0 || benchIndex >= bench.Count) return OpResult.Fail("その控えはいない");
            int old = _idx[section];
            _idx[section] = bench[benchIndex];
            if (SwapsUsed > _rule.Swap) { _idx[section] = old; return OpResult.Fail("当日変更は " + _rule.Swap + " 名まで"); }
            if (IntlCount() > _rule.IntlRun) { _idx[section] = old; return OpResult.Fail("留学生の出走は " + _rule.IntlRun + " 人まで"); }
            Touched = true;
            return OpResult.Success;
        }

        /// <summary>当日変更をすべて戻す(提出時の配置)。</summary>
        public OpResult ResetSwaps()
        {
            if (Started) return OpResult.Fail("レースが始まったあとは変えられない");
            Array.Copy(_pre, _idx, _idx.Length);
            Touched = true;
            return OpResult.Success;
        }

        /// <summary>『おまかせ』: 当日変更は不調以下の先発を最適な控えに置き換える・指示は全区間維持・檄は自動(初期状態)。</summary>
        public OpResult UseRecommended()
        {
            if (Started) return OpResult.Fail("レースが始まったあとは変えられない");
            ApplyRecommendedSwaps();
            for (int i = 0; i < _instr.Length; i++) _instr[i] = Instruction.Keep;
            for (int i = 0; i < _geki.Length; i++) _geki[i] = null;
            Touched = false;
            return OpResult.Success;
        }

        /// <summary>検算用の上級者の方針(04 §14.4): 当日変更は期待時間が最小になる控えと入れ替える(おまかせは不調以下の先発だけ)。</summary>
        public OpResult UseSkilledSwaps()
        {
            if (Started) return OpResult.Fail("レースが始まったあとは変えられない");
            Array.Copy(_pre, _idx, _idx.Length);
            if (_rule.Swap > 0) DayPlanner.ApplySwaps(_plan.Day, _idx, Kind, _plan.Envs, "skilled", _rule.Swap);
            Touched = true;
            return OpResult.Success;
        }

        /// <summary>検算用の上級者の方針: これから走る全区間の指示を、期待時間が最小になる指示にする(目安どおりよりわずかに上積みがある)。</summary>
        public OpResult UseBestInstructions()
        {
            for (int i = Resolved; i < Sections; i++) _instr[i] = DayPlanner.BestInstr(_plan.Day[_idx[i]], i, Kind, RaceMath.DayEnv(Kind, _plan.Envs, i));
            Touched = true;
            return OpResult.Success;
        }

        private int IntlCount() { return _idx.Count(j => _plan.Day[j].Overseas); }

        /// <summary>区間の指示(攻める / 維持 / 抑える)。通過した区間は変えられない。</summary>
        public OpResult SetInstruction(int section, Instruction instr)
        {
            if (section < 0 || section >= Sections) return OpResult.Fail("その区間はない");
            if (section < Resolved) return OpResult.Fail("通過した区間の指示は変えられない");
            _instr[section] = instr;
            Touched = true;
            return OpResult.Success;
        }

        /// <summary>『目安どおりに指示』: これから走る全区間の指示を、余裕の目安(◎ 攻める / 〇 維持 / △ 抑える)にそろえる。</summary>
        public OpResult ApplyGauge()
        {
            for (int i = Resolved; i < Sections; i++) _instr[i] = View(i).Recommended;
            Touched = true;
            return OpResult.Success;
        }

        // ------------------------------------------------------------ 中断セーブ(09 §4.6)
        /// <summary>いまの決定を記録する(いつでも。レースを始める前・途中・終わったあと)。</summary>
        public RaceDayRecord Capture()
        {
            var r = new RaceDayRecord { Kind = Kind, RaceId = RaceId, NextSection = Resolved, Started = Started, Touched = Touched, GekiSpent = _gekiSpent };
            r.Instructions = new int[_instr.Length];
            r.GekiUsed = new int[_geki.Length];
            for (int i = 0; i < _instr.Length; i++)
            {
                r.Instructions[i] = (int)_instr[i];
                r.GekiUsed[i] = _geki[i].HasValue ? (_geki[i].Value ? 1 : 0) : -1;
                if (_idx[i] != _pre[i]) r.Swaps.Add(new RaceDayRecord.Swap { Section = i, RunnerId = _plan.Day[_idx[i]].RunnerId });
            }
            return r;
        }

        /// <summary>記録した決定を戻す(新しく開いた当日に。<see cref="GameSession.OpenRaceDay"/> のあと)。合わない記録(別の大会・いない選手・上限超え)は拒否して、当日は変えない。</summary>
        public OpResult Restore(RaceDayRecord r)
        {
            int n = Sections;
            if (r == null || r.Kind != Kind || r.RaceId != RaceId) return OpResult.Fail("記録は別の大会のもの");
            if (r.Instructions == null || r.GekiUsed == null || r.Instructions.Length != n || r.GekiUsed.Length != n) return OpResult.Fail("記録の区間の数が合わない");
            if (r.NextSection < 0 || r.NextSection > n || (r.NextSection > 0 && !r.Started)) return OpResult.Fail("記録の進み具合がおかしい");
            if (r.GekiSpent < 0 || r.GekiSpent > _rule.Geki) return OpResult.Fail("記録の檄の回数がおかしい");
            var instr = new Instruction[n];
            var geki = new bool?[n];
            for (int i = 0; i < n; i++)
            {
                if (r.Instructions[i] < 0 || r.Instructions[i] > (int)Instruction.Hold) return OpResult.Fail("記録の指示がおかしい");
                instr[i] = (Instruction)r.Instructions[i];
                if (r.GekiUsed[i] < -1 || r.GekiUsed[i] > 1) return OpResult.Fail("記録の檄がおかしい");
                geki[i] = r.GekiUsed[i] < 0 ? (bool?)null : r.GekiUsed[i] == 1;
            }
            var idx = (int[])_pre.Clone();
            foreach (RaceDayRecord.Swap s in r.Swaps)
            {
                if (s.Section < 0 || s.Section >= n) return OpResult.Fail("記録の当日変更の区間がおかしい");
                int j = _plan.Day.FindIndex(x => x.RunnerId == s.RunnerId && s.RunnerId != 0);
                if (j < 0) return OpResult.Fail("記録の当日変更の選手がいない");
                idx[s.Section] = j;
            }
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) if (idx[a] == idx[b]) return OpResult.Fail("記録の配置に同じ選手が 2 回出る");
            int used = 0, intl = 0;
            for (int i = 0; i < n; i++) { if (idx[i] != _pre[i]) used++; if (_plan.Day[idx[i]].Overseas) intl++; }
            if (used > _rule.Swap) return OpResult.Fail("記録の当日変更が上限を超える");
            if (intl > _rule.IntlRun) return OpResult.Fail("記録の留学生の出走が上限を超える");
            Array.Copy(idx, _idx, n);
            Array.Copy(instr, _instr, n);
            Array.Copy(geki, _geki, n);
            _gekiSpent = r.GekiSpent;
            Resolved = r.NextSection;
            Started = r.Started;
            Touched = r.Touched;
            return OpResult.Success;
        }

        // ------------------------------------------------------------ 結果の再計算
        /// <summary>いまの決定でのチーム(未決の檄は自動 = 異変が出れば上限まで使う)。</summary>
        public RaceTeam BuildTeam()
        {
            int n = Sections;
            var lineup = new RaceRunner[n];
            for (int i = 0; i < n; i++) lineup[i] = _plan.Day[_idx[i]];
            bool[] use = null;
            if (_geki.Any(g => g.HasValue))
            {
                use = new bool[n];
                for (int i = 0; i < n; i++) use[i] = !_geki[i].HasValue || _geki[i].Value;
            }
            return new RaceTeam { Id = _plan.TeamId, Lineup = lineup, Instr = (Instruction[])_instr.Clone(), Geki = _rule.Geki, GekiUse = use };
        }

        /// <summary>いまの決定で最後まで走ったときの全チームの結果(順位順)。</summary>
        public List<TeamResult> Preview() { return _simulate(BuildTeam()); }

        public TeamResult OwnResult(List<TeamResult> results) { return results.First(r => r.Id == _plan.TeamId); }

        // ------------------------------------------------------------ レース進行(SC-25)
        /// <summary>レースを始める(当日変更はここまで)。</summary>
        public OpResult Start()
        {
            if (Started) return OpResult.Fail("始まっている");
            Started = true;
            return OpResult.Success;
        }

        /// <summary>これから走る区間(<see cref="Resolved"/>)に自校の走者の異変(ブレーキ・失速)が起きるか。檄を使わない場合の損失と通知の位置を返す。なければ null。</summary>
        public Incident NextIncident()
        {
            if (!Started || Resolved >= Sections) return null;
            int s = Resolved;
            bool? keep = _geki[s];
            _geki[s] = false;
            TeamResult own;
            try { own = OwnResult(Preview()); }
            finally { _geki[s] = keep; }
            double brake = 0.0, stall = 0.0;
            foreach (RaceEvent e in own.Events)
            {
                if (e.Section != s) continue;
                if (e.Kind == "brake") brake = e.Value;
                else if (e.Kind == "stall") stall = e.Value;
            }
            if (brake <= 0.0 && stall <= 0.0) return null;
            return new Incident { Section = s, Kind = brake >= stall ? "brake" : "stall", Loss = Math.Max(brake, stall), Pos = own.IncidentPos[s], GekiLeft = GekiLeft };
        }

        /// <summary>異変に檄を使う(上限まで。異変が出ていないときは使えない)。</summary>
        public OpResult UseGeki()
        {
            if (!Started) return OpResult.Fail("レースが始まっていない");
            if (Resolved >= Sections) return OpResult.Fail("レースは終わっている");
            if (GekiLeft <= 0) return OpResult.Fail("檄の回数を使い切っている");
            if (NextIncident() == null) return OpResult.Fail("異変が出ていない");
            _geki[Resolved] = true;
            Touched = true;
            return OpResult.Success;
        }

        /// <summary>次の中継所へ進む(この区間の指示・檄を確定する)。戻り値は中継所での順位表。</summary>
        public List<StandingRow> Advance()
        {
            if (!Started) Start();
            if (Resolved >= Sections) return Standings(Sections - 1);
            int s = Resolved;
            bool use = _geki[s] == true;
            if (use && NextIncidentWithoutGekiExists(s)) _gekiSpent++;
            _geki[s] = use;
            Resolved++;
            return Standings(s);
        }

        private bool NextIncidentWithoutGekiExists(int s)
        {
            bool? keep = _geki[s];
            _geki[s] = false;
            try
            {
                TeamResult own = OwnResult(Preview());
                return own.Events.Any(e => e.Section == s && (e.Kind == "brake" || e.Kind == "stall"));
            }
            finally { _geki[s] = keep; }
        }

        /// <summary>残りの区間を、いまの決定のまま最後まで進める(未決の檄は自動)。</summary>
        public void Complete()
        {
            Started = true;
            Resolved = Sections;
        }

        /// <summary>中継所(区間 section の終わり)での順位表。いまの決定で再計算した結果から。</summary>
        public List<StandingRow> Standings(int section)
        {
            List<TeamResult> res = Preview();
            var rows = res.Select(r => new StandingRow { TeamId = r.Id, CumDs = r.Cum[section], SectionDs = r.Sections[section], IsOwn = r.Id == _plan.TeamId }).ToList();
            RaceSimulator.StableSort(rows, (a, b) => a.CumDs != b.CumDs ? a.CumDs.CompareTo(b.CumDs) : a.TeamId.CompareTo(b.TeamId));
            for (int i = 0; i < rows.Count; i++) { rows[i].Rank = i + 1; rows[i].GapDs = rows[i].CumDs - rows[0].CumDs; }
            return rows;
        }
    }
}
