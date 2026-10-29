using System.Collections.Generic;

namespace Ekiden.Domain.Game
{
    /// <summary>画面からの操作の結果(08 F4: 無効な操作は消さずに無効表示にし、選ぶと理由を 1 行で出す)。
    /// 理由と警告は日本語の本文ではなく、文字列テーブルのキーと差し込みの値(<see cref="Msg"/>。L-63 ③)で、App の <c>OpText</c> が表から文にする。
    /// <see cref="NeedsConfirm"/> は、操作そのものは可能だが確認が要る(<see cref="Warnings"/> を見せて、確認ずみで同じ操作をもう一度呼ぶ。08 §7.6)。</summary>
    public sealed class OpResult
    {
        public bool Ok { get; private set; }
        public bool NeedsConfirm { get; private set; }

        /// <summary>失敗の理由(成功なら null)。確認が要るときは最初の警告。</summary>
        public Msg Error { get; private set; }

        /// <summary>理由のキー(成功なら空)。試験・分岐用。</summary>
        public string Code { get { return Error == null ? "" : Error.Code; } }

        /// <summary>診断用の文字列(キー + 差し込み)。画面の本文ではない(本文は <c>OpText.Reason</c>)。</summary>
        public string Reason { get { return Error == null ? "" : Error.ToString(); } }

        public IReadOnlyList<Msg> Warnings { get; private set; }

        public static readonly OpResult Success = new OpResult { Ok = true, Warnings = new Msg[0] };

        public static OpResult Fail(ErrDef def, params object[] values) { return Fail(def.Make(values)); }

        public static OpResult Fail(Msg why) { return new OpResult { Ok = false, Error = why, Warnings = new Msg[0] }; }

        public static OpResult Confirm(IReadOnlyList<Msg> warnings) { return new OpResult { Ok = false, NeedsConfirm = true, Error = warnings.Count > 0 ? warnings[0] : null, Warnings = warnings }; }

        public override string ToString() { return Ok ? "OK" : (NeedsConfirm ? "confirm: " : "fail: ") + (Error == null ? "" : Error.ToString()); }
    }
}
