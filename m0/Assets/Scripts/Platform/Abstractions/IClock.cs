using System;
using System.Diagnostics;

namespace Ekiden.Platform
{
    /// <summary>時計(09 §15.4)。Domain は時刻を読まない(D-2)ので、これを使うのは App / Persistence の層だけ(セーブの日時・プレイ時間・書き込みの再試行の待ち)。</summary>
    public interface IClock
    {
        /// <summary>UTC の秒(UNIX 時刻)。保存の日時などの表示用で、ゲームの規則には使わない。</summary>
        long UtcNow();

        /// <summary>単調に増える秒(端末の起動からなど)。経過時間の計測用。時計の巻き戻しの影響を受けない。</summary>
        double MonotonicSeconds();
    }

    /// <summary>実際の時計。</summary>
    public sealed class SystemClock : IClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public long UtcNow() { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        public double MonotonicSeconds() { return _sw.Elapsed.TotalSeconds; }
    }
}
