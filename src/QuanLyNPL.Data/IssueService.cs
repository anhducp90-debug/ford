using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

/// <summary>Quy trình cấp phát theo đơn: nháp theo nhu cầu còn lại → kho nhập thực cấp → xác nhận (StockService.Confirm) → bù / trả.</summary>
public sealed class IssueService(SqliteConnection c, OrderService orders, StockService stock)
{
    public const string AllScope = "ALL";

    /// <summary>
    /// Tạo phiếu xuất nháp. Đề xuất = max(0, nhu cầu kỳ − số dư đầu), với nhu cầu kỳ = nhu cầu hiện tại − nhu cầu đã tính vào sổ.
    /// Số dư dương (cấp thừa) giảm đề xuất, số dư âm (cấp thiếu) cộng vào.
    /// </summary>
    public long CreateIssueDraft(long orderId, string docNo, string scope = AllScope)
    {
        OrderService.RequireOpen(c, orderId);
        var demand = ScopeDemand(orderId, scope);
        if (demand.Count == 0) throw new BusinessException("Đơn chưa có nhu cầu NPL để cấp.");
        EnsureScopeConsistent(orderId, scope);
        var mats = demand.Keys.ToDictionary(k => k, k => new CatalogRepository(c).MaterialId(k));

        using var tx = c.BeginTransaction(deferred: false);
        var id = Db.Insert(c, "INSERT INTO stock_doc(type,doc_no,order_id,scope,created_at,customer_id) VALUES('OUT',$n,$o,$s,$d,(SELECT customer_id FROM \"order\" WHERE id=$o))", tx,
            ("$n", docNo), ("$o", orderId), ("$s", scope), ("$d", Db.Date(DateTime.Now)));
        foreach (var (code, total) in demand.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var mid = mats[code];
            var period = total - stock.CarryAccountedDemand(orderId, mid, scope, tx);
            var suggested = DemandCalculator.Round(Math.Max(0m, period - stock.CarryClosing(orderId, mid, scope, tx)));
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty,qty_suggested,demand,entered) VALUES($d,$m,'0',$s,$p,0)", tx,
                ("$d", id), ("$m", mid), ("$s", Db.D(suggested)), ("$p", Db.D(period)));
        }
        AuditLog.Write(c, "stock_doc", "Lập phiếu nháp", docNo, null, $"Cấp phát theo nhu cầu · phạm vi {scope} · {demand.Count} mã NPL", null, id, tx);
        tx.Commit();
        return id;
    }

    /// <summary>Kho nhập thực cấp. 0 được giữ nguyên (không tự thay bằng đề xuất). Trả về các cảnh báo (không chặn).</summary>
    public List<string> SetActual(long docId, string materialCode, decimal actual)
    {
        if (actual < 0) throw new BusinessException("Thực cấp không được âm.");
        var doc = Db.Query(c, "SELECT status,order_id FROM stock_doc WHERE id=$i", r => (st: r.GetString(0), o: r.IsDBNull(1) ? (long?)null : r.GetInt64(1)), null, ("$i", docId)).SingleOrDefault();
        if (doc == default || doc.st != "DRAFT") throw new BusinessException("Chỉ nhập thực cấp cho phiếu nháp.");
        var mid = new CatalogRepository(c).MaterialId(materialCode);
        var line = Db.Query(c, "SELECT id,qty_suggested FROM stock_doc_line WHERE doc_id=$d AND material_id=$m",
            r => (id: r.GetInt64(0), sug: r.IsDBNull(1) ? (decimal?)null : Db.D(r.GetString(1))), null, ("$d", docId), ("$m", mid)).SingleOrDefault();
        if (line == default) throw new BusinessException($"NPL {materialCode} không có trong phiếu.");
        Db.Exec(c, "UPDATE stock_doc_line SET qty=$q, entered=1 WHERE id=$i", null, ("$q", Db.D(actual)), ("$i", line.id));
        var warns = new List<string>();
        if (line.sug is decimal s && actual > s) warns.Add($"{materialCode}: thực cấp {actual} vượt đề xuất {s}.");
        var avail = stock.OnHand(materialCode) - stock.ReservedForOthers(materialCode, doc.o);
        if (actual > avail) warns.Add($"{materialCode}: thực cấp {actual} vượt tồn khả dụng {avail}.");
        return warns;
    }

    /// <summary>Xuất bù: phiếu bổ sung liên kết phiếu cấp gốc, đi qua cùng kiểm tra/transaction xác nhận.</summary>
    public long CreateSupplement(long refDocId, string docNo, IReadOnlyList<(string material, decimal qty)> lines)
    {
        var o = Db.Query(c, "SELECT order_id,scope,status,type FROM stock_doc WHERE id=$i",
            r => (order: r.IsDBNull(0) ? (long?)null : r.GetInt64(0), scope: r.IsDBNull(1) ? null : r.GetString(1), st: r.GetString(2), t: r.GetString(3)), null, ("$i", refDocId)).SingleOrDefault();
        if (o == default || o.order is null || o.scope is null || o.t != "OUT" || o.st != "CONFIRMED")
            throw new BusinessException("Xuất bù phải liên kết một phiếu cấp theo đơn đã xác nhận.");
        OrderService.RequireOpen(c, o.order.Value);
        var id = stock.CreateDraft("OUT", docNo, lines, o.order);
        Db.Exec(c, "UPDATE stock_doc SET scope=$s, ref_doc_id=$r WHERE id=$i", null, ("$s", o.scope), ("$r", refDocId), ("$i", id));
        return id;
    }

    /// <summary>
    /// Trả NPL vật lý theo đơn: MỘT nghiệp vụ, khi xác nhận vừa tăng tồn vừa giảm lượng đã cấp
    /// (trong cùng transaction, một dòng sổ) nên không bị tính hai lần.
    /// </summary>
    public long CreateReturn(long orderId, string docNo, IReadOnlyList<(string material, decimal qty)> lines, string scope = AllScope)
    {
        OrderService.RequireOpen(c, orderId);
        var id = stock.CreateDraft("RETURN", docNo, lines, orderId);
        Db.Exec(c, "UPDATE stock_doc SET scope=$s WHERE id=$i", null, ("$s", scope), ("$i", id));
        return id;
    }

    /// <summary>Nhu cầu theo phạm vi: ALL = cả đơn, hoặc mã chủng loại. Dữ liệu thiếu thì không cho cấp.</summary>
    public Dictionary<string, decimal> ScopeDemand(long orderId, string scope)
    {
        var d = orders.CalculateDemand(orderId);
        if (!d.IsComplete) throw new BusinessException("Dữ liệu chưa đủ để cấp phát:\n" + string.Join("\n", d.Issues.Select(i => i.Message)));
        if (scope == AllScope) return d.TotalByMaterial.ToDictionary(k => k.Key, k => k.Value);
        return d.Sources.Where(s => s.Variant == scope).GroupBy(s => s.MaterialCode)
                .ToDictionary(g => g.Key, g => DemandCalculator.Round(g.Sum(s => s.Qty)));
    }

    /// <summary>Không tạo phiếu nháp trùng phạm vi; không trộn phạm vi ALL và chủng loại trên cùng đơn (tránh cấp trùng).</summary>
    void EnsureScopeConsistent(long orderId, string scope)
    {
        var drafts = Db.Query(c, "SELECT scope FROM stock_doc WHERE order_id=$o AND type='OUT' AND status='DRAFT' AND scope IS NOT NULL AND ref_doc_id IS NULL",
            r => r.GetString(0), null, ("$o", orderId));
        if (drafts.Count > 0) throw new BusinessException("Đơn đã có phiếu xuất nháp chưa xác nhận.");
        var used = Db.Query(c, "SELECT DISTINCT scope FROM carry_ledger WHERE order_id=$o", r => r.GetString(0), null, ("$o", orderId));
        if (used.Any(u => u != scope && (u == AllScope || scope == AllScope)))
            throw new BusinessException("Đơn đã cấp theo phạm vi khác (cả đơn / chủng loại), không trộn phạm vi.");
    }
}
