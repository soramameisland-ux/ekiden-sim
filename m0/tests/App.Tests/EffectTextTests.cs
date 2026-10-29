using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ekiden.App;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Game;
using Ekiden.Domain.Views;
using Ekiden.Domain.World;
using Ekiden.Persistence.Strings;
using Ekiden.Platform;
using NUnit.Framework;

namespace Ekiden.App.Tests
{
    /// <summary>選択肢の効果の要約(08 R-15。L-63 ①): 数値は構造から作り、本文には書かない。</summary>
    public class EffectTextTests
    {
        private static string Find(string relative)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string p = Path.Combine(dir, relative);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException(relative);
        }

        private static ILocalization Loc()
        {
            return new TableLocalization(StringTable.Parse(File.ReadAllText(Find("Assets/Strings/ja.json"), new UTF8Encoding(false))), true, null);
        }

        private static EffectView V(EffectOp op, Target to = Target.R, int amount = 0, double? p = null) { return new EffectView { Op = op, To = to, Amount = amount, Probability = p }; }

        [Test]
        public void SpecExamples_08_R15()
        {
            ILocalization loc = Loc();
            Assert.AreEqual("やる気+1", EffectText.Of(V(EffectOp.Mot, Target.R, 1), loc));
            Assert.AreEqual("やる気+1(全員)", EffectText.Of(V(EffectOp.Mot, Target.All, 1), loc));
            Assert.AreEqual("疲労−15(遠方出身)", EffectText.Of(V(EffectOp.Fat, Target.Far, -15), loc));
            Assert.AreEqual("やる気+1(50%)", EffectText.Of(V(EffectOp.Mot, Target.R, 1, 0.5), loc));
            Assert.AreEqual("やる気+1(50%・全員)", EffectText.Of(V(EffectOp.Mot, Target.All, 1, 0.5), loc));
            Assert.AreEqual("MEN経験値+10", EffectText.Of(new EffectView { Op = EffectOp.Xp, To = Target.R, StatMode = XpStatMode.Fixed, Stat = Stat.MEN, Xu = 100 }, loc));
            Assert.AreEqual("弱点経験値+25", EffectText.Of(new EffectView { Op = EffectOp.Xp, To = Target.R, StatMode = XpStatMode.Weakest, Xu = 250 }, loc));
            Assert.AreEqual("MEN経験値+10.5(主力)", EffectText.Of(new EffectView { Op = EffectOp.Xp, To = Target.Top10, StatMode = XpStatMode.Fixed, Stat = Stat.MEN, Xu = 105 }, loc), "xu ÷ 10 が割り切れなければ小数");
            Assert.AreEqual("−50万円", EffectText.Of(V(EffectOp.Funds, Target.None, -50), loc));
            Assert.AreEqual("+200万円", EffectText.Of(V(EffectOp.Funds, Target.None, 200), loc));
            Assert.AreEqual("特殊能力 エース区間○(35%)", EffectText.Of(new EffectView { Op = EffectOp.Skill, To = Target.R, SkillId = "skill.ace_section", Probability = 0.35 }, loc));
            Assert.AreEqual("効率×1.03(4週)", EffectText.Of(new EffectView { Op = EffectOp.Mod, To = Target.Team, Mod = ModKind.EMult, ModValue = 1.03, Weeks = 4 }, loc));
            Assert.AreEqual("故障率×0.8(8週)", EffectText.Of(new EffectView { Op = EffectOp.Mod, To = Target.R, Mod = ModKind.InjMult, ModValue = 0.8, Weeks = 8 }, loc));
            Assert.AreEqual("回復+2%(24週)", EffectText.Of(new EffectView { Op = EffectOp.Mod, To = Target.Team, Mod = ModKind.RAdd, ModValue = 0.02, Weeks = 24 }, loc));
            Assert.AreEqual("故障率×0.8(50%・8週・全員)", EffectText.Of(new EffectView { Op = EffectOp.Mod, To = Target.All, Mod = ModKind.InjMult, ModValue = 0.8, Weeks = 8, Probability = 0.5 }, loc), "括弧の中は 確率 → 期間 → 対象");
            Assert.AreEqual("調子+2(3週)", EffectText.Of(new EffectView { Op = EffectOp.Mod, To = Target.R, Mod = ModKind.FormAdd, ModValue = 2, Weeks = 3 }, loc));
            Assert.AreEqual("指導Pt−1", EffectText.Of(V(EffectOp.Guide, Target.None, -1), loc));
            Assert.AreEqual("スカウト+2", EffectText.Of(V(EffectOp.Scout, Target.None, 2), loc));
            Assert.AreEqual("絆+15(全員)", EffectText.Of(V(EffectOp.Bondg, Target.All, 15), loc));
            Assert.AreEqual("OBコーチ:効率×1.06", EffectText.Of(new EffectView { Op = EffectOp.ObRole, Role = ObRole.Coach, Mod = ModKind.EMult, ModValue = 1.06, Weeks = 48 }, loc));
            Assert.AreEqual("OB支援:年収+200万円", EffectText.Of(new EffectView { Op = EffectOp.ObRole, Role = ObRole.Sponsor, Mod = ModKind.FundsYear, ModValue = 200, Weeks = 48 }, loc));
        }

        [Test]
        public void Summary_ShowsThreeItems_AndTheRestAsMore()
        {
            ILocalization loc = Loc();
            var c = new ChoiceView();
            c.Effects.Add(V(EffectOp.Funds, Target.None, -800)); c.Effects.Add(V(EffectOp.Mot, Target.Top10, 1)); c.Effects.Add(V(EffectOp.Fat, Target.All, -5));
            c.MoreEffects = 2;
            Assert.AreEqual("−800万円、やる気+1(主力)、疲労−5(全員)(ほか2件)", EffectText.Summary(c, loc));
            c.MoreEffects = 0;
            Assert.AreEqual("−800万円、やる気+1(主力)、疲労−5(全員)", EffectText.Summary(c, loc));
        }

        /// <summary>カタログの全効果(3 項目を超えるぶんも)に文があり、キーの欠落がなく、構造の数値(量・経験値・補正の値と期間・確率)がそのまま書かれ、長さが目安に収まる。</summary>
        [Test]
        public void EveryEffectInTheCatalog_HasAText_WithItsNumbers_WithinTheLength()
        {
            ILocalization loc = Loc();
            GameSession gs = GameSession.New(1UL, Difficulty.Mid);
            int n = 0, longest = 0;
            foreach (EventDef def in gs.Catalog.Events)
                foreach (ChoiceDef c in def.Choices)
                    foreach (Effect e in c.Fx)
                    {
                        EffectView ev = gs.EffectOf(e);
                        string t = EffectText.Of(ev, loc);
                        string tag = def.Id + "." + c.Id + " " + e.Op + " → " + t;
                        Assert.IsFalse(string.IsNullOrEmpty(t), tag);
                        Assert.IsFalse(t.Contains("⟦") || t.Contains("？"), tag);
                        Assert.LessOrEqual(t.Length, e.Op == EffectOp.ObRole ? EffectText.RoleMax : EffectText.ItemMax, tag);
                        longest = Math.Max(longest, t.Length);
                        switch (e.Op)
                        {
                            case EffectOp.Mot: case EffectOp.Fat: case EffectOp.Bond: case EffectOp.Bondg: case EffectOp.Scout: case EffectOp.Guide:
                                StringAssert.Contains(Math.Abs(e.D).ToString(), t, tag); break;
                            case EffectOp.Funds: StringAssert.Contains(Math.Abs(e.D) + "万円", t.Replace(",", ""), tag); break;
                            case EffectOp.Xp: StringAssert.Contains((e.Xu / 10).ToString(), t, tag); break;
                            case EffectOp.Mod:
                                StringAssert.Contains(e.Weeks + "週", t, tag);
                                if (e.Kind == ModKind.EMult || e.Kind == ModKind.InjMult) StringAssert.Contains(("" + e.Value).TrimEnd('0'), t.Replace("0.80", "0.8"), tag);
                                break;
                        }
                        if (e.P.HasValue) StringAssert.Contains(((int)Math.Round(e.P.Value * 100)) + "%", t, tag);
                        n++;
                    }
            Assert.Greater(n, 100, "試験の前提: 効果が十分にある");
            TestContext.WriteLine("効果 " + n + " 件、最長 " + longest + " 文字");
        }

        /// <summary>選択肢の本文に数字を書かない(効果の数値は要約が構造から作る)。カタログの本文も、表(ja.json)の本文も。</summary>
        [Test]
        public void ChoiceLabels_CarryNoNumbers()
        {
            GameSession gs = GameSession.New(1UL, Difficulty.Mid);
            var table = StringTable.Parse(File.ReadAllText(Find("Assets/Strings/ja.json"), new UTF8Encoding(false)));
            int n = 0;
            foreach (EventDef def in gs.Catalog.Events)
                foreach (ChoiceDef c in def.Choices)
                {
                    Assert.IsFalse(c.Label.Any(ch => char.IsDigit(ch)), def.Id + "." + c.Id + " (カタログ): " + c.Label);
                    string shown = table[def.Id + ".choice." + c.Id];
                    Assert.IsFalse(shown.Any(ch => char.IsDigit(ch)), def.Id + "." + c.Id + " (表): " + shown);
                    n++;
                }
            Assert.GreaterOrEqual(n, 80);
        }

        [Test]
        public void TheAwakeningAndRoleEffects_AreComposedFromTheStructure()
        {
            ILocalization loc = Loc();
            GameSession gs = GameSession.New(1UL, Difficulty.Mid);
            foreach (EventDef def in gs.Catalog.Events.Where(d => d.Choices.Any(c => c.Fx.Any(x => x.Op == EffectOp.ObRole))))
                foreach (ChoiceDef c in def.Choices)
                    foreach (Effect e in c.Fx.Where(x => x.Op == EffectOp.ObRole))
                    {
                        EffectView ev = gs.EffectOf(e);
                        Assert.AreEqual(e.Role, ev.Role); Assert.IsNotNull(ev.Mod, def.Id);
                        ObRoles.ModOf(e.Role, out ModKind k, out double val);
                        Assert.AreEqual(k, ev.Mod); Assert.AreEqual(val, ev.ModValue, "役割が作る補正と同じ値");
                    }
        }
    }
}
