using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Container
{
    /// <summary>読み込みの失敗(09 §4.3)。Code は文書の失敗のコードと同じ文字列。</summary>
    public sealed class LoadException : Exception
    {
        public string Code { get; }

        public LoadException(string code, string detail = "") : base(code + ": " + detail) { Code = code; }
    }

    /// <summary>ヘッダだけを読んだ結果(スロットの一覧用)。</summary>
    public sealed class HeaderInfo
    {
        public JsonObject Header;
        public int Flags;
        public int PayloadOffset;        // ヘッダ部(header_crc32 まで)の長さ = payload_len の位置

        public long SaveCounter { get { return Num("save_counter", -1); } }

        public string Kind { get { return Str("kind"); } }

        public string LineageId { get { return Str("lineage_id"); } }

        public long Num(string key, long dflt)
        {
            var n = Header.Get(key) as JsonNumber;
            return n != null ? (long)n.Value : dflt;
        }

        public string Str(string key)
        {
            var s = Header.Get(key) as JsonString;
            return s != null ? s.Value : null;
        }
    }

    /// <summary>コンテナ形式 EKSV(09 §4.1)。バイトの並びはリトルエンディアン。</summary>
    public static class Eksv
    {
        private const int FlagGzip = 1;
        private static readonly byte[] Magic = { 0x45, 0x4B, 0x53, 0x56 };

        private static void PutU16(Stream s, int v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }

        private static void PutU32(Stream s, uint v) { for (int i = 0; i < 4; i++) s.WriteByte((byte)(v >> (8 * i))); }

        private static uint U32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }

        private static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }

        /// <summary>コンテナを作る。ペイロードは JSON(UTF-8)を gzip(K_GZIP_LEVEL 相当。mtime = 0)。fast = true は最速の圧縮(zlib level 1。中断セーブ用 = 同期で書くので 200 ms に収める。09 §4.6・§14.2。形式は同じ)。</summary>
        public static byte[] Encode(JsonObject header, JsonValue payload, int containerVer = SaveConstants.K_CONTAINER_VER, bool compress = true, bool fast = false)
        {
            byte[] hdr = header.ToUtf8();
            byte[] pay = payload.ToUtf8();
            int flags = 0;
            if (compress)
            {
                using (var ms = new MemoryStream())
                {
                    using (var gz = new GZipStream(ms, fast ? CompressionLevel.Fastest : CompressionLevel.Optimal, true)) gz.Write(pay, 0, pay.Length);
                    pay = ms.ToArray();
                }
                flags |= FlagGzip;
            }
            using (var o = new MemoryStream())
            {
                o.Write(Magic, 0, 4);
                PutU16(o, containerVer);
                PutU16(o, flags);
                PutU32(o, (uint)hdr.Length);
                o.Write(hdr, 0, hdr.Length);
                PutU32(o, Crc32.Compute(o.ToArray()));
                PutU32(o, (uint)pay.Length);
                o.Write(pay, 0, pay.Length);
                PutU32(o, Crc32.Compute(o.ToArray()));
                return o.ToArray();
            }
        }

        /// <summary>ヘッダだけを読む(スロットの一覧用。ペイロードは読まない)。失敗は <see cref="LoadException"/>。</summary>
        public static HeaderInfo DecodeHeader(byte[] b)
        {
            if (b.Length < 16) throw new LoadException("truncated", "短すぎる");
            for (int i = 0; i < 4; i++) if (b[i] != Magic[i]) throw new LoadException("magic");
            int ver = U16(b, 4), flags = U16(b, 6);
            uint hl = U32(b, 8);
            if (ver > SaveConstants.K_CONTAINER_VER) throw new LoadException("container_too_new", ver.ToString());
            if (hl > SaveConstants.K_HEADER_MAX) throw new LoadException("header_len", hl.ToString());
            int end = 12 + (int)hl;
            if (b.Length < end + 4) throw new LoadException("truncated", "ヘッダが途中で切れている");
            if (Crc32.Compute(new ReadOnlySpan<byte>(b, 0, end)) != U32(b, end)) throw new LoadException("header_crc");
            JsonObject header;
            try
            {
                header = JsonReader.Parse(new UTF8Encoding(false, true).GetString(b, 12, (int)hl)) as JsonObject;
            }
            catch (Exception e)
            {
                throw new LoadException("header_json", e.Message);
            }
            if (header == null) throw new LoadException("header_json", "オブジェクトでない");
            return new HeaderInfo { Header = header, Flags = flags, PayloadOffset = end + 4 };
        }

        /// <summary>全体を検証して、ヘッダとペイロード(JSON)を返す(§4.3 の手順1〜5)。</summary>
        public static HeaderInfo Decode(byte[] b, out JsonValue payload)
        {
            HeaderInfo h = DecodeHeader(b);
            int pos = h.PayloadOffset;
            if (b.Length < pos + 4) throw new LoadException("truncated", "ペイロードの長さがない");
            uint pl = U32(b, pos);
            if (pl > (uint)SaveConstants.K_PAYLOAD_MAX) throw new LoadException("payload_len", pl.ToString());
            long want = (long)pos + 4 + pl + 4;
            if (b.Length != want) throw new LoadException(b.Length < want ? "truncated" : "length", "ファイルの長さが合わない");
            if (Crc32.Compute(new ReadOnlySpan<byte>(b, 0, b.Length - 4)) != U32(b, b.Length - 4)) throw new LoadException("crc");
            byte[] pay = new byte[pl];
            Buffer.BlockCopy(b, pos + 4, pay, 0, (int)pl);
            try
            {
                if ((h.Flags & FlagGzip) != 0) pay = Inflate(pay);
                payload = JsonReader.Parse(new UTF8Encoding(false, true).GetString(pay));
            }
            catch (LoadException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new LoadException("payload", e.Message);
            }
            return h;
        }

        /// <summary>gzip の伸長。伸長後が K_PAYLOAD_MAX を超えたら止める(圧縮爆弾の対策)。</summary>
        private static byte[] Inflate(byte[] gz)
        {
            using (var src = new MemoryStream(gz))
            using (var z = new GZipStream(src, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                var buf = new byte[65536];
                long total = 0;
                int n;
                while ((n = z.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > SaveConstants.K_PAYLOAD_MAX) throw new LoadException("payload_len", "伸長後が上限を超える");
                    o.Write(buf, 0, n);
                }
                return o.ToArray();
            }
        }

        /// <summary>伸長後の上限だけを差し替えて検査するための入口(テスト用)。</summary>
        public static bool TryDecode(byte[] b, out HeaderInfo header, out JsonValue payload, out string code)
        {
            try
            {
                header = Decode(b, out payload);
                code = null;
                return true;
            }
            catch (LoadException e)
            {
                header = null;
                payload = null;
                code = e.Code;
                return false;
            }
        }
    }

    /// <summary>ヘッダ JSON の組み立て(09 §4.2。キーの順は文書のとおり)。</summary>
    public sealed class HeaderBuilder
    {
        public string Kind = "auto";
        public int Slot = 1;
        public string LineageId = "";
        public long SaveCounter = 1;
        public long? BaseCounter;
        public int SchemaVersion = SaveConstants.K_SCHEMA_VER;
        public string AppVersion = "0.0.0";
        public string MasterHash = "";
        public long SavedAt;
        public int Year = 1, MonthIndex;
        public string SchoolName = "", ScenarioId = "";
        public string Difficulty;
        public long PlaySeconds;

        public JsonObject Build()
        {
            var o = new JsonObject();
            o["kind"] = new JsonString(Kind);
            o["slot"] = new JsonNumber((long)Slot);
            o["lineage_id"] = new JsonString(LineageId);
            o["save_counter"] = new JsonNumber(SaveCounter);
            if (BaseCounter.HasValue) o["base_counter"] = new JsonNumber(BaseCounter.Value);
            o["schema_version"] = new JsonNumber((long)SchemaVersion);
            o["app_version"] = new JsonString(AppVersion);
            o["master_hash"] = new JsonString(MasterHash);
            o["saved_at"] = new JsonNumber(SavedAt);
            o["year"] = new JsonNumber((long)Year);
            o["month_index"] = new JsonNumber((long)MonthIndex);
            o["school_name"] = new JsonString(SchoolName);
            o["scenario_id"] = new JsonString(ScenarioId);
            o["difficulty"] = Difficulty == null ? (JsonValue)JsonNull.Instance : new JsonString(Difficulty);
            o["play_seconds"] = new JsonNumber(PlaySeconds);
            return o;
        }
    }
}
