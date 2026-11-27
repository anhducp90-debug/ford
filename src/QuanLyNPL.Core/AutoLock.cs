namespace QuanLyNPL.Core;

/// <summary>Tự khóa khi không thao tác. Thời gian được tiêm vào để test; UI gọi Touch() mỗi thao tác và Tick() theo timer.</summary>
public sealed class AutoLock(TimeSpan idle, Func<DateTime> now)
{
    DateTime _last = now();
    public bool IsLocked { get; private set; } = true;   // khởi động ở trạng thái khóa, chờ nhập PIN

    public void Unlock() { IsLocked = false; _last = now(); }
    public void Lock() => IsLocked = true;
    public void Touch() { if (!IsLocked) _last = now(); }
    public bool Tick() { if (!IsLocked && now() - _last >= idle) IsLocked = true; return IsLocked; }
}
