using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

public sealed record MaterialDelta(string Material, decimal Before, decimal After, decimal Issued)
{
    public decimal Diff => After - Before;
}

public sealed record AdjustmentPreview(IReadOnlyList<MaterialDelta> Deltas, IReadOnlyList<string> Warnings,
    IReadOnlyList<Issue> Issues, bool CanApply);

public sealed record OrderVersionInfo(int No, DateTime At, string? Reason, string BomSummary);

/// <summary>
/// Điều chỉnh đơn: xem chênh lệch nhu cầu trước khi lưu, bắt buộc có lý do, lưu nhật ký trước/sau và snapshot mới.
/// Phiếu đã xác nhận không bị hồi tố: chỉ nhu cầu CÒN LẠI thay đổi (kỳ sau tự bù qua sổ carry).
/// </summary>
public sealed class OrderAdjustmentService(SqliteConnection c, OrderService orders, StockService stock)
{
    static readonly JsonSerializerOptions Json = new();

    /// <param name="rebindBom">true: áp dụng phiên bản BOM/cấu hình hiệu lực tại <paramref name="asOf"/> cho phần chưa cấp; false: giữ snapshot cũ.</param>
    public AdjustmentPreview Preview(long orderId, IReadOnlyList<OrderLine> newLines, bool rebindBom = false, DateTime? asOf = null)
    {
        var (_, _, _, snapNew) = Prepare(orderId, newLines, rebindBom, asOf);
        var before = orders.CalculateDemand(orderId);
        var after = OrderService.Calculate(snapNew, newLines);

        var warnings = new List<string>();
        var issuedByMat = IssuedByMaterial(orderId);
        var mats = before.TotalByMaterial.Keys.Union(after.TotalByMaterial.Keys).OrderBy(x => x, StringComparer.Ordinal);
        var deltas = mats.Select(m => new MaterialDelta(m, before.TotalByMaterial.GetValueOrDefault(m), after.TotalByMaterial.GetValueOrDefault(m), issuedByMat.GetValueOrDefault(m))).ToList();
        foreach (var d in deltas.Where(d => d.Issued > d.After))
            warnings.Add($"{d.Material}: đã cấp {d.Issued} vượt nhu cầu mới {d.After}. Cần xử lý phần dư bằng trả kho, giữ lại hoặc chuyển đơn.");
        var drafts = DraftIssueCount(orderId);
        var issues = after.Issues.ToList();
        if (drafts > 0) issues.Add(new(IssueKind.InvalidData, "Đơn có phiếu xuất nháp chưa xác nhận: xác nhận hoặc hủy phiếu nháp trước khi điều chỉnh."));
        return new(deltas, warnings, issues, issues.Count == 0);
    }

    public void Apply(long orderId, IReadOnlyList<OrderLine> newLines, string reason, bool rebindBom = false, DateTime? asOf = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Phải nhập lý do điều chỉnh.");
        var preview = Preview(orderId, newLines, rebindBom, asOf);
        if (!preview.CanApply) throw new BusinessException("Không thể điều chỉnh:\n" + string.Join("\n", preview.Issues.Select(i => i.Message)));
        var (_, _, _, snapNew) = Prepare(orderId, newLines, rebindBom, asOf);
        var oldLines = orders.LoadLines(orderId);

        using var tx = c.BeginTransaction(deferred: false);
        Db.Exec(c, "DELETE FROM order_line WHERE order_id=$o", tx, ("$o", orderId));
        foreach (var l in newLines)
            Db.Exec(c, "INSERT INTO order_line(order_id,default_size_code,sets_qty) VALUES($o,$s,$q)", tx, ("$o", orderId), ("$s", l.DefaultSize), ("$q", Db.D(l.Sets)));
        Db.Exec(c, "INSERT INTO order_snapshot(order_id,created_at,config_json) VALUES($o,$t,$j)", tx,
            ("$o", orderId), ("$t", Db.Date(DateTime.Now)), ("$j", JsonSerializer.Serialize(snapNew, Json)));
        static string Sets(IEnumerable<OrderLine> ls) => string.Join("; ", ls.Select(l => $"{l.DefaultSize}: {AuditLog.N(l.Sets)}"));
        var no = (string)Db.Scalar(c, "SELECT order_no FROM \"order\" WHERE id=$o", tx, ("$o", orderId))!;
        AuditLog.Write(c, "order", "Điều chỉnh", no, Sets(oldLines),
            Sets(newLines) + (rebindBom ? " · áp dụng BOM mới" : "") + " · chênh lệch: " + string.Join("; ", preview.Deltas.Where(d => d.Diff != 0).Select(d => $"{d.Material} {AuditLog.Signed(d.Diff)}")),
            reason, orderId, tx);
        tx.Commit();
    }

    /// <summary>Lịch sử cấu hình của đơn: mỗi lần lập/điều chỉnh là một phiên bản với tóm tắt BOM đã dùng.</summary>
    public List<OrderVersionInfo> History(long orderId)
    {
        var snaps = Db.Query(c, "SELECT created_at,config_json FROM order_snapshot WHERE order_id=$o ORDER BY id",
            r => (at: Db.Date(r.GetString(0)), json: r.GetString(1)), null, ("$o", orderId));
        // Chỉ các lần điều chỉnh (không lẫn đóng/mở đơn hay lập đơn).
        var reasons = Db.Query(c, "SELECT reason FROM audit_log WHERE entity='order' AND entity_id=$o AND (action='Điều chỉnh' OR (action IS NULL AND before_json IS NOT NULL)) ORDER BY id",
            r => r.IsDBNull(0) ? null : r.GetString(0), null, ("$o", orderId));
        var list = new List<OrderVersionInfo>();
        for (var i = 0; i < snaps.Count; i++)
        {
            var s = JsonSerializer.Deserialize<OrderSnapshot>(snaps[i].json, Json)!;
            var summary = string.Join("; ", s.Boms.Select(b => $"{b.Name} v{(b.VersionNo?.ToString() ?? "–")}"));
            list.Add(new(i + 1, snaps[i].at, i == 0 ? "Lập đơn" : (i - 1 < reasons.Count ? reasons[i - 1] : null), summary));
        }
        return list;
    }

    (long product, long customer, string status, OrderSnapshot snap) Prepare(long orderId, IReadOnlyList<OrderLine> newLines, bool rebind, DateTime? asOf)
    {
        var o = Db.Query(c, "SELECT o.product_id,o.customer_id,o.status,p.default_grid_id FROM \"order\" o JOIN product p ON p.id=o.product_id WHERE o.id=$o",
            r => (prod: r.GetInt64(0), cust: r.GetInt64(1), st: r.GetString(2), grid: r.GetInt64(3)), null, ("$o", orderId)).SingleOrDefault();
        if (o == default) throw new BusinessException("Đơn hàng không tồn tại.");
        if (o.st != "OPEN") throw new BusinessException("Đơn đã đóng, không điều chỉnh.");
        orders.ValidateLines(o.grid, newLines);
        var snap = rebind ? orders.BuildSnapshot(o.prod, asOf ?? DateTime.Now) : orders.LoadSnapshot(orderId);
        return (o.prod, o.cust, o.st, snap);
    }

    Dictionary<string, decimal> IssuedByMaterial(long orderId) =>
        Db.Query(c, "SELECT m.code,l.issued FROM carry_ledger l JOIN material m ON m.id=l.material_id WHERE l.order_id=$o",
            r => (code: r.GetString(0), q: Db.D(r.GetString(1))), null, ("$o", orderId))
          .GroupBy(x => x.code).ToDictionary(g => g.Key, g => g.Sum(x => x.q));

    long DraftIssueCount(long orderId) =>
        Convert.ToInt64(Db.Scalar(c, "SELECT COUNT(*) FROM stock_doc WHERE order_id=$o AND status='DRAFT' AND type IN ('OUT','RETURN') AND scope IS NOT NULL", null, ("$o", orderId)));
}
