namespace QuanLyNPL.Data;

/// <summary>
/// Loại BOM cấp phát: Tổ cắt / Sản xuất / Đóng gói. Một phiếu cấp có thể gộp nhiều loại (ví dụ trọn gói A–Z cho một đơn vị).
/// Sổ cân đối tách theo (đơn, nơi nhận, mã NPL) – phạm vi sổ "NOI_NHAN"; số dư của nơi nhận này không dùng cho nơi nhận khác.
/// </summary>
public static class IssueScopes
{
    public const string All = "ALL", ToCat = "TO_CAT", SanXuat = "SAN_XUAT", DongGoi = "DONG_GOI";
    /// <summary>Phạm vi sổ cân đối của mọi phiếu cấp/trả/bù/điều chuyển theo nơi nhận.</summary>
    public const string Place = "NOI_NHAN";
    public static readonly string[] Types = { ToCat, SanXuat, DongGoi };
    /// <summary>Đối tượng sản xuất "cả bộ" (BOM theo bộ).</summary>
    public const string SetObject = Core.DemandCalculator.SetScope;

    /// <summary>Ghép danh sách loại BOM thành giá trị lưu trên phiếu (theo thứ tự Tổ cắt, Sản xuất, Đóng gói).</summary>
    public static string Join(IEnumerable<string> types) { var t = types.ToHashSet(); return string.Join(",", Types.Where(t.Contains)); }
    public static string[] Split(string? scope) => scope is null ? Array.Empty<string>() : scope.Split(',', StringSplitOptions.RemoveEmptyEntries);
    static bool IsTypeList(string s) { var p = Split(s); return p.Length > 0 && p.All(Types.Contains); }

    /// <summary>Phạm vi sổ cân đối ứng với phạm vi ghi trên phiếu: phiếu theo loại BOM (mới hoặc 1.3/1.4) → theo nơi nhận; sổ cũ trước bản 1.3 giữ nguyên.</summary>
    public static string Ledger(string scope) => scope == All || scope == Place || IsTypeList(scope) ? Place : scope;

    public static string Label(string? s) => s switch
    {
        null => "", All => "Cả bộ NPL (cũ)", Place => "Theo nơi nhận", ToCat => "Tổ cắt", SanXuat => "Sản xuất", DongGoi => "Đóng gói",
        Core.DemandCalculator.SetScope => "Theo bộ (cũ)",
        _ when IsTypeList(s) => Split(s).Length == Types.Length ? "Trọn gói (Cắt + SX + Đóng gói)" : string.Join(" + ", Split(s).Select(Label)),
        _ => "Chủng loại " + s + " (cũ)"
    };

    public static void Require(string scope)
    {
        if (!Types.Contains(scope)) throw new BusinessException("Loại BOM cấp phát không hợp lệ: " + scope);
    }
}

/// <summary>Loại xuất bù: bù thiếu (tính vào cân đối) hoặc bù hao hụt vượt định mức (ghi là tiêu hao, không làm số dư dương).</summary>
public enum SupplementKind { BuThieu, BuHaoHut }
