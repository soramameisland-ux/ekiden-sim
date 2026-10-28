using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ekiden.Platform
{
    /// <summary>電源断を注入できるメモリ上のファイルストア(テスト・SimCli 用。参照モデルの <c>Store</c> と同じ手順)。</summary>
    public sealed class MemoryFileStore : IFileStore
    {
        private readonly SortedDictionary<string, byte[]> _files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        private int _ops;
        private int _crashAt = -1;

        /// <summary>書き込みの手順の数(<see cref="WriteAtomic"/> 1 回 = 5。tmp-open・tmp-half・tmp-full・verified・renamed)。</summary>
        public const int StepsPerWrite = 5;

        /// <summary>空き容量。0 以下にすると書き込みが失敗する(ディスクの空き不足の注入)。</summary>
        public long Free = long.MaxValue;

        /// <summary>これから n 番目の手順で電源が落ちたことにする(n = 1〜5)。<see cref="PowerCutException"/> を投げる。</summary>
        public void CrashAt(int n)
        {
            _ops = 0;
            _crashAt = n;
        }

        public void ClearCrash() { _crashAt = -1; }

        private void Tick()
        {
            _ops++;
            if (_crashAt >= 0 && _ops == _crashAt) throw new PowerCutException();
        }

        public bool Exists(string path) { return _files.ContainsKey(path); }

        public byte[] ReadAllBytes(string path)
        {
            byte[] b;
            if (!_files.TryGetValue(path, out b)) throw new FileNotFoundException(path);
            return (byte[])b.Clone();
        }

        public void WriteAtomic(string path, byte[] bytes, Func<byte[], bool> verify)
        {
            if (Free <= 0) throw new IOException("空き容量が足りない");
            string tmp = path + ".tmp";
            Tick();                                                  // tmp-open
            _files[tmp] = bytes.Take(bytes.Length / 2).ToArray();    // 書きかけ(途中で止まりうる)
            Tick();                                                  // tmp-half
            _files[tmp] = (byte[])bytes.Clone();
            Tick();                                                  // tmp-full
            bool ok;
            try { ok = verify == null || verify(_files[tmp]); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                _files.Remove(tmp);
                throw new IOException("書き込んだファイルの検証に失敗した");
            }
            Tick();                                                  // verified
            _files[path] = _files[tmp];                              // 置き換え(同じ場所の rename は原子的)
            Tick();                                                  // renamed
            _files.Remove(tmp);
        }

        public void Delete(string path) { _files.Remove(path); }

        public IReadOnlyList<string> List(string prefix)
        {
            return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        public long FreeSpace() { return Free; }

        /// <summary>テスト用: 任意の中身を直接置く(非原子な置き換えの途中や、壊れたファイルを作る)。</summary>
        public void PutRaw(string path, byte[] bytes) { _files[path] = (byte[])bytes.Clone(); }

        public MemoryFileStore Clone()
        {
            var c = new MemoryFileStore { Free = Free };
            foreach (var kv in _files) c._files[kv.Key] = (byte[])kv.Value.Clone();
            return c;
        }
    }

    /// <summary>電源断(テスト用)。ゲームの規則からは捕まえない。</summary>
    public sealed class PowerCutException : Exception
    {
        public PowerCutException() : base("電源断") { }
    }
}
