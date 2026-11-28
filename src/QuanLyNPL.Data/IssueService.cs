using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

/// <summary>Số lượng một phiếu cấp phục vụ: đối tượng sản xuất (mã chủng loại, hoặc "(Bộ)" cho BOM theo bộ) × size.</summary>
public sealed record IssueQty(string Object, string Size, decimal Qty);

/// <summary>Tiến độ cấp theo size của một đối tượng: số lượng đơn hàng, đã cấp (đã xác nhận), đang ở phiếu nháp, còn lại.</summary>
public sealed record SizeProgress(string Size, decimal Ordered, decimal Issued, decimal Draft)
{
    public decimal Remaining => Ordered - Issued - Draft;
}

public sealed record IssuableObject(string Code, string Label, IReadOnlyList<SizeProgress> Sizes);

public sealed record SettlementLine(string Place, long? PlaceId, string Scope, string Material, string MaterialName, string Unit,
    decimal Demand, decimal Issued, decimal Loss, decimal Returned, decimal Balance)
{
    public string Conclusion => Balance > 0 ? "Dư" : Balance < 0 ? "Thiếu" : "Đủ";
}

public sealed record SurplusLine(string Material, decimal Qty);

/// <summary>Đóng đơn khi còn thiếu (chưa cấp hết số lượng đơn hoặc nơi nhận còn thiếu NPL) cần người dùng xác nhận.</summary>
public sealed class ShortageOnCloseException(string message) : BusinessException(message);

/// <summary>
/// Cấp phát NPL theo SỐ LƯỢNG SẢN PHẨM cho NƠI NHẬN:
/// đơn hàng = tổng cần sản xuất (phân rã theo chủng loại × size). Mỗi phiếu cấp ghi nơi nhận + loại BOM
/// (Tổ cắt → nhà cắt; Sản xuất = BOM chủng loại → các tổ sản xuất; Đóng gói theo bộ) và số lượng từng chủng loại (hoặc bộ) theo size.
/// Nhu cầu NPL của phiếu = định mức × số lượng. Lũy kế số lượng cấp mỗi đối tượng/size không vượt đơn hàng.
/// Cân đối (thừa/thiếu) tính riêng theo nơi nhận + loại BOM; số dư của tổ này không dùng cho tổ khác.
/// </summary>
public sealed class IssueService(SqliteConnection c, OrderService orders, StockService stock)
{
    // ---------------- Đối tượng & tiến độ cấp theo size ----------------

    Dictionary<string, string?> BomTypes(OrderSnapshot snap) => snap.Boms.ToDictionary(b => b.Id, b => b.Type ??
        Db.Scalar(c, "SELECT type FROM bom WHERE id=$i", null, ("$i", long.TryParse(b.Id, out var id) ? id : 0)) as string);

    /// <summary>
    /// Các đối tượng cấp được cho một loại BOM, kèm tiến độ theo size: chủng loại có BOM loại đó (số lượng = số bộ × SL/bộ),
    /// và "(Bộ)" nếu loại đó khai báo theo bộ (số lượng = số bộ).
    /// </summary>
    public List<IssuableObject> Objects(long orderId, string type)
    {
        IssueScopes.Require(type);
        var snap = orders.LoadSnapshot(orderId);
        var types = BomTypes(snap);
        var boms = snap.Boms.Where(b => types.GetValueOrDefault(b.Id) == type).ToList();
        var lines = orders.LoadLines(orderId);
        var issued = IssuedQuantities(orderId, type);
        var result = new List<IssuableObject>();
        SizeProgress P(string obj, string size, decimal ordered) =>
            new(size, ordered, issued.GetValueOrDefault((obj, size, true)), issued.GetValueOrDefault((obj, size, false)));
        if (boms.Any(b => b.PerSet))
            result.Add(new IssuableObject(IssueScopes.SetObject, "Cả bộ", lines.Select(l => P(IssueScopes.SetObject, l.DefaultSize, l.Sets)).ToList()));
        var variantCodes = boms.Any(b => !b.PerSet && b.ScopeVariant is null)
            ? snap.Variants.Select(v => v.Code).ToList()
            : boms.Where(b => !b.PerSet && b.ScopeVariant is not null).Select(b => b.ScopeVariant!).Distinct().ToList();
        var names = Db.Query(c, "SELECT v.code,v.name FROM variant v JOIN \"order\" o ON o.product_id=v.product_id WHERE o.id=$o", r => (r.GetString(0), r.GetString(1)), null, ("$o", orderId))
            .ToDictionary(x => x.Item1, x => x.Item2);
        foreach (var v in snap.Variants.Where(v => variantCodes.Contains(v.Code)))
            result.Add(new IssuableObject(v.Code, names.GetValueOrDefault(v.Code, v.Code), lines.Select(l => P(v.Code, l.DefaultSize, l.Sets * v.QtyPerSet)).ToList()));
        return result;
    }

    /// <summary>Số lượng đã đưa vào phiếu cấp (true = đã xác nhận, false = đang nháp) theo đối tượng/size.</summary>
    Dictionary<(string obj, string size, bool confirmed), decimal> IssuedQuantities(long orderId, string type, SqliteTransaction? tx = null) =>
        Db.Query(c, @"SELECT q.object,q.size_code,d.status,q.qty FROM issue_qty q JOIN stock_doc d ON d.id=q.doc_id
                      WHERE d.order_id=$o AND d.scope=$t AND d.type='OUT' AND d.ref_doc_id IS NULL",
                r => (obj: r.GetString(0), size: r.GetString(1), conf: r.GetString(2) == "CONFIRMED", q: Db.D(r.GetString(3))), tx, ("$o", orderId), ("$t", type))
            .GroupBy(x => (x.obj, x.size, x.conf)).ToDictionary(g => g.Key, g => g.Sum(x => x.q));

    /// <summary>Số lượng ghi trên một phiếu cấp.</summary>
    public List<IssueQty> DocQuantities(long docId) =>
        Db.Query(c, "SELECT object,size_code,qty FROM issue_qty WHERE doc_id=$d ORDER BY id", r => new IssueQty(r.GetString(0), r.GetString(1), Db.D(r.GetString(2))), null, ("$d", docId));

    /// <summary>Nhu cầu NPL (chưa làm tròn) của một tập số lượng: định mức các BOM thuộc loại × số lượng chủng loại/bộ theo size.</summary>
    public (Dictionary<string, decimal> demand, List<string> issues) DemandFor(long orderId, string type, IReadOnlyList<IssueQty> qty)
    {
        var snap = orders.LoadSnapshot(orderId);
        var types = BomTypes(snap);
        var boms = snap.Boms.Where(b => types.GetValueOrDefault(b.Id) == type).ToList();
        var demand = new Dictionary<string, decimal>(); var issues = new List<string>();
        foreach (var g in qty.Where(q => q.Qty > 0).GroupBy(q => q.Object))
        {
            var isSet = g.Key == IssueScopes.SetObject;
            var bs = boms.Where(b => isSet ? b.PerSet : !b.PerSet && (b.ScopeVariant == g.Key || b.ScopeVariant is null)).ToList();
            var r = DemandCalculator.Calculate(
                g.Select(q => new OrderLine(q.Size, q.Qty)).ToList(),
                isSet ? Array.Empty<VariantConfig>() : new[] { new VariantConfig(g.Key, 1m) },
                isSet ? new SizeMap() : g.Aggregate(new SizeMap(), (m, q) => m.Add(g.Key, q.Size, q.Size)),
                bs.Select(b => new Bom(b.Id, b.ScopeVariant, b.PerSet, b.Name)),
                bs.SelectMany(b => b.Lines.Select(l => new BomLine(b.Id, l.Material, l.Size, l.Usage, l.Waste, l.Variant))));
            issues.AddRange(r.Issues.Select(i => i.Message));
            if (bs.Count == 0) issues.Add($"Chưa có BOM {IssueScopes.Label(type)} cho {(isSet ? "cả bộ" : "chủng loại " + g.Key)}.");
            foreach (var s in r.Sources) demand[s.MaterialCode] = demand.GetValueOrDefault(s.MaterialCode) + s.Qty;
        }
        return (demand, issues.Distinct().ToList());
    }

    // ---------------- Phiếu cấp ----------------

    /// <summary>
    /// Phiếu xuất nháp cấp NPL cho một nơi nhận theo loại BOM và số lượng sản phẩm theo size.
    /// Kiểm tra: lũy kế số lượng (đã xác nhận + nháp) của từng đối tượng/size không vượt đơn hàng.
    /// Đề xuất = max(0, nhu cầu của số lượng này − số dư của nơi nhận/loại BOM); NPL nơi nhận đang thiếu từ lần trước được cộng vào.
    /// </summary>
    public long CreateIssueDraft(long orderId, long placeId, string type, string docNo, IReadOnlyList<IssueQty> quantities)
    {
        OrderService.RequireOpen(c, orderId);
        IssueScopes.Require(type);
        RequireReceiver(placeId);
        var drafts = Convert.ToInt32(Db.Scalar(c, "SELECT COUNT(*) FROM stock_doc WHERE order_id=$o AND issue_place_id=$p AND scope=$s AND type='OUT' AND status='DRAFT' AND ref_doc_id IS NULL",
            null, ("$o", orderId), ("$p", placeId), ("$s", type)));
        if (drafts > 0) throw new BusinessException("Nơi nhận này đã có phiếu xuất nháp cùng loại BOM chưa xác nhận.");
        var qty = quantities.Where(q => q.Qty != 0).ToList();
        var errs = new List<string>();
        if (qty.Any(q => q.Qty < 0)) errs.Add("Số lượng không được âm.");
        if (qty.GroupBy(q => (q.Object, q.Size)).Any(g => g.Count() > 1)) errs.Add("Trùng đối tượng/size.");
        var objects = Objects(orderId, type).ToDictionary(o => o.Code);
        foreach (var q in qty.Where(q => q.Qty > 0))
        {
            if (!objects.TryGetValue(q.Object, out var ob)) { errs.Add($"{q.Object}: không có BOM {IssueScopes.Label(type)}."); continue; }
            var sp = ob.Sizes.FirstOrDefault(s => s.Size == q.Size);
            if (sp is null) errs.Add($"{ob.Label} size {q.Size}: không có trong đơn.");
            else if (q.Qty > sp.Remaining)
                errs.Add($"{ob.Label} size {q.Size}: cấp {AuditLog.N(q.Qty)} vượt số lượng còn lại {AuditLog.N(sp.Remaining)} (đơn {AuditLog.N(sp.Ordered)}, đã cấp {AuditLog.N(sp.Issued)}, đang nháp {AuditLog.N(sp.Draft)}).");
        }
        var (raw, issues) = DemandFor(orderId, type, qty);
        errs.AddRange(issues.Select(i => "Dữ liệu chưa đủ: " + i));
        if (errs.Count > 0) throw new BusinessException(string.Join("\n", errs));
        var demand = raw.ToDictionary(kv => kv.Key, kv => DemandCalculator.Round(kv.Value));
        var cat = new CatalogRepository(c);
        // NPL nơi nhận đang thiếu từ lần cấp trước (số dư âm) cũng được đưa vào để cấp bù
        var owed = Db.Query(c, "SELECT DISTINCT m.code FROM carry_ledger l JOIN material m ON m.id=l.material_id WHERE l.order_id=$o AND l.place_id=$p AND l.scope=$s",
                r => r.GetString(0), null, ("$o", orderId), ("$p", placeId), ("$s", type))
            .Where(m => !demand.ContainsKey(m) && stock.CarryClosing(orderId, cat.MaterialId(m), type, placeId) < 0).ToList();
        var codes = demand.Keys.Union(owed).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (qty.Count == 0 && owed.Count == 0) throw new BusinessException("Chưa nhập số lượng cấp.");
        var mats = codes.ToDictionary(k => k, cat.MaterialId);

        using var tx = c.BeginTransaction(deferred: false);
        var id = Db.Insert(c, @"INSERT INTO stock_doc(type,doc_no,order_id,scope,issue_place_id,to_place_id,created_at,customer_id)
                                VALUES('OUT',$n,$o,$s,$p,$p,$d,(SELECT customer_id FROM ""order"" WHERE id=$o))", tx,
            ("$n", docNo), ("$o", orderId), ("$s", type), ("$p", placeId), ("$d", Db.Date(DateTime.Now)));
        foreach (var q in qty)
            Db.Exec(c, "INSERT INTO issue_qty(doc_id,object,size_code,qty) VALUES($d,$o,$s,$q)", tx, ("$d", id), ("$o", q.Object), ("$s", q.Size), ("$q", Db.D(q.Qty)));
        foreach (var code in codes)
        {
            var mid = mats[code];
            var period = demand.GetValueOrDefault(code);
            var suggested = DemandCalculator.Round(Math.Max(0m, period - stock.CarryClosing(orderId, mid, type, placeId, tx)));
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty,qty_suggested,demand,entered) VALUES($d,$m,'0',$s,$p,0)", tx,
                ("$d", id), ("$m", mid), ("$s", Db.D(suggested)), ("$p", Db.D(period)));
        }
        AuditLog.Write(c, "stock_doc", "Lập phiếu nháp", docNo, null,
            $"Cấp phát · {Db.Scalar(c, "SELECT name FROM place WHERE id=$p", tx, ("$p", placeId))} · {IssueScopes.Label(type)} · " +
            string.Join("; ", qty.Select(q => $"{(q.Object == IssueScopes.SetObject ? "Bộ" : q.Object)}/{q.Size}: {AuditLog.N(q.Qty)}")), null, id, tx);
        tx.Commit();
        return id;
    }

    void RequireReceiver(long placeId)
    {
        var place = Db.Query(c, "SELECT kind,active,name FROM place WHERE id=$i", r => (kind: r.GetString(0), active: r.GetInt32(1) == 1, name: r.GetString(2)), null, ("$i", placeId)).SingleOrDefault();
        if (place == default) throw new BusinessException("Nơi nhận không tồn tại.");
        if (!place.active) throw new BusinessException($"{place.name} đã ngừng sử dụng.");
        if (place.kind == "XUAT") throw new BusinessException($"{place.name} chỉ là nơi xuất, không nhận cấp phát.");
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

    /// <summary>Cảnh báo trước khi lập xuất bù. Bù thiếu: không vượt phần đang thiếu của nơi nhận. Bù hao hụt: NPL phải có trong định mức đã cấp cho nơi nhận.</summary>
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

    /// <summary>Xuất bù: phiếu bổ sung liên kết phiếu cấp gốc, cùng đơn / nơi nhận / loại BOM. Loại bù quyết định cách ghi sổ cân đối.</summary>
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
    /// Trả NPL từ một nơi nhận về kho: khi xác nhận vừa tăng tồn vừa giảm lượng đã cấp của ĐÚNG nơi nhận/loại BOM
    /// (một nghiệp vụ, không tính hai lần). Không trả vượt lượng nơi đó đã nhận.
    /// </summary>
    public long CreateReturn(long orderId, long placeId, string scope, string docNo, IReadOnlyList<(string material, decimal qty)> lines)
    {
        OrderService.RequireOpen(c, orderId);
        if (!IssueScopes.Types.Contains(scope) && !LedgerKeys(orderId).Any(k => k.place == placeId && k.scope == scope))
            throw new BusinessException("Loại BOM cấp phát không hợp lệ: " + scope);
        var id = stock.CreateDraft("RETURN", docNo, lines, orderId);
        Db.Exec(c, "UPDATE stock_doc SET scope=$s, issue_place_id=$p, from_place_id=$p WHERE id=$i", null, ("$s", scope), ("$p", placeId), ("$i", id));
        return id;
    }

    /// <summary>Các khóa (nơi nhận, loại BOM) đã có trong sổ cân đối của đơn.</summary>
    public List<(long? place, string placeName, string scope)> LedgerKeys(long orderId) =>
        Db.Query(c, @"SELECT DISTINCT l.place_id,COALESCE(p.name,'(chưa có nơi nhận)'),l.scope FROM carry_ledger l LEFT JOIN place p ON p.id=l.place_id WHERE l.order_id=$o ORDER BY 2,3",
            r => (r.IsDBNull(0) ? (long?)null : r.GetInt64(0), r.GetString(1), r.GetString(2)), null, ("$o", orderId));

    // ---------------- Điều chuyển NPL dư ----------------

    /// <summary>
    /// Điều chuyển NPL DƯ đang ở một nơi nhận (đơn / nơi nhận / loại BOM) sang nơi nhận khác hoặc đơn khác cùng khách hàng.
    /// Chỉ trong phạm vi số dư dương của nguồn; NPL phải có trong BOM loại đích của đơn nhận; không đổi tồn kho; ghi sổ hai phía trong một giao dịch.
    /// Phần dư được trừ vào đề xuất các lần cấp sau của nơi nhận đích.
    /// </summary>
    public long TransferSurplus(string docNo, long fromOrderId, long fromPlaceId, string fromScope, long toOrderId, long toPlaceId, string toScope,
        IReadOnlyList<SurplusLine> lines, string? reason = null)
    {
        if (fromOrderId == toOrderId && fromPlaceId == toPlaceId && fromScope == toScope) throw new BusinessException("Nơi chuyển và nơi nhận phải khác nhau.");
        if (lines.Count == 0) throw new BusinessException("Chưa có dòng nào.");
        IssueScopes.Require(toScope);
        OrderService.RequireOpen(c, fromOrderId); OrderService.RequireOpen(c, toOrderId);
        RequireReceiver(toPlaceId);
        long Cust(long o) => Convert.ToInt64(Db.Scalar(c, "SELECT customer_id FROM \"order\" WHERE id=$o", null, ("$o", o)));
        if (Cust(fromOrderId) != Cust(toOrderId)) throw new BusinessException("Chỉ điều chuyển giữa các đơn cùng khách hàng; khác khách hàng dùng Chuyển NPL giữa khách hàng.");
        var snap = orders.LoadSnapshot(toOrderId);
        var types = BomTypes(snap);
        var targetMats = snap.Boms.Where(b => types.GetValueOrDefault(b.Id) == toScope).SelectMany(b => b.Lines.Select(l => l.Material)).ToHashSet();
        var cat = new CatalogRepository(c);
        var errs = new List<string>();
        foreach (var (l, i) in lines.Select((l, i) => (l, i + 1)))
        {
            if (l.Qty <= 0) { errs.Add($"Dòng {i}: số lượng phải dương."); continue; }
            if (!targetMats.Contains(l.Material)) { errs.Add($"Dòng {i}: {l.Material} không có trong BOM {IssueScopes.Label(toScope)} của đơn nhận."); continue; }
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

    /// <summary>Số dư đang DƯ (dương) của một đơn theo nơi nhận / loại BOM / mã NPL – nguồn cho điều chuyển và trả kho.</summary>
    public List<(long PlaceId, string Place, string Scope, string Material, decimal Surplus)> Surpluses(long orderId) =>
        Settlement(orderId).Where(s => Math.Round(s.Balance, 4) > 0 && s.PlaceId is not null).Select(s => (s.PlaceId!.Value, s.Place, s.Scope, s.Material, s.Balance)).ToList();

    // ---------------- Quyết toán ----------------

    /// <summary>Tiến độ cấp theo size của mọi loại BOM và đối tượng: đơn hàng / đã cấp / đang nháp / còn lại.</summary>
    public List<(string Type, IssuableObject Obj, SizeProgress P)> Progress(long orderId) =>
        IssueScopes.Types.SelectMany(t => Objects(orderId, t).SelectMany(o => o.Sizes.Select(p => (t, o, p)))).ToList();

    /// <summary>
    /// Quyết toán NPL theo nơi nhận / loại BOM / mã NPL từ sổ cân đối:
    /// nhu cầu (định mức × số lượng đã cấp), đã cấp (không gồm hao hụt), bù hao hụt, trả/chuyển đi, số dư (= số dư cuối mới nhất).
    /// </summary>
    public List<SettlementLine> Settlement(long orderId)
    {
        var rows = Db.Query(c, @"SELECT l.place_id,COALESCE(p.name,'(chưa có nơi nhận)'),l.scope,m.code,m.name,m.unit,l.issued,l.loss,l.demand
                                 FROM carry_ledger l JOIN material m ON m.id=l.material_id LEFT JOIN place p ON p.id=l.place_id WHERE l.order_id=$o",
            r => (pid: r.IsDBNull(0) ? (long?)null : r.GetInt64(0), place: r.GetString(1), scope: r.GetString(2), mat: r.GetString(3), name: r.GetString(4), unit: r.GetString(5),
                  issued: Db.D(r.GetString(6)), loss: Db.D(r.GetString(7)), demand: Db.D(r.GetString(8))), null, ("$o", orderId));
        return rows.GroupBy(r => (r.pid, r.place, r.scope, r.mat, r.name, r.unit))
            .Select(g =>
            {
                var issued = g.Where(r => r.issued > 0).Sum(r => r.issued);
                var returned = -g.Where(r => r.issued < 0).Sum(r => r.issued);
                var loss = g.Sum(r => r.loss); var demand = g.Sum(r => r.demand);
                return new SettlementLine(g.Key.place, g.Key.pid, g.Key.scope, g.Key.mat, g.Key.name, g.Key.unit, demand, issued - loss, loss, returned, issued - loss - returned - demand);
            })
            .OrderBy(r => r.Place).ThenBy(r => r.Scope).ThenBy(r => r.Material, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Đóng đơn có quyết toán: nơi nhận còn DƯ thì không cho đóng (trả kho hoặc điều chuyển);
    /// còn THIẾU (nơi nhận thiếu NPL hoặc chưa cấp hết số lượng đơn) thì chỉ đóng khi người dùng xác nhận.
    /// </summary>
    public void CloseOrder(long orderId, bool allowShortage = false)
    {
        var s = Settlement(orderId);
        var surplus = s.Where(x => Math.Round(x.Balance, 2) > 0).ToList();
        if (surplus.Count > 0)
            throw new BusinessException("Còn NPL dư, cần trả kho hoặc điều chuyển trước khi đóng đơn:\n" +
                string.Join("\n", surplus.Select(x => $"  {x.Place} / {IssueScopes.Label(x.Scope)} / {x.Material}: dư {AuditLog.N(x.Balance)} {x.Unit}")));
        var msgs = s.Where(x => Math.Round(x.Balance, 2) < 0)
            .Select(x => $"  {x.Place} / {IssueScopes.Label(x.Scope)} / {x.Material}: thiếu {AuditLog.N(-x.Balance)} {x.Unit}").ToList();
        msgs.AddRange(Progress(orderId).Where(p => p.P.Ordered - p.P.Issued > 0)
            .Select(p => $"  {IssueScopes.Label(p.Type)} / {p.Obj.Label} size {p.P.Size}: chưa cấp {AuditLog.N(p.P.Ordered - p.P.Issued)} / {AuditLog.N(p.P.Ordered)}"));
        if (msgs.Count > 0 && !allowShortage) throw new ShortageOnCloseException("Đơn còn chưa cấp đủ:\n" + string.Join("\n", msgs));
        orders.SetStatus(orderId, false);
    }
}
