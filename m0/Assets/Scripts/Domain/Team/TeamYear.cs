using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.Domain.Team
{
    /// <summary>年度末に自校の選手と資金に起こること(05 §4・§10): 卒業(進路・殿堂・OB)・退部・学年の繰り上げ・収支と強化。</summary>
    public sealed class YearTurnover
    {
        public readonly List<OBRecord> Graduates = new List<OBRecord>();
        public readonly List<Runner> Quit = new List<Runner>();
    }

    /// <summary>年度末の精算の内訳(05 §4.1。金額は万円)。画面(SC-12)の収支の表示用で、規則には作用しない。保存しない(年度末の画面は中断できない。09 §4.6)。</summary>
    public sealed class Settlement
    {
        public int FundsBefore;                 // 精算前の資金
        public double IncomeFormula;            // 800 + 名声 + 0.0012 × 名声² + 25 × 評価ポイント
        public double IncomeMod;                // funds_year の補正
        public double Staff;                    // スタッフ給与
        public double Upkeep;                   // 施設の維持
        public int Members;                     // 部員数(卒業前)
        public double MembersExpense;           // 15 × 部員数
        public int FundsAfterOps;               // 収支のあと・強化の前の資金(万円に丸めたもの)
        public int FundsAfter;                  // 自動の強化のあと(手動の強化のときは FundsAfterOps と同じ。強化は画面の操作)
        public FacilityLevels FacBefore, FacAfter;

        public double Income { get { return IncomeFormula + IncomeMod; } }
        public double Expense { get { return Staff + Upkeep + MembersExpense; } }
    }

    public static class TeamYear
    {
        public const int MinRunners = 5;                 // 在籍人数の下限(02 I-07)
        public const double QuitRate = 0.40;             // やる気 1 の選手が年度末に退部する確率(05 §10.4)
        public const double HallOfFamePower = 85.0;      // 殿堂入りの卒業時の力(05 §10.2)
        public const int HallOfFameSectionWins = 3;      // 同じく、区間賞の通算回数
        public const int HallOfFameRecords = 1;          // 同じく、区間記録の更新の通算回数

        /// <summary>実業団に進む確率 = clamp((卒業時の力 − 62) / 14, 0, 0.9)(05 §10.2)。</summary>
        public static double CorpProbability(double power)
        {
            return Math.Max(0.0, Math.Min(0.9, (power - 62.0) / 14.0));
        }

        /// <summary>進路。uCorp が実業団の抽選、残りは uOther で 教員 25% / 一般就職 55% / 院進学 20%(05 §10.2)。</summary>
        public static CareerPath ChoosePath(double power, double uCorp, double uOther)
        {
            if (uCorp < CorpProbability(power)) return CareerPath.Corporate;
            if (uOther < 0.25) return CareerPath.Teacher;
            if (uOther < 0.80) return CareerPath.Employment;
            return CareerPath.GradSchool;
        }

        /// <summary>殿堂入り(05 §10.2): 区間記録を更新した、または大会での区間賞が通算 3 回以上、または卒業時の力が 85 以上。</summary>
        public static bool IsHallOfFame(Runner r, double power)
        {
            return r.Records >= HallOfFameRecords || r.SectionWins >= HallOfFameSectionWins || power >= HallOfFamePower;
        }

        /// <summary>入学した年度(<see cref="Runner.EnrollWeek"/> から。年度は 1 始まり)。</summary>
        public static int EnrollYear(Runner r) { return Calendar.FloorDiv(r.EnrollWeek, Calendar.WeeksPerYear) + 1; }

        /// <summary>
        /// 年度末の卒業と退部(05 §12 の手順 5)。乱数は label 9・(year, 0) の 1 本: 卒業生を ID の昇順に 2 回ずつ(実業団か・その他の進路の分け)、
        /// 続けて(卒業後に残る)やる気 1 の選手を ID の昇順に 1 回ずつ(退部。在籍が <see cref="MinRunners"/> 人のときは退部させない。抽選は常に引く)。
        /// 卒業生は OB になり、残る選手は学年を 1 つ進める。選手への参照(個別指導・面談・欠場・登録・絆)も消す。
        /// </summary>
        public static YearTurnover GraduateAndQuit(TeamState s, int year, IRngFactory rngs)
        {
            var res = new YearTurnover();
            Pcg32 g = rngs.Stream(RngRegistry.LabelTeamYear, year, 0);
            foreach (Runner r in s.Runners.Where(x => x.Grade >= 4).OrderBy(x => x.Id).ToList())
            {
                double uCorp = g.Uniform(), uOther = g.Uniform();
                double power = TeamState.LongP(r.Stats);
                CareerPath path = ChoosePath(power, uCorp, uOther);
                OBRecord ob = OBRecord.FromGraduate(r, year, EnrollYear(r), path, r.HighSchool > 0 ? "hs." + r.HighSchool + ".name" : "", IsHallOfFame(r, power));
                s.Obs.Add(ob);
                res.Graduates.Add(ob);
                Remove(s, r);
            }
            foreach (Runner r in s.Runners.Where(x => x.Motivation <= 1).OrderBy(x => x.Id).ToList())
            {
                double u = g.Uniform();
                if (u < QuitRate && s.Runners.Count > MinRunners)
                {
                    res.Quit.Add(r);
                    Remove(s, r);
                }
            }
            s.Bonds.RemoveAllNotIn(new HashSet<int>(s.Runners.Select(x => x.Id)));
            foreach (Runner r in s.Runners) r.Grade++;
            return res;
        }

        private static void Remove(TeamState s, Runner r)
        {
            s.Runners.Remove(r);
            s.IndividualMenus.Remove(r.Id);
            s.Talked.Remove(r.Id);
            s.Bench.Remove(r.Id);
            s.Forced.Remove(r.Id);
            foreach (List<int> e in s.Entries) e.Remove(r.Id);
        }

        /// <summary>
        /// 年度の収支(05 §4.1)と強化(§4.2。順番と上限は <see cref="FacilityPolicy"/>)。収入 = 名声と評価ポイントの式 + <c>funds_year</c> の補正(期間中の年度末に 1 回)、
        /// 支出 = スタッフ + 施設の維持 + 部員の費用(卒業前の人数)。資金は万円の整数に丸める。<paramref name="rank"/> = 監督ランク(強化の上限を決める)。<paramref name="upgrade"/> = false のときは収支だけ(強化は画面の操作 <c>UpgradeFacility</c> / <c>UpgradeRecommended</c>)。
        /// </summary>
        public static Settlement Settle(TeamState s, double prestige, int yearPoints, int rank, bool upgrade = true)
        {
            FacilityLevels f = s.Fac;
            double formula = WorldMath.Income(prestige, yearPoints), mod = s.ModSum(ModKind.FundsYear);
            double income = formula + mod;
            double staff = WorldMath.StaffExpense(f.Coach, f.Trainer, f.Nutri, f.Scout);
            double upkeep = WorldMath.FacilityUpkeep(f.Dorm, f.Track, f.Hill, f.Gym, f.Medical);
            var rec = new Settlement
            {
                FundsBefore = s.Funds, IncomeFormula = formula, IncomeMod = mod, Staff = staff, Upkeep = upkeep,
                Members = s.Runners.Count, MembersExpense = WorldMath.RunnerExpense(s.Runners.Count), FacBefore = f,
            };
            double funds = Q6.Round(s.Funds + (income - staff - upkeep - WorldMath.RunnerExpense(s.Runners.Count)));
            rec.FundsAfterOps = Rounding.HalfUp(funds);
            if (upgrade) FacilityPolicy.Upgrade(ref f, ref funds, rank);
            s.Funds = Rounding.HalfUp(funds);
            s.Fac = f;
            rec.FundsAfter = s.Funds;
            rec.FacAfter = f;
            return rec;
        }
    }
}
