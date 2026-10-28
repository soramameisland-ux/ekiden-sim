using System.Collections.Generic;

namespace Ekiden.Persistence
{
    /// <summary>09 §2 の定数表。名前は文書の `K_…` と同じ(L-37)。<c>SaveConstants.All</c> が文書の表と一致することをテストで確かめる。</summary>
    public static class SaveConstants
    {
        public const int K_SLOTS = 3;
        public const int K_AUTO_GENERATIONS = 2;
        public const string K_MAGIC = "EKSV";
        public const int K_CONTAINER_VER = 1;
        public const int K_SCHEMA_VER = 1;
        public const int K_GZIP_LEVEL = 6;
        public const int K_SIZE_TARGET = 1048576;
        public const int K_SIZE_HARD = 3145728;
        public const int K_TICKET_LOG_MAX = 50;
        public const int K_EVENT_LOG_MAX = 50;
        public const int K_NOTIFY_MAX = 30;
        public const int K_CLOCK_BACK_TOL_S = 86400;
        public const int K_GRACE_DAYS = 30;
        public const int K_GRACE_RUN_H = 120;
        public const int K_HEADER_MAX = 65536;
        public const int K_PAYLOAD_MAX = 67108864;
        public const int K_BACKUP_MAX = 5;
        public const int K_LOG_MAX_BYTES = 1048576;
        public const int K_STORE_TIMEOUT_S = 5;
        public const int K_SAVE_RETRY_S = 1;
        public const int K_ACH_RETRY_S = 60;
        public const int K_SYNC_MIN_S = 60;
        public const int K_DOMAIN_CALL_MS = 100;

        public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
        {
            { nameof(K_SLOTS), K_SLOTS.ToString() },
            { nameof(K_AUTO_GENERATIONS), K_AUTO_GENERATIONS.ToString() },
            { nameof(K_MAGIC), K_MAGIC.ToString() },
            { nameof(K_CONTAINER_VER), K_CONTAINER_VER.ToString() },
            { nameof(K_SCHEMA_VER), K_SCHEMA_VER.ToString() },
            { nameof(K_GZIP_LEVEL), K_GZIP_LEVEL.ToString() },
            { nameof(K_SIZE_TARGET), K_SIZE_TARGET.ToString() },
            { nameof(K_SIZE_HARD), K_SIZE_HARD.ToString() },
            { nameof(K_TICKET_LOG_MAX), K_TICKET_LOG_MAX.ToString() },
            { nameof(K_EVENT_LOG_MAX), K_EVENT_LOG_MAX.ToString() },
            { nameof(K_NOTIFY_MAX), K_NOTIFY_MAX.ToString() },
            { nameof(K_CLOCK_BACK_TOL_S), K_CLOCK_BACK_TOL_S.ToString() },
            { nameof(K_GRACE_DAYS), K_GRACE_DAYS.ToString() },
            { nameof(K_GRACE_RUN_H), K_GRACE_RUN_H.ToString() },
            { nameof(K_HEADER_MAX), K_HEADER_MAX.ToString() },
            { nameof(K_PAYLOAD_MAX), K_PAYLOAD_MAX.ToString() },
            { nameof(K_BACKUP_MAX), K_BACKUP_MAX.ToString() },
            { nameof(K_LOG_MAX_BYTES), K_LOG_MAX_BYTES.ToString() },
            { nameof(K_STORE_TIMEOUT_S), K_STORE_TIMEOUT_S.ToString() },
            { nameof(K_SAVE_RETRY_S), K_SAVE_RETRY_S.ToString() },
            { nameof(K_ACH_RETRY_S), K_ACH_RETRY_S.ToString() },
            { nameof(K_SYNC_MIN_S), K_SYNC_MIN_S.ToString() },
            { nameof(K_DOMAIN_CALL_MS), K_DOMAIN_CALL_MS.ToString() },
        };
    }
}
