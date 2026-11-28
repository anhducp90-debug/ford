using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

/// <summary>Bảng báo cáo dùng chung cho giao diện, Excel và PDF.</summary>
public sealed record ReportTable(string Title, IReadOnlyList<string> Filters, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);

public sealed record ReportFilter(DateTime? From = null, DateTime? To = null, long? CustomerId = null, long? ProductId = null,
    long? OrderId = null, string? MaterialCode = null, bool OnlyShortage = false);

/// <summary>Kết quả tóm tắt số bộ tối đa theo cơ cấu size của đơn.</summary>
public sealed record MaxSetsSummary(bool Complete, decimal OrderedSets, decimal MaxSets, decimal Ratio, IReadOnlyList<string> Limiting);

public sealed class ReportService(SqliteConnection c, OrderService orders, StockService stock)
{
    static string FDate(DateTime d) => d.ToString("dd/MM/yyyy");

    List<string> Describe(ReportFilter f)
    {
        var l = new List<string>();
        if (f.From is not null || f.To is not null) l.Add($"Thời gian: {(f.From is null ? "…" : FDate(f.From.Value))} – {(f.To is null ? "…" : FDate(f.To.Value))}");
        if (f.CustomerId is long ci) l.Add("Khách hàng: " + Db.Scalar(c, "SELECT code FROM customer WHERE id=$i", null, ("$i", ci)));
        if (f.ProductId is long pi) l.Add("Mã hàng: " + Db.Scalar(c, "SELECT code FROM product WHERE id=$i", null, ("$i", pi)));
        if (f.OrderId is long oi) l.Add("Đơn hàng: " + Db.Scalar(c, "SELECT order_no FROM \"order\" WHERE id=$i", null, ("$i", oi)));
        if (f.MaterialCode is not null) l.Add("Mã NPL: " + f.MaterialCode);
        if (f.OnlyShortage) l.Add("Chỉ mã thiếu");
        return l;
    }

    // Ngày 'To' tính hết ngày đó.
    static DateTime? EndOfDay(DateTime? d) => d?.Date.AddDays(1);

    /// <summary>Tồn hiện tại theo mã NPL (tồn thực tế, đã giữ, khả dụng).</summary>
    public ReportTable CurrentStock(ReportFilter? f = null)
    {
        f ??= new();
        var rows = new List<object?[]>();
        foreach (var m in Materials(f.MaterialCode, f.CustomerId))
        {
            var onHand = stock.OnHand(m.code); var reserved = stock.ReservedForOthers(m.code, null);
            rows.Add(new object?[] { m.code, m.name, m.cust, m.unit, onHand, reserved, onHand - reserved });
        }
        return new("Tồn hiện tại", Describe(f), new[] { "Mã NPL", "Tên", "Khách hàng", "ĐVT", "Tồn thực tế", "Đã giữ", "Khả dụng" }, rows);
    }

    /// <summary>Nhập – xuất – tồn theo kỳ. Tồn cuối = đầu + nhập − xuất + trả + điều chỉnh.</summary>
    public ReportTable InOutStock(ReportFilter f)
    {
        var from = f.From ?? DateTime.MinValue; var toEx = EndOfDay(f.To) ?? DateTime.MaxValue;
        var txns = Db.Query(c, "SELECT m.code,d.type,t.qty_signed,t.created_at FROM stock_txn t JOIN material m ON m.id=t.material_id JOIN stock_doc d ON d.id=t.doc_id",
            r => (code: r.GetString(0), type: r.GetString(1), q: Db.D(r.GetString(2)), at: Db.Date(r.GetString(3))));
        var rows = new List<object?[]>();
        foreach (var m in Materials(f.MaterialCode, f.CustomerId))
        {
            var mine = txns.Where(t => t.code == m.code).ToList();
            var open = mine.Where(t => t.at < from).Sum(t => t.q);
            var inP = mine.Where(t => t.at >= from && t.at < toEx).ToList();
            var nhap = inP.Where(t => t.type == "IN").Sum(t => t.q);
            var xuat = -inP.Where(t => t.type == "OUT").Sum(t => t.q);
            var tra = inP.Where(t => t.type == "RETURN").Sum(t => t.q);
            var dc = inP.Where(t => t.type is "ADJUST_UP" or "ADJUST_DOWN").Sum(t => t.q);
            rows.Add(new object?[] { m.code, m.name, m.cust, m.unit, open, nhap, xuat, tra, dc, open + nhap - xuat + tra + dc });
        }
        return new("Nhập – xuất – tồn", Describe(f), new[] { "Mã NPL", "Tên", "Khách hàng", "ĐVT", "Tồn đầu kỳ", "Nhập", "Xuất", "Trả", "Điều chỉnh", "Tồn cuối kỳ" }, rows);
    }

    /// <summary>Nhu cầu theo đơn: tổng hợp theo mã NPL.</summary>
    public ReportTable OrderDemand(long orderId)
    {
        var d = orders.CalculateDemand(orderId);
        var rows = d.TotalByMaterial.OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => new object?[] { k.Key, MatName(k.Key), k.Value }).ToList();
        var filters = Describe(new ReportFilter(OrderId: orderId));
        if (!d.IsComplete) filters.Add("CHƯA ĐỦ DỮ LIỆU: " + string.Join("; ", d.Issues.Select(i => i.Message)));
        return new("Nhu cầu theo đơn", filters, new[] { "Mã NPL", "Tên", "Tổng nhu cầu" }, rows);
    }

    /// <summary>Drill-down: nhu cầu phát sinh từ BOM nào, chủng loại nào, size nào.</summary>
    public ReportTable OrderDemandDetail(long orderId)
    {
        var d = orders.CalculateDemand(orderId);
        var bomNames = orders.LoadSnapshot(orderId).Boms.ToDictionary(b => b.Id, b => b.Name);
        var rows = d.Sources.OrderBy(s => s.MaterialCode, StringComparer.Ordinal).ThenBy(s => s.BomId).ThenBy(s => s.Variant).ThenBy(s => s.VariantSize)
            .Select(s => new object?[] { s.MaterialCode, bomNames.GetValueOrDefault(s.BomId, s.BomId), s.Variant, s.VariantSize, s.Qty }).ToList();
        return new("Nhu cầu theo đơn – chi tiết nguồn", Describe(new ReportFilter(OrderId: orderId)), new[] { "Mã NPL", "BOM", "Chủng loại", "Size", "Nhu cầu (chưa làm tròn)" }, rows);
    }

    /// <summary>
    /// Định mức một bộ của mã hàng theo từng size: tổng (SL chủng loại/bộ × định mức × (1+hao hụt)) của mọi BOM theo chủng loại
    /// cộng định mức các BOM theo bộ; dùng phiên bản BOM hiệu lực tại <paramref name="asOf"/>. Giá trị làm tròn 4 số lẻ để xem.
    /// </summary>
    public (ReportTable Summary, ReportTable Detail, IReadOnlyList<string> Issues) PerSetNorm(long productId, DateTime? asOf = null)
    {
        var at = asOf ?? DateTime.Now;
        var snap = orders.BuildSnapshot(productId, at);
        var sizes = new Queries(c).ProductSizes(productId);
        var bomNames = snap.Boms.ToDictionary(b => b.Id, b => b.Name);
        var perSize = new Dictionary<string, Dictionary<string, decimal>>();
        var detail = new List<object?[]>();
        var issues = new List<string>();
        foreach (var s in sizes)
        {
            var r = OrderService.Calculate(snap, new[] { new OrderLine(s, 1m) });
            issues.AddRange(r.Issues.Select(i => i.Message));
            perSize[s] = r.Sources.GroupBy(x => x.MaterialCode).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            detail.AddRange(r.Sources.OrderBy(x => x.MaterialCode, StringComparer.Ordinal).ThenBy(x => x.Variant)
                .Select(x => new object?[] { s, x.MaterialCode, MatName(x.MaterialCode), bomNames.GetValueOrDefault(x.BomId, x.BomId),
                    x.Variant == DemandCalculator.SetScope ? "Cả bộ" : x.Variant, Math.Round(x.Qty, 4) }));
        }
        var mats = perSize.Values.SelectMany(d => d.Keys).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();
        var rows = mats.Select(m => new object?[] { m, MatName(m), MatUnit(m) }
            .Concat(sizes.Select(s => (object?)(perSize[s].TryGetValue(m, out var q) ? Math.Round(q, 4) : 0m))).ToArray()).ToList();
        var p = Db.Query(c, "SELECT p.code,k.code FROM product p JOIN customer k ON k.id=p.customer_id WHERE p.id=$p", r => (r.GetString(0), r.GetString(1)), null, ("$p", productId)).Single();
        var filters = new List<string> { $"Mã hàng: {p.Item1} · Khách hàng: {p.Item2}", $"Phiên bản BOM hiệu lực ngày {at:dd/MM/yyyy}", "Đã gồm hao hụt; làm tròn 4 số lẻ để xem" };
        var distinctIssues = issues.Distinct().ToList();
        if (distinctIssues.Count > 0) filters.Add("CHƯA ĐỦ DỮ LIỆU: " + string.Join("; ", distinctIssues));
        return (new ReportTable("Định mức một bộ", filters, new[] { "Mã NPL", "Tên", "ĐVT" }.Concat(sizes.Select(s => "Size " + s)).ToList(), rows),
                new ReportTable("Định mức một bộ – chi tiết nguồn", filters, new[] { "Size", "Mã NPL", "Tên", "BOM", "Chủng loại", "Lượng / bộ" }, detail),
                distinctIssues);
    }

    /// <summary>
    /// Số bộ tối đa sản xuất được với tồn khả dụng hiện tại (đã trừ lượng giữ cho đơn khác):
    /// (1) theo cơ cấu size của đơn: hệ số k = min(tồn khả dụng / nhu cầu) trên mọi mã NPL, số bộ = k × tổng số bộ (không vượt số bộ đặt);
    /// (2) riêng từng size: min(tồn khả dụng / định mức 1 bộ của size) – mỗi size tính độc lập, không cộng các size với nhau.
    /// </summary>
    public (ReportTable ByOrderMix, ReportTable BySize, MaxSetsSummary Summary) MaxSets(long orderId)
    {
        var snap = orders.LoadSnapshot(orderId);
        var lines = orders.LoadLines(orderId);
        var total = lines.Sum(l => l.Sets);
        var d = OrderService.Calculate(snap, lines);
        // Năng lực của mỗi mã = phần đã cấp cho đơn + tồn khả dụng hiện tại.
        decimal Avail(string m) => Math.Max(0m, stock.OnHand(m) - stock.ReservedForOthers(m, orderId)) + Math.Max(0m, stock.IssuedNet(orderId, m));
        var need = d.Sources.GroupBy(x => x.MaterialCode).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        var mixRows = need.Where(kv => kv.Value > 0)
            .Select(kv => (mat: kv.Key, need: kv.Value, avail: Avail(kv.Key), ratio: Avail(kv.Key) / kv.Value))
            .OrderBy(x => x.ratio).ThenBy(x => x.mat, StringComparer.Ordinal).ToList();
        var k = mixRows.Count == 0 ? 0m : Math.Min(1m, mixRows.Min(x => x.ratio));
        var maxMix = Math.Floor(k * total);
        var limiting = mixRows.Where(x => x.ratio == mixRows[0].ratio).Select(x => x.mat).ToList();

        var filters = Describe(new ReportFilter(OrderId: orderId));
        var incomplete = !d.IsComplete;
        if (incomplete) filters.Add("CHƯA ĐỦ DỮ LIỆU: " + string.Join("; ", d.Issues.Select(i => i.Message).Distinct()) + " – không kết luận số bộ tối đa.");
        else filters.Add($"Theo cơ cấu size của đơn: tối đa {maxMix:#,##0} / {total:#,##0} bộ ({k * 100:0.#}%), giới hạn bởi: {string.Join(", ", limiting)}");

        var byMix = new ReportTable("Số bộ tối đa – theo cơ cấu size của đơn", filters,
            new[] { "Mã NPL", "Tên", "Nhu cầu cả đơn", "Đã cấp + tồn khả dụng", "Đáp ứng %", "Số bộ tối đa theo mã" },
            mixRows.Select(x => new object?[] { x.mat, MatName(x.mat), Math.Round(x.need, 2), x.avail, Math.Round(Math.Min(x.ratio, 9.99m) * 100, 1), Math.Floor(Math.Min(x.ratio, 1m) * total) }).ToList());

        var sizeRows = new List<object?[]>();
        foreach (var l in lines)
        {
            var r = OrderService.Calculate(snap, new[] { new OrderLine(l.DefaultSize, 1m) });
            var norm = r.Sources.GroupBy(x => x.MaterialCode).Select(g => (mat: g.Key, q: g.Sum(x => x.Qty))).Where(x => x.q > 0).ToList();
            if (norm.Count == 0 || !r.IsComplete) { sizeRows.Add(new object?[] { l.DefaultSize, l.Sets, null, "CHƯA ĐỦ DỮ LIỆU" }); continue; }
            var per = norm.Select(x => (x.mat, max: Math.Floor(Avail(x.mat) / x.q))).OrderBy(x => x.max).ToList();
            sizeRows.Add(new object?[] { l.DefaultSize, l.Sets, per[0].max, string.Join(", ", per.Where(x => x.max == per[0].max).Select(x => x.mat)) });
        }
        var bySize = new ReportTable("Số bộ tối đa – riêng từng size", filters,
            new[] { "Size", "Số bộ đặt", "Tối đa nếu chỉ sản xuất size này", "NPL giới hạn" }, sizeRows);
        return (byMix, bySize, new MaxSetsSummary(!incomplete, total, incomplete ? 0 : maxMix, incomplete ? 0 : k, limiting));
    }

    string MatUnit(string code) => (string)Db.Scalar(c, "SELECT unit FROM material WHERE code=$c", null, ("$c", code))!;

    /// <summary>Khả năng đáp ứng một đơn: từng mã NPL kèm trạng thái.</summary>
    public ReportTable OrderAvailability(long orderId, bool onlyShortage = false)
    {
        var r = stock.CheckOrder(orderId, orders);
        var rows = r.Lines.Where(l => !onlyShortage || l.Shortage > 0)
            .Select(l => new object?[] { l.MaterialCode, MatName(l.MaterialCode), l.Demand, l.Issued, l.OnHand, l.Reserved, l.Available, l.Shortage, l.Shortage > 0 ? "THIẾU" : "ĐỦ" }).ToList();
        var filters = Describe(new ReportFilter(OrderId: orderId, OnlyShortage: onlyShortage));
        filters.Insert(0, "Trạng thái đơn: " + StatusText(r.Status));
        return new("Cảnh báo thiếu NPL", filters, new[] { "Mã NPL", "Tên", "Nhu cầu", "Đã cấp cho đơn", "Tồn thực tế", "Giữ cho đơn khác", "Tồn khả dụng", "Thiếu", "Trạng thái" }, rows);
    }

    /// <summary>Đáp ứng đơn hàng: mỗi đơn mở một dòng, kèm danh sách mã thiếu.</summary>
    public ReportTable Fulfillment(ReportFilter? f = null)
    {
        f ??= new();
        var rows = new List<object?[]>();
        foreach (var o in OpenOrders(f))
        {
            string status, missing = "";
            try
            {
                var r = stock.CheckOrder(o.id, orders);
                status = StatusText(r.Status);
                missing = string.Join(", ", r.Lines.Where(l => l.Shortage > 0).Select(l => l.MaterialCode));
            }
            catch (Exception) { status = StatusText(FulfillStatus.Incomplete); }
            if (f.OnlyShortage && status != "THIẾU NPL") continue;
            rows.Add(new object?[] { o.no, o.cust, o.prod, FDate(o.date), status, missing });
        }
        return new("Đáp ứng đơn hàng", Describe(f), new[] { "Số đơn", "Khách hàng", "Mã hàng", "Ngày đặt", "Trạng thái", "Mã NPL thiếu" }, rows);
    }

    /// <summary>Lịch sử giao dịch kho theo mã NPL / kỳ / đơn / khách hàng: mọi biến động tồn đã xác nhận, kèm nơi xuất, nơi nhận, phạm vi, ghi chú.</summary>
    public ReportTable TransactionHistory(ReportFilter f)
    {
        var from = f.From ?? DateTime.MinValue; var toEx = EndOfDay(f.To) ?? DateTime.MaxValue;
        var rows = Txns(f.MaterialCode, f.OrderId, f.CustomerId).Where(t => t.At >= from && t.At < toEx)
            .Select(t => new object?[] { t.At.ToString("dd/MM/yyyy HH:mm"), t.DocNo, t.TypeText, t.Customer, t.Material, t.Qty, t.Order, t.ScopeText, t.FromPlace, t.ToPlace, t.Note }).ToList();
        return new("Lịch sử giao dịch", Describe(f), new[] { "Thời gian", "Số phiếu", "Loại", "Khách hàng", "Mã NPL", "Số lượng (±)", "Đơn", "Phạm vi", "Nơi xuất", "Nơi nhận", "Ghi chú" }, rows);
    }

    /// <summary>
    /// Thẻ kho một mã NPL: tồn đầu kỳ, từng giao dịch đã xác nhận trong kỳ (nhập/xuất) và tồn sau mỗi giao dịch, tồn cuối kỳ.
    /// Tồn cuối kỳ = tồn đầu + tổng nhập − tổng xuất (khớp với Nhập – xuất – tồn).
    /// </summary>
    public ReportTable StockCard(string materialCode, DateTime? fromDate, DateTime? toDate)
    {
        var from = fromDate?.Date ?? DateTime.MinValue; var toEx = EndOfDay(toDate) ?? DateTime.MaxValue;
        var all = Txns(materialCode, null, null);
        var opening = all.Where(t => t.At < from).Sum(t => t.Qty);
        var balance = opening;
        var rows = new List<object?[]> { new object?[] { fromDate is null ? "" : Ui(from), "", "Tồn đầu kỳ", null, null, opening, "", "", "", "" } };
        decimal tin = 0, tout = 0;
        foreach (var t in all.Where(t => t.At >= from && t.At < toEx))
        {
            balance += t.Qty;
            if (t.Qty > 0) tin += t.Qty; else tout -= t.Qty;
            rows.Add(new object?[] { t.At.ToString("dd/MM/yyyy HH:mm"), t.DocNo, t.TypeText,
                t.Qty > 0 ? t.Qty : null, t.Qty < 0 ? -t.Qty : null, balance, t.Order, t.FromPlace, t.ToPlace, t.Note ?? "" });
        }
        rows.Add(new object?[] { toDate is null ? "" : Ui(toDate.Value), "", "Cộng phát sinh / tồn cuối kỳ", tin, tout, balance, "", "", "", "" });
        var m = Db.Query(c, "SELECT m.name,m.unit,COALESCE(k.code,'') FROM material m LEFT JOIN customer k ON k.id=m.customer_id WHERE m.code=$c",
            r => (r.GetString(0), r.GetString(1), r.GetString(2)), null, ("$c", materialCode)).SingleOrDefault();
        var filters = new List<string> { $"Mã NPL: {materialCode} – {m.Item1} · ĐVT: {m.Item2} · Khách hàng: {m.Item3}" };
        filters.AddRange(Describe(new ReportFilter(fromDate, toDate)));
        return new("Thẻ kho", filters, new[] { "Ngày", "Số phiếu", "Diễn giải", "Nhập", "Xuất", "Tồn", "Đơn", "Nơi xuất", "Nơi nhận", "Ghi chú" }, rows);
        static string Ui(DateTime d) => d.ToString("dd/MM/yyyy");
    }

    /// <summary>Quyết toán NPL của đơn theo nơi nhận / phạm vi / mã NPL (dư – thiếu – đủ).</summary>
    public ReportTable SettlementReport(long orderId)
    {
        var iss = new IssueService(c, orders, stock);
        var rows = iss.Settlement(orderId).Select(s => new object?[] { s.Place, IssueScopes.Label(s.Scope), s.Material, s.MaterialName, s.Unit,
            s.Demand, s.Issued, s.Loss, s.Returned, s.Balance, s.Conclusion }).ToList();
        var filters = Describe(new ReportFilter(OrderId: orderId));
        // Phần số bộ chưa giao việc cho nơi nhận nào
        var notAssigned = IssueScopes.Types.Select(t => (t, rem: iss.Remaining(orderId, t).Where(kv => kv.Value > 0).ToList()))
            .Where(x => x.rem.Count > 0).Select(x => $"{IssueScopes.Label(x.t)}: " + string.Join(", ", x.rem.Select(kv => $"{kv.Key} còn {AuditLog.N(kv.Value)} bộ"))).ToList();
        if (notAssigned.Count > 0) filters.Add("Chưa giao việc – " + string.Join(" · ", notAssigned));
        var dư = rows.Count(r => (string)r[10]! == "Dư"); var thiếu = rows.Count(r => (string)r[10]! == "Thiếu");
        filters.Add(dư == 0 && thiếu == 0 ? "Kết luận: đã cân đối đủ" : $"Kết luận: {dư} dòng dư (phải trả kho / điều chuyển trước khi đóng), {thiếu} dòng thiếu");
        return new("Quyết toán NPL theo đơn", filters,
            new[] { "Nơi nhận", "Phạm vi", "Mã NPL", "Tên", "ĐVT", "Nhu cầu", "Đã cấp", "Bù hao hụt", "Trả / chuyển đi", "Số dư", "Kết luận" }, rows);
    }

    /// <summary>Hao hụt vượt định mức (xuất bù hao hụt) theo đơn / nơi nhận / mã NPL, so với nhu cầu theo định mức.</summary>
    public ReportTable LossReport(ReportFilter f)
    {
        var from = f.From ?? DateTime.MinValue; var toEx = EndOfDay(f.To) ?? DateTime.MaxValue;
        var data = Db.Query(c, @"SELECT o.order_no,COALESCE(p.name,''),l.scope,m.code,m.name,m.unit,l.demand,l.loss,l.created_at
                                 FROM carry_ledger l JOIN ""order"" o ON o.id=l.order_id JOIN material m ON m.id=l.material_id LEFT JOIN place p ON p.id=l.place_id
                                 WHERE ($o IS NULL OR l.order_id=$o) AND ($k IS NULL OR o.customer_id=$k)",
            r => (order: r.GetString(0), place: r.GetString(1), scope: r.GetString(2), mat: r.GetString(3), name: r.GetString(4), unit: r.GetString(5),
                  demand: Db.D(r.GetString(6)), loss: Db.D(r.GetString(7)), at: Db.Date(r.GetString(8))), null, ("$o", f.OrderId), ("$k", f.CustomerId));
        var rows = data.GroupBy(x => (x.order, x.place, x.scope, x.mat, x.name, x.unit))
            .Select(g => (g.Key, demand: g.Sum(x => x.demand), loss: g.Where(x => x.at >= from && x.at < toEx).Sum(x => x.loss)))
            .Where(x => x.loss > 0).OrderByDescending(x => x.demand == 0 ? 0 : x.loss / x.demand)
            .Select(x => new object?[] { x.Key.order, x.Key.place, IssueScopes.Label(x.Key.scope), x.Key.mat, x.Key.name, x.Key.unit, x.demand, x.loss,
                x.demand == 0 ? null : Math.Round(x.loss / x.demand * 100, 2) }).ToList();
        return new("Hao hụt vượt định mức", Describe(f), new[] { "Đơn", "Nơi nhận", "Phạm vi", "Mã NPL", "Tên", "ĐVT", "Nhu cầu theo định mức", "Bù hao hụt", "Hao hụt vượt %" }, rows);
    }

    /// <summary>Nhật ký thay đổi dạng bảng báo cáo (lọc + xuất Excel/PDF).</summary>
    public ReportTable AuditReport(AuditFilter f)
    {
        var rows = new Queries(c).Audit(f).Select(a => new object?[] { a.At.ToString("dd/MM/yyyy HH:mm:ss"), a.Group, a.ActionText, a.RefText, a.Before ?? "", a.After ?? "", a.Reason ?? "" }).ToList();
        var filters = Describe(new ReportFilter(f.From, f.To));
        if (f.Entity is not null) filters.Add("Nhóm: " + AuditLog.Group(f.Entity));
        if (!string.IsNullOrWhiteSpace(f.Keyword)) filters.Add("Từ khóa: " + f.Keyword);
        return new("Nhật ký thay đổi", filters, new[] { "Thời gian", "Nhóm", "Thao tác", "Đối tượng", "Trước", "Sau", "Lý do / ghi chú" }, rows);
    }

    sealed record Txn(DateTime At, string DocNo, string TypeText, string Customer, string Material, decimal Qty, string Order, string ScopeText, string FromPlace, string ToPlace, string? Note);

    List<Txn> Txns(string? material, long? orderId, long? customerId) =>
        Db.Query(c, @"SELECT t.created_at,d.doc_no,d.type,COALESCE(d.kind,''),d.ref_doc_id,d.scope,COALESCE(k.code,''),m.code,t.qty_signed,COALESCE(o.order_no,''),
                             COALESCE(pf.name,''),COALESCE(pt.name,''),d.note,COALESCE(d.sub_kind,'')
                      FROM stock_txn t JOIN stock_doc d ON d.id=t.doc_id JOIN material m ON m.id=t.material_id
                      LEFT JOIN ""order"" o ON o.id=d.order_id LEFT JOIN customer k ON k.id=d.customer_id
                      LEFT JOIN place pf ON pf.id=d.from_place_id LEFT JOIN place pt ON pt.id=d.to_place_id
                      WHERE ($m IS NULL OR m.code=$m) AND ($o IS NULL OR d.order_id=$o) AND ($k IS NULL OR m.customer_id=$k) ORDER BY t.created_at, t.id",
            r => new Txn(Db.Date(r.GetString(0)), r.GetString(1), TypeText(r.GetString(2), !r.IsDBNull(4), r.GetString(3), r.IsDBNull(5) ? null : r.GetString(5), r.GetString(13)),
                r.GetString(6), r.GetString(7), Db.D(r.GetString(8)), r.GetString(9),
                IssueScopes.Label(r.IsDBNull(5) ? null : r.GetString(5)),
                r.GetString(10), r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12)),
            null, ("$m", material), ("$o", orderId), ("$k", customerId));


    /// <summary>Lịch sử cấu hình của đơn: phiên bản BOM, lưới size, quy đổi và lý do điều chỉnh.</summary>
    public ReportTable ConfigHistory(long orderId)
    {
        var adj = new OrderAdjustmentService(c, orders, stock);
        var rows = adj.History(orderId).Select(v => new object?[] { v.No, v.At.ToString("dd/MM/yyyy HH:mm"), v.Reason, v.BomSummary }).ToList();
        return new("Lịch sử cấu hình", Describe(new ReportFilter(OrderId: orderId)), new[] { "Phiên bản", "Thời điểm", "Lý do", "BOM áp dụng" }, rows);
    }

    // ---- helpers ----
    List<(string code, string name, string unit, string cust)> Materials(string? code, long? customerId = null) =>
        Db.Query(c, "SELECT m.code,m.name,m.unit,COALESCE(k.code,'(chưa gán)') FROM material m LEFT JOIN customer k ON k.id=m.customer_id WHERE ($c IS NULL OR m.code=$c) AND ($k IS NULL OR m.customer_id=$k) ORDER BY m.code",
            r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)), null, ("$c", code), ("$k", customerId));

    string MatName(string code) => (string)Db.Scalar(c, "SELECT name FROM material WHERE code=$c", null, ("$c", code))!;

    List<(long id, string no, string cust, string prod, DateTime date)> OpenOrders(ReportFilter f) =>
        Db.Query(c, @"SELECT o.id,o.order_no,cu.code,p.code,o.order_date FROM ""order"" o JOIN customer cu ON cu.id=o.customer_id JOIN product p ON p.id=o.product_id
                      WHERE o.status='OPEN' AND ($c IS NULL OR o.customer_id=$c) AND ($p IS NULL OR o.product_id=$p) AND ($o IS NULL OR o.id=$o) ORDER BY o.id",
            r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Db.Date(r.GetString(4))), null,
            ("$c", f.CustomerId), ("$p", f.ProductId), ("$o", f.OrderId))
          .Where(o => (f.From is null || o.Item5 >= f.From) && (f.To is null || o.Item5 < EndOfDay(f.To))).ToList();

    public static string StatusText(FulfillStatus s) => s switch { FulfillStatus.Enough => "ĐỦ NPL", FulfillStatus.Short => "THIẾU NPL", _ => "CHƯA ĐỦ DỮ LIỆU" };

    static string TypeText(string t, bool bu, string kind = "", string? scope = null, string sub = "") => kind == TransferService.Kind ? (t == "OUT" ? "Chuyển sang khách hàng khác" : "Nhận từ khách hàng khác") : t switch
    {
        "IN" => "Nhập", "OUT" => bu ? (sub == "BU_HAO_HUT" ? "Xuất bù hao hụt" : "Xuất bù thiếu") : scope is not null ? "Xuất cấp phát" : "Xuất", "RETURN" => "Trả", "ADJUST_UP" => "Điều chỉnh tăng", "ADJUST_DOWN" => "Điều chỉnh giảm", _ => t
    };
}
