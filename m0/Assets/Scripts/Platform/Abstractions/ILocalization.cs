namespace Ekiden.Platform
{
    /// <summary>
    /// 画面の文字列(09 §12.3・§15.4)。キー(<c>scr.home.title</c> など)から本文を引き、本文の <c>{名前}</c> を呼び出しの引数で差し込む。
    /// 数値・時間は呼び出し側が 09 §13 の整形(<c>Fmt</c>)で文字列にして渡す。学校・選手の名前は表ではなくデータ(呼び出し側が渡す)。
    /// </summary>
    public interface ILocalization
    {
        /// <summary>本文を引く。<paramref name="args"/> は『名前, 値, 名前, 値, …』の交互(例: <c>Get("scr.home.welcome", "school", "青嵐大学")</c>)。
        /// キーがなければ、開発ビルドは <c>⟦キー⟧</c>、製品は <c>？</c>(診断ログに 1 回だけ)を返す。</summary>
        string Get(string key, params string[] args);

        bool Has(string key);
    }
}
