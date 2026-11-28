namespace QuanLyNPL.Data;

/// <summary>
/// Phạm vi cấp phát = loại BOM giao cho nơi nhận: cả bộ NPL (mọi loại BOM), hoặc riêng Tổ cắt / Sản xuất / Đóng gói.
/// Sổ cân đối tách theo (đơn, nơi nhận, phạm vi, mã NPL).
/// </summary>
public static class IssueScopes
{
    public const string All = "ALL", ToCat = "TO_CAT", SanXuat = "SAN_XUAT", DongGoi = "DONG_GOI";
    public static readonly string[] Types = { ToCat, SanXuat, DongGoi };
    public static readonly string[] Values = { All, ToCat, SanXuat, DongGoi };

    public static string Label(string? s) => s switch
    {
        null => "", All => "Cả bộ NPL", ToCat => "Tổ cắt", SanXuat => "Sản xuất", DongGoi => "Đóng gói",
        Core.DemandCalculator.SetScope => "Theo bộ (cũ)", _ => "Chủng loại " + s + " (cũ)"
    };

    /// <summary>Loại BOM thuộc phạm vi (ALL gồm mọi loại).</summary>
    public static bool Covers(string scope, string? bomType) => scope == All || scope == bomType;

    public static void Require(string scope)
    {
        if (!Values.Contains(scope)) throw new BusinessException("Phạm vi cấp không hợp lệ: " + scope);
    }
}

/// <summary>Loại xuất bù: bù thiếu (tính vào cân đối) hoặc bù hao hụt vượt định mức (ghi là tiêu hao, không làm số dư dương).</summary>
public enum SupplementKind { BuThieu, BuHaoHut }
