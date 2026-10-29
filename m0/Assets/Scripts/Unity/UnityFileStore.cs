using System;
using System.Collections.Generic;
using System.IO;
using Ekiden.Platform;

namespace Ekiden.Unity
{
    /// <summary>
    /// 端末のファイル(09 §15.4)。根は既定で <c>Application.persistentDataPath</c>。パスは根からの相対('/' 区切り)。
    /// 書き込みは <see cref="IFileStore.WriteAtomic"/> の契約どおり: <c>{path}.tmp</c> に書く → フラッシュ(ディスクまで)→ 読み戻して verify → 置き換える → .tmp を消す。
    /// verify が false(または例外)なら .tmp を消して <see cref="IOException"/>(本物には触れない)。
    /// </summary>
    public sealed class UnityFileStore : IFileStore
    {
        private readonly string _root;

        public UnityFileStore(string root)
        {
            _root = Path.GetFullPath(root);
            Directory.CreateDirectory(_root);
        }

        /// <summary><c>Application.persistentDataPath</c> の下の <c>save/</c> を根にする。</summary>
        public static UnityFileStore Default()
        {
            return new UnityFileStore(Path.Combine(UnityEngine.Application.persistentDataPath, "save"));
        }

        public string Root { get { return _root; } }

        private string Full(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Contains("..")) throw new ArgumentException("不正なパス: " + path);
            return Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        }

        public bool Exists(string path) { return File.Exists(Full(path)); }

        public byte[] ReadAllBytes(string path)
        {
            string f = Full(path);
            if (!File.Exists(f)) throw new FileNotFoundException(path);
            return File.ReadAllBytes(f);
        }

        public void WriteAtomic(string path, byte[] bytes, Func<byte[], bool> verify)
        {
            string f = Full(path);
            string tmp = f + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);                                                     // OS のキャッシュからディスクへ
            }
            bool ok;
            try { ok = verify == null || verify(File.ReadAllBytes(tmp)); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                TryDelete(tmp);
                throw new IOException("書き込んだファイルの検証に失敗した: " + path);
            }
            Replace(tmp, f);
        }

        private static void Replace(string tmp, string f)
        {
            if (!File.Exists(f)) { File.Move(tmp, f); return; }
            try { File.Replace(tmp, f, null); }                                     // 同じボリュームの置き換え(原子的)
            catch (PlatformNotSupportedException) { File.Delete(f); File.Move(tmp, f); }   // 置き換えがない環境: A / B の世代が電源断を受け持つ(09 §4.4)
        }

        private static void TryDelete(string f)
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch (IOException) { }
        }

        public void Delete(string path)
        {
            string f = Full(path);
            if (File.Exists(f)) File.Delete(f);
        }

        public IReadOnlyList<string> List(string prefix)
        {
            var list = new List<string>();
            if (!Directory.Exists(_root)) return list;
            foreach (string f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith(prefix ?? "", StringComparison.Ordinal)) list.Add(rel);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public long FreeSpace()
        {
            try { return new DriveInfo(Path.GetPathRoot(_root)).AvailableFreeSpace; }
            catch (Exception) { return long.MaxValue; }                            // 端末によっては取れない(取れないときは書き込みの失敗で知る)
        }
    }
}
