namespace Ekiden.Domain.Race
{
    /// <summary>記録の更新 1 件(<see cref="Section"/> = −1 は大会記録)。</summary>
    public sealed class RecordUpdate
    {
        public RaceKind Kind;
        public int Section;
        public int SchoolId, RunnerId, TimeDs, Year;
        public int PrevSchoolId, PrevTimeDs, PrevYear;       // 更新される前の記録
    }
}
