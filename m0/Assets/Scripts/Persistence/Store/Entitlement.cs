using System;

namespace Ekiden.Persistence.Store
{
    /// <summary>ストアの答え(07 §8.1。<c>IEntitlementProvider</c> が返す)。</summary>
    public enum StoreAnswer { Owned, NotOwned, Unknown }

    /// <summary>購入状態のキャッシュ(<c>LocalState.entitlements[pack_id]</c>。端末ごと・同期しない。09 §3.3)。</summary>
    public sealed class EntitlementCache
    {
        public bool Owned;
        public double VerifiedAt;               // 確認した時刻(UTC 秒。端末の時計)
        public double VerifiedRun;              // 確認の時点の run_total_s(単調時計で数えたアプリを動かした累計秒)

        public EntitlementCache Clone() { return new EntitlementCache { Owned = Owned, VerifiedAt = VerifiedAt, VerifiedRun = VerifiedRun }; }
    }

    /// <summary>判定の結果。State: true = 購入済み / false = 返金・未購入 / null = 未確認(付与も停止もしない)。</summary>
    public struct EntitlementResult
    {
        public bool? State;
        public double Hw;                       // 新しい高水位(これまでに見た最大の時刻)
        public EntitlementCache Cache;          // 新しいキャッシュ(ストアに確認できたときだけ作り直す)
    }

    /// <summary>購入状態の判定(07 §8.2)と時計対策(09 §10)。ゲームの結果・実績・既に得た券・選んだ見た目には影響しない(影響するのは黄金世代プランの付与の可否だけ)。</summary>
    public static class Entitlement
    {
        public const double DaySeconds = 86400.0;

        /// <summary>07 §8.2 の判定(時計が正しいとき)。cacheOwned / cacheDay はキャッシュの所有と確認した日。</summary>
        public static bool? State(StoreAnswer provider, bool hasCache, bool cacheOwned, double cacheDay, double nowDay)
        {
            if (provider == StoreAnswer.Owned) return true;
            if (provider == StoreAnswer.NotOwned) return false;
            if (hasCache && cacheOwned && nowDay - cacheDay <= SaveConstants.K_GRACE_DAYS) return true;
            return null;
        }

        /// <summary>09 §10.2 の判定。now = 端末の時刻(UTC 秒)/ hw = 見た最大の時刻 / runTotal = アプリを動かした累計秒(単調時計)。</summary>
        public static EntitlementResult Guarded(StoreAnswer provider, EntitlementCache cache, double now, double hw, double runTotal)
        {
            if (provider != StoreAnswer.Unknown)                       // ストアに確認できた = 時刻の基準を取り直す
            {
                bool owned = provider == StoreAnswer.Owned;
                return new EntitlementResult { State = owned, Hw = now, Cache = new EntitlementCache { Owned = owned, VerifiedAt = now, VerifiedRun = runTotal } };
            }
            double newHw = Math.Max(hw, now);
            var res = new EntitlementResult { State = null, Hw = newHw, Cache = cache };
            if (now + SaveConstants.K_CLOCK_BACK_TOL_S < hw) return res;                // 巻き戻された(許容 24 時間を超える)
            if (cache == null || !cache.Owned) return res;
            if (runTotal - cache.VerifiedRun > SaveConstants.K_GRACE_RUN_H * 3600.0) return res;
            if (now - cache.VerifiedAt <= SaveConstants.K_GRACE_DAYS * DaySeconds) res.State = true;
            return res;
        }
    }
}
