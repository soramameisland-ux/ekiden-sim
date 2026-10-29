using System.Collections.Generic;
using Ekiden.Domain.Game;
using Ekiden.Platform;

namespace Ekiden.App
{
    /// <summary>
    /// 操作の理由・警告の文(L-63 ③)。Domain の <see cref="OpResult"/> はキーと差し込みの値(<see cref="Msg"/>)だけを持つので、ここで文字列テーブルから本文にする。
    /// 本文は <c>err.*</c>(失敗)・<c>warn.*</c>(確認が要る警告)・<c>hint.*</c>(可能なときの一言)。入れ子の理由(<see cref="Msg.Why"/>)は先に文にして <c>{why}</c> に入れる。
    /// </summary>
    public static class OpText
    {
        /// <summary>1 つの文。</summary>
        public static string Of(Msg m, ILocalization loc)
        {
            var args = new List<string>(m.Args);
            if (m.Why != null) { args.Add("why"); args.Add(Of(m.Why, loc)); }
            return loc.Get(m.Code, args.ToArray());
        }

        /// <summary>失敗の理由(成功なら空。08 F4 の『理由を 1 行で』)。</summary>
        public static string Reason(OpResult r, ILocalization loc) { return r.Error == null ? "" : Of(r.Error, loc); }

        /// <summary>確認が要るときの警告(08 §7.6)。</summary>
        public static IReadOnlyList<string> Warnings(OpResult r, ILocalization loc)
        {
            var list = new List<string>();
            foreach (Msg w in r.Warnings) list.Add(Of(w, loc));
            return list;
        }
    }
}
