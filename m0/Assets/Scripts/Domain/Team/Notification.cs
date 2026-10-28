namespace Ekiden.Domain.Team
{
    /// <summary>通知の種別(08a SC-38)。購入状態の不明(<see cref="Entitlement"/>)と自動セーブの失敗(<see cref="AutosaveFailed"/>)は App 層が足す。</summary>
    public enum NotifyKind { Injury, Warning, Event, Ticket, Schedule, Entitlement, AutosaveFailed }

    /// <summary>通知 1 件(08 §7.5)。文は <see cref="Code"/> から画面が文字列表で組む(<c>nt.*</c>)。</summary>
    public sealed class Notification
    {
        public NotifyKind Kind;
        public int AbsWeek;             // 起きた通算週
        /// <summary>Injury: <c>injury.minor|moderate|severe</c> / Warning: <c>fatigue</c> / Event: イベントの ID / Ticket: <c>earned</c> /
        /// Schedule: <c>entry_open.{fall|national|newyear}</c> <c>race_week.{...}</c> <c>qualifier_week</c> / Entitlement: <c>unknown</c> / AutosaveFailed: <c>failed</c> <c>disk_full</c>。</summary>
        public string Code = "";
        public int RunnerId;            // 関係する選手(なければ 0)
        public int N;                   // Warning: 人数 / Ticket: 枚数 / Schedule(entry_open): 大会まであと何週
        public bool Unread = true;
    }
}
