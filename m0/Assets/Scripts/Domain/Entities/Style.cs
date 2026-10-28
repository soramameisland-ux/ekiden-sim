using System;

namespace Ekiden.Domain.Entities
{
    /// <summary>個性(型・走法・展開)。コードは 3 文字(例 SPC)。中立は BFE(10 §2.1)。</summary>
    public readonly struct Style : IEquatable<Style>
    {
        public StyleForm Form { get; }
        public StyleRun Run { get; }
        public StyleFlow Flow { get; }

        public Style(StyleForm form, StyleRun run, StyleFlow flow)
        {
            Form = form; Run = run; Flow = flow;
        }

        public static readonly Style Neutral = new Style(StyleForm.Balance, StyleRun.Free, StyleFlow.Even);

        public string Code
        {
            get { return new string(new[] { "SBL"[(int)Form], "PFS"[(int)Run], "CEL"[(int)Flow] }); }
        }

        /// <summary>3 文字から読む。不正なら false(セーブの読み込みでは I-21 の修復で中立にする)。</summary>
        public static bool TryParse(string code, out Style style)
        {
            style = Neutral;
            if (code == null || code.Length != 3) return false;
            int f = "SBL".IndexOf(code[0]), r = "PFS".IndexOf(code[1]), l = "CEL".IndexOf(code[2]);
            if (f < 0 || r < 0 || l < 0) return false;
            style = new Style((StyleForm)f, (StyleRun)r, (StyleFlow)l);
            return true;
        }

        public bool Equals(Style other) { return Form == other.Form && Run == other.Run && Flow == other.Flow; }
        public override bool Equals(object obj) { return obj is Style s && Equals(s); }
        public override int GetHashCode() { return ((int)Form * 3 + (int)Run) * 3 + (int)Flow; }
        public override string ToString() { return Code; }
    }
}
