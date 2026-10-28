using System.Collections.Generic;

namespace Ekiden.Domain.Game
{
    /// <summary>画面からの操作の結果(08 F4: 無効な操作は消さずに無効表示にし、選ぶと理由を 1 行で出す)。
    /// <see cref="NeedsConfirm"/> は、操作そのものは可能だが確認が要る(<see cref="Warnings"/> を見せて、確認ずみで同じ操作をもう一度呼ぶ。08 §7.6)。</summary>
    public sealed class OpResult
    {
        public bool Ok { get; private set; }
        public bool NeedsConfirm { get; private set; }
        public string Reason { get; private set; }
        public IReadOnlyList<string> Warnings { get; private set; }

        public static readonly OpResult Success = new OpResult { Ok = true, Reason = "", Warnings = new string[0] };

        public static OpResult Fail(string reason) { return new OpResult { Ok = false, Reason = reason, Warnings = new string[0] }; }

        public static OpResult Confirm(IReadOnlyList<string> warnings) { return new OpResult { Ok = false, NeedsConfirm = true, Reason = string.Join(" / ", warnings), Warnings = warnings }; }

        public override string ToString() { return Ok ? "OK" : (NeedsConfirm ? "確認: " : "不可: ") + Reason; }
    }
}
