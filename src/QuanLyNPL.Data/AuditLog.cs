using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

/// <summary>
/// Nhật ký thay đổi: mọi thao tác ghi dữ liệu (danh mục, BOM, đơn, phiếu, xác nhận, hủy nháp, kiểm kê, chuyển NPL,
/// nơi xuất/nhận, cài đặt, bảo mật, sao lưu/phục hồi) ghi một dòng: thời điểm, nhóm, thao tác, đối tượng, trước/sau, lý do.
/// </summary>
public static class AuditLog
{
    public static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>
    {
        ["customer"] = "Khách hàng", ["material"] = "Nguyên phụ liệu", ["size_grid"] = "Lưới size", ["product"] = "Mã hàng", ["variant"] = "Chủng loại",
        ["bom"] = "BOM", ["order"] = "Đơn hàng", ["stock_doc"] = "Phiếu kho", ["reservation"] = "Giữ kho", ["stock_count"] = "Kiểm kê",
        ["material_transfer"] = "Chuyển NPL", ["place"] = "Nơi xuất / nhận", ["setting"] = "Cài đặt", ["security"] = "Bảo mật", ["backup"] = "Sao lưu / phục hồi",
        // nhóm cũ (trước migration 8)
        ["stock_doc_draft"] = "Phiếu kho", ["stock_count_draft"] = "Kiểm kê", ["material_transfer_draft"] = "Chuyển NPL"
    };

    public static string Group(string entity) => Groups.TryGetValue(entity, out var g) ? g : entity;

    public static void Write(SqliteConnection c, string entity, string action, string? refText, string? before = null, string? after = null,
        string? reason = null, long? entityId = null, SqliteTransaction? tx = null) =>
        Db.Exec(c, "INSERT INTO audit_log(entity,entity_id,action,ref_text,before_json,after_json,reason,created_at) VALUES($e,$i,$a,$r,$b,$f,$why,$t)", tx,
            ("$e", entity), ("$i", entityId), ("$a", action), ("$r", refText), ("$b", before), ("$f", after), ("$why", reason), ("$t", Db.Date(DateTime.Now)));

    static readonly System.Globalization.NumberFormatInfo Vn = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
    public static string N(decimal d) => d.ToString("#,##0.####", Vn);
    public static string Signed(decimal d) => (d > 0 ? "+" : "") + N(d);
}
