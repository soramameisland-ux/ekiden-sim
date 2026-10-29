namespace Ekiden.Domain.World
{
    /// <summary>
    /// 新規ゲームの初期の区間記録(02 §4.8『初期の区間記録』の表。L-68)。予熱の 15 年度は自校と無関係なので、年度 1 の最初の大会の前に、予熱の区間記録をこの値に置き換える。
    /// 値は実在の公表値の水準(2026 年 10 月時点。資料により数秒〜2 分の差がある)で、保持者の名前は持たない。保持者は『歴代の記録』(<see cref="LegacySchoolId"/>)。
    /// </summary>
    public static class RecordSeeds
    {
        /// <summary>保持者が『歴代の記録』のときの <see cref="CourseRecord.SchoolId"/>(学校でも走者でもない)。</summary>
        public const int LegacySchoolId = -1;

        /// <summary>初期の記録の <see cref="CourseRecord.Year"/>(予熱の年度 −14〜0 と区別する印)。</summary>
        public const int LegacyYear = -100;

        /// <summary>[大会][区間] のタイム(ds = 0.1 秒)。区間の数は <see cref="Ekiden.Domain.Race.RaceTables.Course"/> と同じ。</summary>
        public static readonly int[][] CourseDs =
        {
            new[] { 13500, 9270, 13760, 10300, 10550, 16970 },        // 第一戦(6 区間)
            new[] { 16180, 18610, 19660, 19830, 21010, 22070, 29710, 33320 },   // 第二戦(8 区間)
            new[] { 36280, 39090, 35650, 36000, 40360, 34070, 36430, 38250, 40350, 40700 },   // 第三戦(10 区間)
        };

        /// <summary>画面の名前のキー。『歴代の記録』は学校の名前でなく専用の語。</summary>
        public static string NameKey(int schoolId) { return schoolId < 0 ? "record.legacy_holder" : "school." + schoolId + ".name"; }
    }
}
