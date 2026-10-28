using System;
using System.Collections.Generic;

namespace Ekiden.Platform
{
    /// <summary>ファイルの保存先(09 §15.4)。パスは root からの相対('/' 区切り)。書き込みは必ず <see cref="WriteAtomic"/>(一時ファイル → 検証 → 置き換え。§4.4)。</summary>
    public interface IFileStore
    {
        bool Exists(string path);

        /// <summary>ファイルを全部読む。なければ <see cref="System.IO.FileNotFoundException"/>。</summary>
        byte[] ReadAllBytes(string path);

        /// <summary>{path}.tmp に書く → フラッシュ → 読み戻して verify に通す → path へ置き換える → .tmp を消す。verify が false(または例外)なら .tmp を消して <see cref="System.IO.IOException"/>。</summary>
        void WriteAtomic(string path, byte[] bytes, Func<byte[], bool> verify);

        void Delete(string path);

        /// <summary>prefix で始まるパスの一覧(昇順)。</summary>
        IReadOnlyList<string> List(string prefix);

        /// <summary>空き容量(バイト)。</summary>
        long FreeSpace();
    }
}
