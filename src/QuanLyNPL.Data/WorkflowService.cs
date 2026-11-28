using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

/// <summary>Một việc cần làm: nhóm, mức (bad/warn/ok/info), nội dung, và đích mở ra (đơn / nơi nhận / phiếu).</summary>
public sealed record TodoItem(string Group, string Severity, string Title, string Detail, string Action,
    long? OrderId = null, long? PlaceId = null, long? DocId = null, string? Target = null);

/// <summary>Bước thiết lập ban đầu.</summary>
public sealed record SetupStep(string Title, bool Done, string Screen, string Hint);

/// <summary>Một chặng trong vòng đời đơn hàng: trạng thái done / active / warn / bad / todo.</summary>
public sealed record OrderStage(string Name, string State, string Detail, string? Action = null);

/// <summary>
/// Dẫn đường vận hành: việc cần làm hằng ngày và vòng đời 7 chặng của đơn hàng
/// (Đơn hàng → Đáp ứng → Nhập kho → Cấp phát → Cân đối → Quyết toán → Đóng đơn).
/// </summary>
public sealed class WorkflowService(SqliteConnection c, OrderService orders, StockService stock, IssueService issue)
{
    int Count(string sql) => Convert.ToInt32(Db.Scalar(c, sql));

    public List<SetupStep> Setup() => new()
    {
        new("Khai báo nơi nhận (nhà cắt, tổ may, tổ hoàn thiện…) và kho", Count("SELECT COUNT(*) FROM place WHERE kind<>'XUAT'") > 0, "Nơi xuất / Nơi nhận", "Quản trị > Nơi xuất / Nơi nhận"),
        new("Khai báo khách hàng", Count("SELECT COUNT(*) FROM customer") > 0, "Khách hàng", "Khai báo > Khách hàng"),
        new("Khai báo nguyên phụ liệu của khách hàng", Count("SELECT COUNT(*) FROM material") > 0, "Nguyên phụ liệu", "Khai báo > Nguyên phụ liệu"),
        new("Khai báo lưới size", Count("SELECT COUNT(*) FROM size_grid") > 0, "Lưới size", "Khai báo > Lưới size"),
        new("Khai báo mã hàng", Count("SELECT COUNT(*) FROM product") > 0, "Mã hàng", "Khai báo > Mã hàng"),
        new("Khai báo chủng loại của mã hàng", Count("SELECT COUNT(*) FROM variant") > 0, "Chủng loại", "Khai báo > Chủng loại"),
        new("Khai báo BOM (Tổ cắt, Sản xuất, Đóng gói)", Count("SELECT COUNT(*) FROM bom_version") > 0, "BOM & định mức", "Khai báo > BOM & định mức"),
        new("Nhập kho NPL", Count("SELECT COUNT(*) FROM stock_doc WHERE type='IN' AND status='CONFIRMED'") > 0, "Phiếu nhập", "Nhập kho > Phiếu nhập"),
        new("Lập đơn hàng", Count("SELECT COUNT(*) FROM \"order\"") > 0, "Đơn hàng", "Khai báo > Đơn hàng"),
    };

    /// <summary>Việc cần làm: phiếu nháp chờ xác nhận, đơn thiếu dữ liệu / thiếu NPL, nơi nhận đang dư / thiếu, đơn sẵn sàng đóng.</summary>
    public List<TodoItem> Todos()
    {
        var items = new List<TodoItem>();
        var now = DateTime.Now;
        string Age(DateTime t) { var d = (int)(now - t).TotalDays; return d <= 0 ? "hôm nay" : $"{d} ngày trước"; }
        foreach (var d in new Queries(c).Docs(status: "DRAFT").Where(d => d.Kind != TransferService.Kind))
        {
            var kind = d.Type switch { "IN" => "Phiếu nhập", "RETURN" => "Phiếu trả", "OUT" => d.RefDocId is not null ? "Phiếu xuất bù" : d.Scope is not null ? "Phiếu cấp" : "Phiếu xuất", _ => "Phiếu" };
            var target = d.Type == "IN" ? "receipt" : d.Type == "RETURN" || d.RefDocId is not null ? "return" : "issue";
            items.Add(new("Phiếu nháp chờ xác nhận", (now - d.CreatedAt).TotalDays >= 2 ? "warn" : "info", $"{kind} {d.DocNo}",
                string.Join(" · ", new[] { d.OrderNo, d.IssuePlace, d.CustomerCode, "lập " + Age(d.CreatedAt) }.Where(x => x.Length > 0)) +
                (d.Scope is not null && d.RefDocId is null && d.Type == "OUT" ? " · đang giữ chỗ số lượng cấp" : ""),
                "Mở phiếu", d.OrderId, d.IssuePlaceId, d.Id, target));
        }
        foreach (var k in new Queries(c).Counts().Where(k => k.Status == "DRAFT"))
            items.Add(new("Phiếu nháp chờ xác nhận", "info", $"Phiếu kiểm kê {k.DocNo}", $"{k.CustomerCode} · lập {Age(k.CreatedAt)}", "Mở phiếu", Target: "count"));
        foreach (var t in new Queries(c).Transfers().Where(t => t.Status == "DRAFT"))
            items.Add(new("Phiếu nháp chờ xác nhận", "info", $"Phiếu chuyển {t.DocNo}", $"{t.FromCustomer} → {t.ToCustomer} · lập {Age(t.CreatedAt)}", "Mở phiếu", Target: "transfer"));

        foreach (var o in new Queries(c).Orders(status: "OPEN"))
        {
            var label = $"{o.No} – {o.ProductCode}";
            var demand = orders.CalculateDemand(o.Id);
            if (!demand.IsComplete)
            {
                items.Add(new("Đơn hàng cần xử lý", "bad", $"{label}: thiếu dữ liệu BOM",
                    string.Join("; ", demand.Issues.Select(i => i.Message).Distinct().Take(3)) + (demand.Issues.Count > 3 ? " …" : ""), "Mở đơn", o.Id, Target: "order"));
                continue;
            }
            var chk = stock.CheckOrder(o.Id, orders);
            var shortLines = chk.Lines.Where(l => l.Shortage > 0).ToList();
            if (shortLines.Count > 0)
                items.Add(new("Đơn hàng cần xử lý", "warn", $"{label}: thiếu NPL để cấp",
                    string.Join(", ", shortLines.Take(5).Select(l => $"{l.MaterialCode} thiếu {AuditLog.N(l.Shortage)}")) + (shortLines.Count > 5 ? " …" : ""), "Nhập phần thiếu", o.Id, Target: "receipt-short"));
            var settle = issue.Settlement(o.Id).Where(s => s.PlaceId is not null).ToList();
            foreach (var g in settle.Where(s => Math.Round(s.Balance, 2) < 0).GroupBy(s => (s.PlaceId, s.Place)))
                items.Add(new("Nơi nhận đang thiếu / dư", "bad", $"{g.Key.Place} đang THIẾU NPL – {o.No}",
                    string.Join(", ", g.Select(s => $"{s.Material} {AuditLog.N(-s.Balance)} {s.Unit}")), "Bù phần thiếu", o.Id, g.Key.PlaceId, Target: "supplement"));
            foreach (var g in settle.Where(s => Math.Round(s.Balance, 2) > 0).GroupBy(s => (s.PlaceId, s.Place)))
                items.Add(new("Nơi nhận đang thiếu / dư", "warn", $"{g.Key.Place} đang DƯ NPL – {o.No}",
                    string.Join(", ", g.Select(s => $"{s.Material} {AuditLog.N(s.Balance)} {s.Unit}")), "Trả phần dư", o.Id, g.Key.PlaceId, Target: "return"));
            var prog = issue.Progress(o.Id);
            if (prog.Count > 0 && prog.All(p => p.P.Ordered - p.P.Issued <= 0 && p.P.Draft == 0) && settle.All(s => Math.Round(s.Balance, 2) == 0))
                items.Add(new("Đơn sẵn sàng đóng", "ok", $"{label}: đã cấp đủ, cân đối xong", "Quyết toán rồi đóng đơn.", "Quyết toán", o.Id, Target: "settle"));
        }
        return items;
    }

    /// <summary>7 chặng của đơn hàng kèm trạng thái và việc tiếp theo.</summary>
    public List<OrderStage> Stages(long orderId)
    {
        var o = new Queries(c).Orders().First(x => x.Id == orderId);
        var closed = o.Status != "OPEN";
        var demand = orders.CalculateDemand(orderId);
        var list = new List<OrderStage>();
        list.Add(demand.IsComplete
            ? new("Đơn hàng", "done", $"{AuditLog.N(o.TotalSets)} bộ · BOM đủ")
            : new("Đơn hàng", "bad", "BOM chưa đủ: " + demand.Issues.Select(i => i.Message).Distinct().First(), "bom"));
        var chk = demand.IsComplete ? stock.CheckOrder(orderId, orders) : null;
        var shortLines = chk?.Lines.Where(l => l.Shortage > 0).ToList() ?? new();
        list.Add(chk is null ? new("Đáp ứng", "todo", "Chờ đủ dữ liệu BOM")
            : shortLines.Count == 0 ? new("Đáp ứng", "done", "Tồn khả dụng đủ cho phần chưa cấp")
            : new("Đáp ứng", "warn", $"Thiếu {shortLines.Count} mã NPL", "check"));
        list.Add(chk is null ? new("Nhập kho", "todo", "")
            : shortLines.Count == 0 ? new("Nhập kho", "done", "Không cần nhập thêm")
            : new("Nhập kho", "active", "Cần nhập: " + string.Join(", ", shortLines.Take(3).Select(l => $"{l.MaterialCode} {AuditLog.N(l.Shortage)}")) + (shortLines.Count > 3 ? " …" : ""), "receipt-short"));
        var prog = issue.Progress(orderId);
        var ordered = prog.Sum(p => p.P.Ordered); var issued = prog.Sum(p => Math.Min(p.P.Issued, p.P.Ordered)); var drafts = prog.Sum(p => p.P.Draft);
        var pct = ordered == 0 ? 0 : Math.Round(issued * 100m / ordered, 0);
        var draftDocs = Count($"SELECT COUNT(*) FROM stock_doc WHERE order_id={orderId} AND status='DRAFT'");
        list.Add(ordered == 0 ? new("Cấp phát", "todo", "")
            : pct >= 100 && draftDocs == 0 ? new("Cấp phát", "done", "Đã cấp 100% số lượng")
            : new("Cấp phát", issued + drafts == 0 ? "todo" : "active", $"Đã cấp {AuditLog.N(pct)}%" + (draftDocs > 0 ? $" · {draftDocs} phiếu nháp" : ""), "issue"));
        var settle = issue.Settlement(orderId);
        var dư = settle.Where(s => Math.Round(s.Balance, 2) > 0).ToList(); var thiếu = settle.Where(s => Math.Round(s.Balance, 2) < 0).ToList();
        list.Add(settle.Count == 0 ? new("Cân đối", "todo", "")
            : dư.Count + thiếu.Count == 0 ? new("Cân đối", "done", "Mọi nơi nhận cân đối")
            : new("Cân đối", thiếu.Count > 0 ? "bad" : "warn", $"{dư.Select(s => s.Place).Distinct().Count()} nơi dư · {thiếu.Select(s => s.Place).Distinct().Count()} nơi thiếu", "settle"));
        var ready = list.Take(5).All(s => s.State == "done");
        list.Add(closed ? new("Quyết toán", "done", "Đã quyết toán") : ready ? new("Quyết toán", "active", "Sẵn sàng quyết toán", "settle") : new("Quyết toán", "todo", "Chờ cấp đủ và cân đối"));
        list.Add(closed ? new("Đóng đơn", "done", "Đã đóng") : new("Đóng đơn", ready ? "active" : "todo", ready ? "Có thể đóng đơn" : "", ready ? "close" : null));
        return list;
    }
}
