using System.Globalization;
using System.Text;

namespace Ekiden.Domain.Game
{
    /// <summary>
    /// 画面に出す 1 つの文(理由・警告)を、文字列テーブルのキーと差し込みの値で表したもの(L-63 ③)。Domain は日本語の本文を持たず、App の <c>OpText</c> が表から文にする。
    /// <see cref="Args"/> は <c>ILocalization.Get</c> と同じ並び(名前, 値, 名前, 値 …)。<see cref="Why"/> は、本文の <c>{why}</c> に入る入れ子の文(例: 『2 週目: 走行量は必須』)。
    /// </summary>
    public sealed class Msg
    {
        public readonly string Code;
        public readonly string[] Args;
        public readonly Msg Why;

        public Msg(string code, string[] args, Msg why = null) { Code = code; Args = args ?? new string[0]; Why = why; }

        /// <summary>診断用(ログ・例外・試験の失敗の表示)。画面の本文ではない。</summary>
        public override string ToString()
        {
            var sb = new StringBuilder(Code);
            if (Args.Length > 0)
            {
                sb.Append('(');
                for (int i = 0; i + 1 < Args.Length; i += 2) sb.Append(i > 0 ? ", " : "").Append(Args[i]).Append('=').Append(Args[i + 1]);
                sb.Append(')');
            }
            if (Why != null) sb.Append(" <- ").Append(Why);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 文の定義(キーと、差し込みの名前)。<see cref="OpErr"/> に 1 件ずつ置く。<c>why</c> という名前の引数は入れ子の <see cref="Msg"/>(<see cref="Msg.Why"/>)で、差し込みの値にはならない。
    /// <see cref="Internal"/> は開発者向け(中断セーブの記録の検証など)で、画面の本文を持たない。
    /// </summary>
    public sealed class ErrDef
    {
        public readonly string Code;
        public readonly string[] ArgNames;
        public readonly bool Internal;

        public ErrDef(string code, bool isInternal, string[] argNames) { Code = code; Internal = isInternal; ArgNames = argNames; }

        /// <summary>値は <see cref="ArgNames"/> と同じ順・同じ数。数は不変の書式、<c>why</c> は <see cref="Msg"/>。</summary>
        public Msg Make(params object[] values)
        {
            if (values.Length != ArgNames.Length) throw new System.ArgumentException(Code + ": 引数は " + ArgNames.Length + " 個(" + values.Length + " 個渡された)");
            var args = new System.Collections.Generic.List<string>();
            Msg why = null;
            for (int i = 0; i < values.Length; i++)
            {
                if (ArgNames[i] == "why") { why = (Msg)values[i]; continue; }
                args.Add(ArgNames[i]);
                args.Add(values[i] as string ?? System.Convert.ToString(values[i], CultureInfo.InvariantCulture));
            }
            return new Msg(Code, args.ToArray(), why);
        }
    }
}
