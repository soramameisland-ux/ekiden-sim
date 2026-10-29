using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;
using Ekiden.Platform;

namespace Ekiden.App
{
    /// <summary>
    /// イベントの選択肢の『効果の要約』(08 R-15)。<c>EffectView</c>(構造)と文字列の表(<c>efx.*</c>)から 1 項目ずつ文を組む。
    /// 数値は選択肢の本文に書かず、ここで構造から作る(本文と効果がずれない。08b G-06 / L-63 ①)。
    /// 例: やる気 +1 → 『やる気+1』/ 疲労 −15 → 『疲労−15(遠方出身)』/ XP → 『MEN経験値+10』/ 資金 → 『−50万円』/ 特殊能力 → 『特殊能力 ○○(35%)』/ 補正 → 『効率×1.03(4週)』。
    /// 対象は括弧の中に『(全員)』『(主力)』などと書く。イベントの当事者(<c>r</c>)とチーム全体・資金・スカウト・指導ポイントは書かない(自明)。
    /// </summary>
    public static class EffectText
    {
        /// <summary>1 項目の長さの目安(文字数。R-15)。役割(OB に任せる効果)は 1 項目だけの選択肢なので、<see cref="RoleMax"/> まで。</summary>
        public const int ItemMax = 14, RoleMax = 20;

        /// <summary>効果 1 つ。</summary>
        public static string Of(EffectView e, ILocalization loc)
        {
            string head;
            var parts = new List<string>();                                         // 括弧の中: 確率・期間・対象(この順)
            switch (e.Op)
            {
                case EffectOp.Mot: head = loc.Get("efx.mot", "v", Signed(e.Amount)); break;
                case EffectOp.Fat: head = loc.Get("efx.fat", "v", Signed(e.Amount)); break;
                case EffectOp.Bond:
                case EffectOp.Bondg: head = loc.Get("efx.bond", "v", Signed(e.Amount)); break;
                case EffectOp.Funds: head = loc.Get("efx.funds", "v", SignedMoney(e.Amount)); break;
                case EffectOp.Scout: head = loc.Get("efx.scout", "v", Signed(e.Amount)); break;
                case EffectOp.Guide: head = loc.Get("efx.guide", "v", Signed(e.Amount)); break;
                case EffectOp.Xp:
                    string v = SignedPlain(e.Xu / 10.0);                                     // xu ÷ 10(割り切れなければ小数 1 桁)
                    if (e.StatMode == XpStatMode.Fixed) head = loc.Get("efx.xp", "stat", loc.Get("stat." + e.Stat.Value + ".abbr"), "v", v);
                    else head = loc.Get(e.StatMode == XpStatMode.Weakest ? "efx.xp.weakest" : "efx.xp.random", "v", v);
                    break;
                case EffectOp.Skill: head = loc.Get("efx.skill", "name", loc.Get(e.SkillId + ".name")); break;
                case EffectOp.Inj: head = loc.Get("efx.inj." + e.Severity.Value.ToString().ToLowerInvariant()); break;
                case EffectOp.Mod:
                    head = ModText(e.Mod.Value, e.ModValue, loc);
                    if (e.Weeks > 0) parts.Add(loc.Get("efx.weeks", "n", e.Weeks.ToString(CultureInfo.InvariantCulture)));
                    break;
                case EffectOp.ObRole: head = loc.Get("efx.role." + e.Role.Value.ToString().ToLowerInvariant(), "mod", ModText(e.Mod.Value, e.ModValue, loc)); break;
                case EffectOp.Captain: head = loc.Get("efx.captain"); break;
                case EffectOp.Bench: head = loc.Get("efx.bench"); break;
                default: head = loc.Get("efx.entry"); break;
            }
            if (e.Probability.HasValue) parts.Insert(0, loc.Get("efx.prob", "p", Rounding.HalfUp(e.Probability.Value * 100.0).ToString(CultureInfo.InvariantCulture)));
            if (e.To != Target.None && e.To != Target.Team && e.To != Target.R) parts.Add(loc.Get("efx.to." + e.To.ToString().ToLowerInvariant()));
            if (parts.Count == 0) return head;
            return head + loc.Get("efx.paren", "parts", string.Join(loc.Get("efx.sep"), parts));
        }

        /// <summary>選択肢の要約 1 行(最大 3 項目 + 『ほか n件』)。</summary>
        public static string Summary(ChoiceView c, ILocalization loc)
        {
            var items = new List<string>();
            foreach (EffectView e in c.Effects) items.Add(Of(e, loc));
            string line = string.Join(loc.Get("efx.join"), items);
            if (c.MoreEffects > 0) line += loc.Get("efx.more", "n", c.MoreEffects.ToString(CultureInfo.InvariantCulture));
            return line;
        }

        private static string ModText(ModKind kind, double value, ILocalization loc)
        {
            switch (kind)
            {
                case ModKind.EMult: return loc.Get("efx.mod.e_mult", "v", Plain(value));
                case ModKind.InjMult: return loc.Get("efx.mod.inj_mult", "v", Plain(value));
                case ModKind.RAdd: return loc.Get("efx.mod.r_add", "v", Signed(Rounding.HalfUp(value * 100.0)) + "%");
                case ModKind.FormAdd: return loc.Get("efx.mod.form_add", "v", Signed(Rounding.HalfUp(value)));
                case ModKind.FundsYear: return loc.Get("efx.mod.funds_year", "v", SignedMoney(Rounding.HalfUp(value)));
                default: return loc.Get("efx.mod.scout_pts", "v", Signed(Rounding.HalfUp(value)));
            }
        }

        /// <summary>+1 / −15(マイナスは Fmt.Minus)。</summary>
        public static string Signed(long n) { return (n > 0 ? "+" : n < 0 ? Fmt.Minus : "") + Math.Abs(n).ToString(CultureInfo.InvariantCulture); }

        public static string SignedMoney(long man) { return man > 0 ? "+" + Fmt.Money(man) : Fmt.Money(man); }

        /// <summary>倍率など(小数 2 桁まで・末尾の 0 は書かない)。</summary>
        public static string Plain(double x) { return (Rounding.HalfUp(x * 100.0) / 100.0).ToString("0.##", CultureInfo.InvariantCulture); }

        /// <summary>符号つきの小数(経験値の xu ÷ 10 など。小数 2 桁まで)。</summary>
        public static string SignedPlain(double x)
        {
            string body = Plain(Math.Abs(x));
            return (x > 0 ? "+" : x < 0 ? Fmt.Minus : "") + body;
        }
    }
}
