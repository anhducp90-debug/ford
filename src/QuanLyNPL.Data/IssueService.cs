using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

public sealed record AssignmentRow(long Id, long OrderId, long PlaceId, string PlaceCode, string PlaceName, string Scope, IReadOnlyDictionary<string, decimal> Sets)
{
    public decimal Total => Sets.Values.Sum();
}

public sealed record SettlementLine(string Place, long? PlaceId, string Scope, string Material, string MaterialName, string Unit,
    decimal Demand, decimal Issued, decimal Loss, decimal Returned, decimal Balance)
{
    public string Conclusion => Balance > 0 ? "Dư" : Balance < 0 ? "Thiếu" : "Đủ";
}

public sealed record SurplusLine(string Material, decimal Qty);

/// <summary>Đóng đơn khi còn thiếu cần người dùng xác nhận rõ.</summary>
public sealed class ShortageOnCloseException(string message) : BusinessException(message);

/// <summary>
/// Cấp phát theo NƠI NHẬN: giao việc (nơi nhận + phạm vi + số bộ theo size) → phiếu nháp theo nhu cầu còn lại của đúng nơi nhận/phạm vi →
/// kho nhập thực cấp → xác nhận (StockService.Confirm) → bù thiếu / bù hao hụt / trả / điều chuyển dư → quyết toán khi đóng đơn.
/// Số dư của nơi nhận này không bao giờ tự bù cho nơi nhận khác, kể cả cùng mã NPL.
/// </summary>
public sealed class IssueService(SqliteConnection c, OrderService orders, StockService stock)
{
    public const string AllScope = IssueScopes.All;

    // ---------------- Giao việc ----------------

    public List<AssignmentRow> Assignments(long orderId) =>
        Db.Query(c, @"SELECT a.id,a.order_id,a.place_id,p.code,p.name,a.scope FROM assignment a JOIN place p ON p.id=a.place_id WHERE a.order_id=$o ORDER BY p.code,a.scope",
                r => (id: r.GetInt64(0), o: r.GetInt64(1), p: r.GetInt64(2), pc: r.GetString(3), pn: r.GetString(4), s: r.GetString(5)), null, ("$o", orderId))
            .Select(a => new AssignmentRow(a.id, a.o, a.p, a.pc, a.pn, a.s,
                Db.Query(c, "SELECT size_code,sets FROM assignment_line WHERE assignment_id=$a ORDER BY id", r => (r.GetString(0), Db.D(r.GetString(1))), null, ("$a", a.id))
                  .ToDictionary(x => x.Item1, x => x.Item2)))
            .ToList();

    /// <summary>Số bộ còn có thể giao cho một phạm vi tại từng size (đã trừ phần giao cho nơi khác; ALL chiếm chỗ của mọi loại).</summary>
    public Dictionary<string, decimal> Remaining(long orderId, string scope, long? exceptPlaceId = null)
    {
        IssueScopes.Require(scope);
        var ordered = orders.LoadLines(orderId).ToDictionary(l => l.DefaultSize, l => l.Sets);
        var others = Assignments(orderId).Where(a => !(a.PlaceId == exceptPlaceId && a.Scope == scope)).ToList();
        var types = scope == IssueScopes.All ? IssueScopes.Types : new[] { scope };
        return ordered.ToDictionary(kv => kv.Key, kv => types.Min(t =>
            kv.Value - others.Where(a => a.Scope == IssueScopes.All || a.Scope == t).Sum(a => a.Sets.GetValueOrDefault(kv.Key))));
    }

    /// <summary>
    /// Giao việc (hoặc thêm đợt): đặt số bộ theo size giao cho nơi nhận với phạm vi. Tổng giao cho mọi nơi nhận không vượt số bộ của đơn
    /// ở từng size và từng loại BOM. Giảm dưới phần đã cấp thì phần đã cấp thành số dư (cần trả / điều chuyển).
    /// </summary>
    public void SetAssignment(long orderId, long placeId, string scope, IReadOnlyList<OrderLine> sets, string? reason = null)
    {
        OrderService.RequireOpen(c, orderId);
        IssueScopes.Require(scope);
        var place = Db.Query(c, "SELECT kind,active,name,code FROM place WHERE id=$i", r => (kind: r.GetString(0), active: r.GetInt32(1) == 1, name: r.GetString(2), code: r.GetString(3)), null, ("$i", placeId)).SingleOrDefault();
        if (place == default) throw new BusinessException("Nơi nhận không tồn tại.");
        if (!place.active) throw new BusinessException($"{place.name} đã ngừng sử dụng.");
        if (place.kind == "XUAT") throw new BusinessException($"{place.name} chỉ là nơi xuất, không nhận cấp phát.");
        var ordered = orders.LoadLines(orderId).ToDictionary(l => l.DefaultSize, l => l.Sets);
        var errs = new List<string>();
        if (sets.GroupBy(s => s.DefaultSize).Any(g => g.Count() > 1)) errs.Add("Trùng size.");
        var remaining = Remaining(orderId, scope, placeId);
        foreach (var s in sets)
        {
            if (s.Sets < 0) errs.Add($"Size {s.DefaultSize}: số bộ âm.");
            else if (!ordered.ContainsKey(s.DefaultSize)) { if (s.Sets > 0) errs.Add($"Size {s.DefaultSize} không có trong đơn."); }
            else if (s.Sets > remaining[s.DefaultSize]) errs.Add($"Size {s.DefaultSize}: giao {AuditLog.N(s.Sets)} bộ vượt phần còn giao được {AuditLog.N(remaining[s.DefaultSize])} bộ ({IssueScopes.Label(scope)}).");
        }
        if (errs.Count > 0) throw new BusinessException(string.Join("\n", errs));
        var old = Assignments(orderId).FirstOrDefault(a => a.PlaceId == placeId && a.Scope == scope);
        using var tx = c.BeginTransaction(deferred: false);
        var aid = old?.Id ?? Db.Insert(c, "INSERT INTO assignment(order_id,place_id,scope,created_at) VALUES($o,$p,$s,$t)", tx, ("$o", orderId), ("$p", placeId), ("$s", scope), ("$t", Db.Date(DateTime.Now)));
        Db.Exec(c, "DELETE FROM assignment_line WHERE assignment_id=$a", tx, ("$a", aid));
        foreach (var s in sets.Where(s => ordered.ContainsKey(s.DefaultSize)))
            Db.Exec(c, "INSERT INTO assignment_line(assignment_id,size_code,sets) VALUES($a,$s,$q)", tx, ("$a", aid), ("$s", s.DefaultSize), ("$q", Db.D(s.Sets)));
        string Fmt(IEnumerable<KeyValuePair<string, decimal>> d) => string.Join("; ", d.Where(x => x.Value != 0).Select(x => $"{x.Key}: {AuditLog.N(x.Value)}"));
        AuditLog.Write(c, "order", old is null ? "Giao việc" : "Sửa giao việc",
            $"{Db.Scalar(c, "SELECT order_no FROM \"order\" WHERE id=$o", tx, ("$o", orderId))} / {place.code} / {IssueScopes.Label(scope)}",
            old is null ? null : Fmt(old.Sets), Fmt(sets.Select(s => new KeyValuePair<string, decimal>(s.DefaultSize, s.Sets))), reason, orderId, tx);
        tx.Commit();
    }

    // ---------------- Nhu cầu theo nơi nhận ----------------

    /// <summary>Nhu cầu của đúng nơi nhận + phạm vi: số bộ đã giao × định mức các BOM thuộc phạm vi (làm tròn 2 số lẻ mỗi mã).</summary>
    public Dictionary<string, decimal> KeyDemand(long orderId, long placeId, string scope)
    {
        var a = Assignments(orderId).FirstOrDefault(x => x.PlaceId == placeId && x.Scope == scope)
            ?? throw new BusinessException("Chưa giao việc cho nơi nhận này với phạm vi " + IssueScopes.Label(scope) + ".");
        var lines = a.Sets.Where(kv => kv.Value > 0).Select(kv => new OrderLine(kv.Key, kv.Value)).ToList();
        if (lines.Count == 0) return new();
        var snap = orders.LoadSnapshot(orderId);
        var d = OrderService.Calculate(snap, lines);
        if (!d.IsComplete) throw new BusinessException("Dữ liệu chưa đủ để cấp phát:\n" + string.Join("\n", d.Issues.Select(i => i.Message).Distinct()));
        var types = BomTypes(snap);
        return d.Sources.Where(s => IssueScopes.Covers(scope, types.GetValueOrDefault(s.BomId))).GroupBy(s => s.MaterialCode)
            .ToDictionary(g => g.Key, g => DemandCalculator.Round(g.Sum(s => s.Qty)));
    }

    Dictionary<string, string?> BomTypes(OrderSnapshot snap) => snap.Boms.ToDictionary(b => b.Id, b => b.Type ??
        Db.Scalar(c, "SELECT type FROM bom WHERE id=$i", null, ("$i", long.TryParse(b.Id, out var id) ? id : 0)) as string);

    // ---------------- Phiếu cấp ----------------

    /// <summary>
    /// Phiếu xuất nháp cho một nơi nhận + phạm vi: đề xuất = max(0, nhu cầu kỳ − số dư của ĐÚNG nơi nhận/phạm vi).
    /// Mỗi nơi nhận + phạm vi tối đa một phiếu nháp; các nơi nhận khác lập song song được.
    /// </summary>
    public long CreateIssueDraft(long orderId, long placeId, string scope, string docNo)
    {
        OrderService.RequireOpen(c, orderId);
        var demand = KeyDemand(orderId, placeId, scope);
        var drafts = Convert.ToInt32(Db.Scalar(c, "SELECT COUNT(*) FROM stock_doc WHERE order_id=$o AND issue_place_id=$p AND scope=$s AND type='OUT' AND status='DRAFT' AND ref_doc_id IS NULL",
            null, ("$o", orderId), ("$p", placeId), ("$s", scope)));
        if (drafts > 0) throw new BusinessException("Nơi nhận này đã có phiếu xuất nháp cùng phạm vi chưa xác nhận.");
        var cat = new CatalogRepository(c);
        // Mã đã có trong sổ nhưng nay không còn nhu cầu (giảm giao việc) vẫn đưa vào để sổ ghi nhận nhu cầu kỳ âm.
        var inLedger = Db.Query(c, "SELECT DISTINCT m.code FROM carry_ledger l JOIN material m ON m.id=l.material_id WHERE l.order_id=$o AND l.place_id=$p AND l.scope=$s",
            r => r.GetString(0), null, ("$o", orderId), ("$p", placeId), ("$s", scope));
        var codes = demand.Keys.Union(inLedger).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (codes.Count == 0) throw new BusinessException("Nơi nhận chưa có nhu cầu NPL để cấp (kiểm tra số bộ giao và BOM của phạm vi).");
        var mats = codes.ToDictionary(k => k, cat.MaterialId);

        using var tx = c.BeginTransaction(deferred: false);
        var id = Db.Insert(c, @"INSERT INTO stock_doc(type,doc_no,order_id,scope,issue_place_id,to_place_id,created_at,customer_id)
                                VALUES('OUT',$n,$o,$s,$p,$p,$d,(SELECT customer_id FROM ""order"" WHERE id=$o))", tx,
            ("$n", docNo), ("$o", orderId), ("$s", scope), ("$p", placeId), ("$d", Db.Date(DateTime.Now)));
        var lines = 0;
        foreach (var code in codes)
        {
            var mid = mats[code];
            var period = demand.GetValueOrDefault(code) - stock.CarryAccountedDemand(orderId, mid, scope, placeId, tx);
            var suggested = DemandCalculator.Round(Math.Max(0m, period - stock.CarryClosing(orderId, mid, scope, placeId, tx)));
            if (period == 0 && suggested == 0 && !demand.ContainsKey(code)) continue;
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty,qty_suggested,demand,entered) VALUES($d,$m,'0',$s,$p,0)", tx,
                ("$d", id), ("$m", mid), ("$s", Db.D(suggested)), ("$p", Db.D(period)));
            lines++;
        }
        AuditLog.Write(c, "stock_doc", "Lập phiếu nháp", docNo, null,
            $"Cấp phát · nơi nhận {Db.Scalar(c, "SELECT name FROM place WHERE id=$p", tx, ("$p", placeId))} · {IssueScopes.Label(scope)} · {lines} mã NPL", null, id, tx);
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
        if (line.sug is decimal s && actual > s) warns.Add($"{materialCode}: thực cấp {AuditLog.N(actual)} vượt đề xuất {AuditLog.N(s)}.");
        var avail = stock.OnHand(materialCode) - stock.ReservedForOthers(materialCode, doc.o);
        if (actual > avail) warns.Add($"{materialCode}: thực cấp {AuditLog.N(actual)} vượt tồn khả dụng {AuditLog.N(avail)}.");
        return warns;
    }

    // ---------------- Xuất bù ----------------

    (long order, string scope, long? place, long? toPlace) SourceIssue(long refDocId)
    {
        var o = Db.Query(c, "SELECT order_id,scope,status,type,issue_place_id,ref_doc_id,to_place_id FROM stock_doc WHERE id=$i",
            r => (order: r.IsDBNull(0) ? (long?)null : r.GetInt64(0), scope: r.IsDBNull(1) ? null : r.GetString(1), st: r.GetString(2), t: r.GetString(3),
                  place: r.IsDBNull(4) ? (long?)null : r.GetInt64(4), refd: !r.IsDBNull(5), to: r.IsDBNull(6) ? (long?)null : r.GetInt64(6)), null, ("$i", refDocId)).SingleOrDefault();
        if (o == default || o.order is null || o.scope is null || o.t != "OUT" || o.st != "CONFIRMED" || o.refd)
            throw new BusinessException("Xuất bù phải liên kết một phiếu cấp phát theo đơn đã xác nhận.");
        return (o.order.Value, o.scope, o.place, o.to);
    }

    /// <summary>
    /// Cảnh báo trước khi lập xuất bù. Bù thiếu: không vượt phần đang thiếu của đúng nơi nhận/phạm vi.
    /// Bù hao hụt: NPL phải có trong định mức của nơi nhận/phạm vi đó.
    /// </summary>
    public List<string> SupplementWarnings(long refDocId, IReadOnlyList<(string material, decimal qty)> lines, SupplementKind kind)
    {
        var (order, scope, place, _) = SourceIssue(refDocId);
        var cat = new CatalogRepository(c);
        var w = new List<string>();
        foreach (var (m, q) in lines)
        {
            var mid = cat.MaterialId(m);
            if (kind == SupplementKind.BuThieu)
            {
                var shortage = Math.Max(0m, -stock.CarryClosing(order, mid, scope, place));
                if (q > shortage) w.Add($"{m}: bù thiếu {AuditLog.N(q)} vượt phần đang thiếu {AuditLog.N(shortage)} của nơi nhận này.");
            }
            else if (stock.CarryAccountedDemand(order, mid, scope, place) <= 0)
                w.Add($"{m}: không có trong định mức đã cấp cho nơi nhận này.");
        }
        return w;
    }

    /// <summary>Xuất bù: phiếu bổ sung liên kết phiếu cấp gốc, cùng đơn / nơi nhận / phạm vi. Loại bù quyết định cách ghi sổ cân đối.</summary>
    public long CreateSupplement(long refDocId, string docNo, IReadOnlyList<(string material, decimal qty)> lines, SupplementKind kind = SupplementKind.BuThieu)
    {
        var (order, scope, place, toPlace) = SourceIssue(refDocId);
        OrderService.RequireOpen(c, order);
        var id = stock.CreateDraft("OUT", docNo, lines, order, kind == SupplementKind.BuHaoHut ? "Bù hao hụt vượt định mức" : "Bù thiếu");
        Db.Exec(c, "UPDATE stock_doc SET scope=$s, ref_doc_id=$r, issue_place_id=$p, to_place_id=$tp, sub_kind=$k WHERE id=$i", null,
            ("$s", scope), ("$r", refDocId), ("$p", place), ("$tp", toPlace ?? place), ("$k", kind == SupplementKind.BuHaoHut ? "BU_HAO_HUT" : "BU_THIEU"), ("$i", id));
        return id;
    }

    // ---------------- Trả NPL ----------------

    /// <summary>
    /// Trả NPL từ một nơi nhận về kho: khi xác nhận vừa tăng tồn vừa giảm lượng đã cấp của ĐÚNG nơi nhận/phạm vi
    /// (một nghiệp vụ, không tính hai lần). Không trả vượt lượng nơi đó đã nhận.
    /// </summary>
    public long CreateReturn(long orderId, long placeId, string scope, string docNo, IReadOnlyList<(string material, decimal qty)> lines)
    {
        OrderService.RequireOpen(c, orderId);
        IssueScopes.Require(scope);
        var id = stock.CreateDraft("RETURN", docNo, lines, orderId);
        Db.Exec(c, "UPDATE stock_doc SET scope=$s, issue_place_id=$p, from_place_id=$p WHERE id=$i", null, ("$s", scope), ("$p", placeId), ("$i", id));
        return id;
    }

    // ---------------- Điều chuyển NPL dư ----------------

    /// <summary>
    /// Điều chuyển NPL DƯ đang ở một nơi nhận (đơn A / nơi nhận / phạm vi) sang nơi nhận khác hoặc đơn khác cùng khách hàng.
    /// Chỉ chuyển trong phạm vi số dư dương của nguồn; không đổi tồn kho (NPL đã ở xưởng); ghi sổ cân đối cả hai phía trong một giao dịch.
    /// </summary>
    public long TransferSurplus(string docNo, long fromOrderId, long fromPlaceId, string fromScope, long toOrderId, long toPlaceId, string toScope,
        IReadOnlyList<SurplusLine> lines, string? reason = null)
    {
        if (fromOrderId == toOrderId && fromPlaceId == toPlaceId && fromScope == toScope) throw new BusinessException("Nơi chuyển và nơi nhận phải khác nhau.");
        if (lines.Count == 0) throw new BusinessException("Chưa có dòng nào.");
        OrderService.RequireOpen(c, fromOrderId); OrderService.RequireOpen(c, toOrderId);
        long Cust(long o) => Convert.ToInt64(Db.Scalar(c, "SELECT customer_id FROM \"order\" WHERE id=$o", null, ("$o", o)));
        if (Cust(fromOrderId) != Cust(toOrderId)) throw new BusinessException("Chỉ điều chuyển giữa các đơn cùng khách hàng; khác khách hàng dùng Chuyển NPL giữa khách hàng.");
        if (!Assignments(toOrderId).Any(a => a.PlaceId == toPlaceId && a.Scope == toScope))
            throw new BusinessException("Đơn nhận chưa giao việc cho nơi nhận / phạm vi này.");
        var cat = new CatalogRepository(c);
        var errs = new List<string>();
        var targetDemand = KeyDemand(toOrderId, toPlaceId, toScope);
        foreach (var (l, i) in lines.Select((l, i) => (l, i + 1)))
        {
            if (l.Qty <= 0) { errs.Add($"Dòng {i}: số lượng phải dương."); continue; }
            if (!targetDemand.ContainsKey(l.Material)) { errs.Add($"Dòng {i}: {l.Material} không có trong định mức của nơi nhận đích."); continue; }
            var surplus = stock.CarryClosing(fromOrderId, cat.MaterialId(l.Material), fromScope, fromPlaceId);
            if (l.Qty > surplus) errs.Add($"Dòng {i}: {l.Material} chuyển {AuditLog.N(l.Qty)} vượt số dư {AuditLog.N(Math.Max(0, surplus))} của nơi chuyển.");
        }
        if (errs.Count > 0) throw new BusinessException(string.Join("\n", errs));
        var now = Db.Date(DateTime.Now);
        var mids = lines.Select(l => l.Material).Distinct().ToDictionary(m => m, cat.MaterialId);   // tra mã trước khi mở transaction
        using var tx = c.BeginTransaction(deferred: false);
        var id = Db.Insert(c, @"INSERT INTO surplus_transfer(doc_no,from_order_id,from_place_id,from_scope,to_order_id,to_place_id,to_scope,reason,created_at)
                                VALUES($n,$fo,$fp,$fs,$to,$tp,$ts,$r,$t)", tx,
            ("$n", docNo), ("$fo", fromOrderId), ("$fp", fromPlaceId), ("$fs", fromScope), ("$to", toOrderId), ("$tp", toPlaceId), ("$ts", toScope), ("$r", reason), ("$t", now));
        foreach (var l in lines)
        {
            var mid = mids[l.Material];
            Db.Exec(c, "INSERT INTO surplus_transfer_line(transfer_id,material_id,qty) VALUES($i,$m,$q)", tx, ("$i", id), ("$m", mid), ("$q", Db.D(l.Qty)));
            Ledger(fromOrderId, mid, fromScope, fromPlaceId, -l.Qty, id, now, tx);
            Ledger(toOrderId, mid, toScope, toPlaceId, l.Qty, id, now, tx);
        }
        string Key(long o, long p, string s) => $"{Db.Scalar(c, "SELECT order_no FROM \"order\" WHERE id=$o", tx, ("$o", o))}/{Db.Scalar(c, "SELECT code FROM place WHERE id=$p", tx, ("$p", p))}/{IssueScopes.Label(s)}";
        AuditLog.Write(c, "stock_doc", "Điều chuyển dư", docNo, Key(fromOrderId, fromPlaceId, fromScope),
            Key(toOrderId, toPlaceId, toScope) + " · " + string.Join("; ", lines.Select(l => $"{l.Material}: {AuditLog.N(l.Qty)}")), reason, id, tx);
        tx.Commit();
        return id;
    }

    void Ledger(long order, long mid, string scope, long place, decimal issued, long transferId, string now, SqliteTransaction tx)
    {
        var opening = stock.CarryClosing(order, mid, scope, place, tx);
        Db.Exec(c, @"INSERT INTO carry_ledger(order_id,material_id,scope,place_id,opening,issued,demand,loss,closing,surplus_transfer_id,created_at)
                     VALUES($o,$m,$s,$p,$op,$i,'0','0',$cl,$st,$t)", tx,
            ("$o", order), ("$m", mid), ("$s", scope), ("$p", place), ("$op", Db.D(opening)), ("$i", Db.D(issued)), ("$cl", Db.D(opening + issued)), ("$st", transferId), ("$t", now));
    }

    /// <summary>Danh sách số dư đang DƯ (dương) của một đơn theo nơi nhận / phạm vi / mã NPL – nguồn cho điều chuyển.</summary>
    public List<(long PlaceId, string Place, string Scope, string Material, decimal Surplus)> Surpluses(long orderId) =>
        Settlement(orderId).Where(s => s.Balance > 0 && s.PlaceId is not null).Select(s => (s.PlaceId!.Value, s.Place, s.Scope, s.Material, s.Balance)).ToList();

    /// <summary>
    /// Đóng đơn có quyết toán: còn DƯ ở nơi nhận nào thì không cho đóng (phải trả kho hoặc điều chuyển);
    /// còn THIẾU thì chỉ đóng khi người dùng xác nhận (allowShortage).
    /// </summary>
    public void CloseOrder(long orderId, bool allowShortage = false)
    {
        var s = Settlement(orderId);
        var surplus = s.Where(x => Math.Round(x.Balance, 2) > 0).ToList();
        if (surplus.Count > 0)
            throw new BusinessException("Còn NPL dư, cần trả kho hoặc điều chuyển trước khi đóng đơn:\n" +
                string.Join("\n", surplus.Select(x => $"  {x.Place} / {IssueScopes.Label(x.Scope)} / {x.Material}: dư {AuditLog.N(x.Balance)} {x.Unit}")));
        var shortage = s.Where(x => Math.Round(x.Balance, 2) < 0).ToList();
        if (shortage.Count > 0 && !allowShortage)
            throw new ShortageOnCloseException("Đơn còn NPL chưa cấp đủ:\n" +
                string.Join("\n", shortage.Select(x => $"  {x.Place} / {IssueScopes.Label(x.Scope)} / {x.Material}: thiếu {AuditLog.N(-x.Balance)} {x.Unit}")));
        orders.SetStatus(orderId, false);
    }

    // ---------------- Quyết toán ----------------

    /// <summary>
    /// Quyết toán NPL của đơn theo nơi nhận / phạm vi / mã NPL:
    /// Số dư = đã cấp ròng (cấp + bù thiếu + nhận điều chuyển − trả − chuyển đi) − nhu cầu hiện tại theo giao việc. Bù hao hụt ghi riêng (đã tiêu hao).
    /// </summary>
    public List<SettlementLine> Settlement(long orderId)
    {
        var rows = Db.Query(c, @"SELECT l.place_id,COALESCE(p.name,'(chưa có nơi nhận)'),l.scope,m.code,m.name,m.unit,l.issued,l.loss,l.demand
                                 FROM carry_ledger l JOIN material m ON m.id=l.material_id LEFT JOIN place p ON p.id=l.place_id WHERE l.order_id=$o",
            r => (pid: r.IsDBNull(0) ? (long?)null : r.GetInt64(0), place: r.GetString(1), scope: r.GetString(2), mat: r.GetString(3), name: r.GetString(4), unit: r.GetString(5),
                  issued: Db.D(r.GetString(6)), loss: Db.D(r.GetString(7)), demand: Db.D(r.GetString(8))), null, ("$o", orderId));
        var keys = rows.Select(r => (r.pid, r.place, r.scope)).Distinct().ToList();
        // Thêm các nơi nhận đã giao việc nhưng chưa cấp lần nào
        foreach (var a in Assignments(orderId)) if (!keys.Any(k => k.pid == a.PlaceId && k.scope == a.Scope)) keys.Add((a.PlaceId, a.PlaceName, a.Scope));
        var result = new List<SettlementLine>();
        foreach (var k in keys)
        {
            Dictionary<string, decimal> demandNow;
            try { demandNow = k.pid is long p && Assignments(orderId).Any(a => a.PlaceId == p && a.Scope == k.scope) ? KeyDemand(orderId, p, k.scope) : new(); }
            catch (BusinessException) { demandNow = new(); }
            var kr = rows.Where(r => r.pid == k.pid && r.scope == k.scope).ToList();
            var legacy = k.pid is null || !IssueScopes.Values.Contains(k.scope);
            foreach (var mat in kr.Select(r => r.mat).Union(demandNow.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                var mr = kr.Where(r => r.mat == mat).ToList();
                var info = mr.FirstOrDefault();
                var (name, unit) = info.mat is not null ? (info.name, info.unit)
                    : Db.Query(c, "SELECT name,unit FROM material WHERE code=$c", r => (r.GetString(0), r.GetString(1)), null, ("$c", mat)).Single();
                var issued = mr.Where(r => r.issued > 0).Sum(r => r.issued);
                var returned = -mr.Where(r => r.issued < 0).Sum(r => r.issued);
                var loss = mr.Sum(r => r.loss);
                // Sổ cũ (trước khi có nơi nhận) dùng nhu cầu đã tính vào sổ
                var demand = legacy ? mr.Sum(r => r.demand) : demandNow.GetValueOrDefault(mat);
                result.Add(new SettlementLine(k.place, k.pid, k.scope, mat, name, unit, demand, issued - loss, loss, returned, issued - loss - returned - demand));
            }
        }
        return result.OrderBy(r => r.Place).ThenBy(r => r.Scope).ThenBy(r => r.Material, StringComparer.Ordinal).ToList();
    }
}
