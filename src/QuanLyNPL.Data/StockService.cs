using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

public sealed class StockService(SqliteConnection c)
{
    static readonly Dictionary<string, int> Sign = new()
    { ["IN"] = 1, ["RETURN"] = 1, ["ADJUST_UP"] = 1, ["OUT"] = -1, ["ADJUST_DOWN"] = -1 };

    /// <summary>
    /// Phiếu kho thuộc một khách hàng: phiếu theo đơn lấy khách hàng của đơn; phiếu khác lấy theo tham số hoặc suy ra từ NPL.
    /// Mọi NPL trong phiếu phải thuộc khách hàng đó.
    /// </summary>
    public long CreateDraft(string type, string docNo, IReadOnlyList<(string material, decimal qty)> lines, long? orderId = null, string? note = null, long? customerId = null)
    {
        if (!Sign.ContainsKey(type)) throw new BusinessException($"Loại phiếu không hợp lệ: {type}");
        var cat = new CatalogRepository(c);
        foreach (var (m, q) in lines)
            if (q < 0) throw new BusinessException($"Số lượng âm ở NPL {m}");
        long? cust = customerId;
        if (orderId is long oid)
        {
            var oc = Convert.ToInt64(Db.Scalar(c, "SELECT customer_id FROM \"order\" WHERE id=$o", null, ("$o", oid)) ?? throw new BusinessException("Đơn hàng không tồn tại."));
            if (cust is not null && cust != oc) throw new BusinessException("Khách hàng của phiếu khác khách hàng của đơn.");
            cust = oc;
        }
        cust = cat.ResolveDocCustomer(lines.Select(l => l.material), cust);
        var resolved = lines.Select(l => (id: cat.MaterialId(l.material), l.qty)).ToList();   // tra mã trước khi mở transaction
        using var tx = c.BeginTransaction();
        var id = Db.Insert(c, "INSERT INTO stock_doc(type,doc_no,order_id,note,created_at,customer_id) VALUES($t,$n,$o,$note,$d,$k)", tx,
            ("$t", type), ("$n", docNo), ("$o", orderId), ("$note", note), ("$d", Db.Date(DateTime.Now)), ("$k", cust));
        foreach (var (m, q) in resolved)
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty) VALUES($d,$m,$q)", tx,
                ("$d", id), ("$m", m), ("$q", Db.D(q)));
        tx.Commit();
        return id;
    }

    public decimal OnHand(string materialCode, SqliteTransaction? tx = null) =>
        Db.Query(c, "SELECT t.qty_signed FROM stock_txn t JOIN material m ON m.id=t.material_id WHERE m.code=$c",
            r => Db.D(r.GetString(0)), tx, ("$c", materialCode)).Sum();

    public decimal ReservedForOthers(string materialCode, long? exceptOrderId, SqliteTransaction? tx = null) =>
        Db.Query(c, "SELECT r.qty FROM reservation r JOIN material m ON m.id=r.material_id WHERE m.code=$c AND r.active=1 AND ($o IS NULL OR r.order_id<>$o)",
            r => Db.D(r.GetString(0)), tx, ("$c", materialCode), ("$o", exceptOrderId)).Sum();

    /// <summary>
    /// Xác nhận phiếu trong một transaction nguyên tử: chỉ lúc này mới ghi stock_txn (và sổ carry nếu là phiếu cấp/trả/bù theo đơn).
    /// Không xác nhận phiếu rỗng/đã xác nhận; xuất không được làm tồn khả dụng âm.
    /// </summary>
    public void Confirm(long docId)
    {
        using var tx = c.BeginTransaction(deferred: false);   // BEGIN IMMEDIATE: khóa ghi, tránh 2 phiếu cùng dùng 1 lượng tồn
        var doc = Db.Query(c, "SELECT type,status,order_id,scope,ref_doc_id,kind FROM stock_doc WHERE id=$i",
            r => (type: r.GetString(0), status: r.GetString(1), order: r.IsDBNull(2) ? (long?)null : r.GetInt64(2),
                  scope: r.IsDBNull(3) ? null : r.GetString(3), refDoc: r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                  kind: r.IsDBNull(5) ? null : r.GetString(5)), tx, ("$i", docId)).SingleOrDefault();
        if (doc == default) throw new BusinessException("Phiếu không tồn tại.");
        if (doc.kind == TransferService.Kind) throw new BusinessException("Phiếu chuyển NPL giữa khách hàng phải xác nhận ở chức năng Chuyển NPL.");
        if (doc.status != "DRAFT") throw new BusinessException("Phiếu đã được xác nhận.");
        var lines = Db.Query(c, "SELECT l.material_id,m.code,l.qty,l.entered,l.demand FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE l.doc_id=$d",
            r => (id: r.GetInt64(0), code: r.GetString(1), qty: Db.D(r.GetString(2)), entered: r.GetInt32(3) == 1,
                  demand: r.IsDBNull(4) ? 0m : Db.D(r.GetString(4))), tx, ("$d", docId));
        var isIssue = doc.type == "OUT" && doc.scope is not null && doc.refDoc is null;   // phiếu cấp theo nhu cầu
        var isSupplement = doc.type == "OUT" && doc.scope is not null && doc.refDoc is not null;
        var isReturn = doc.type == "RETURN" && doc.order is not null && doc.scope is not null;
        if (lines.Count == 0) throw new BusinessException("Phiếu rỗng, không thể xác nhận.");
        if (isIssue)
        {
            var missing = lines.Where(l => !l.entered).Select(l => l.code).ToList();
            if (missing.Count > 0) throw new BusinessException("Kho chưa nhập thực cấp cho: " + string.Join(", ", missing) + " (nhập 0 nếu không cấp).");
        }
        else if (lines.All(l => l.qty == 0)) throw new BusinessException("Phiếu rỗng, không thể xác nhận.");
        var sign = Sign[doc.type];

        if (sign < 0)
            foreach (var g in lines.GroupBy(l => l.code))
            {
                var avail = OnHand(g.Key, tx) - ReservedForOthers(g.Key, doc.order, tx);
                var need = g.Sum(x => x.qty);
                if (need > avail) throw new BusinessException($"Không đủ tồn khả dụng cho {g.Key}: cần {need}, khả dụng {avail}.");
            }
        if (isReturn)
            foreach (var l in lines)
            {
                var net = CarryIssued(doc.order!.Value, l.id, doc.scope!, tx);
                if (l.qty > net) throw new BusinessException($"Trả {l.code} vượt lượng đã cấp cho đơn (đã cấp {net}, trả {l.qty}).");
            }

        var now = Db.Date(DateTime.Now);
        foreach (var l in lines.Where(l => l.qty > 0))
            Db.Exec(c, "INSERT INTO stock_txn(doc_id,material_id,qty_signed,created_at) VALUES($d,$m,$q,$t)", tx,
                ("$d", docId), ("$m", l.id), ("$q", Db.D(sign * l.qty)), ("$t", now));

        if (isIssue || isSupplement || isReturn)
            foreach (var l in lines)
            {
                var opening = CarryClosing(doc.order!.Value, l.id, doc.scope!, tx);
                var issued = isReturn ? -l.qty : l.qty;
                var demand = isIssue ? l.demand : 0m;     // bù/trả không tạo nhu cầu mới
                Db.Exec(c, "INSERT INTO carry_ledger(order_id,material_id,scope,opening,issued,demand,closing,doc_id,created_at) VALUES($o,$m,$s,$op,$i,$d,$cl,$doc,$t)", tx,
                    ("$o", doc.order), ("$m", l.id), ("$s", doc.scope), ("$op", Db.D(opening)), ("$i", Db.D(issued)), ("$d", Db.D(demand)),
                    ("$cl", Db.D(opening + issued - demand)), ("$doc", docId), ("$t", now));
            }
        if (sign < 0 && doc.order is not null)   // lượng đã cấp thực tế tiêu thụ phần giữ kho của chính đơn
            foreach (var l in lines.Where(l => l.qty > 0)) ConsumeReservation(doc.order.Value, l.id, l.qty, tx);

        Db.Exec(c, "UPDATE stock_doc SET status='CONFIRMED', confirmed_at=$t WHERE id=$i", tx, ("$t", now), ("$i", docId));
        tx.Commit();
    }

    void ConsumeReservation(long orderId, long materialId, decimal qty, SqliteTransaction tx)
    {
        var rs = Db.Query(c, "SELECT id,qty FROM reservation WHERE order_id=$o AND material_id=$m AND active=1", r => (r.GetInt64(0), Db.D(r.GetString(1))), tx, ("$o", orderId), ("$m", materialId));
        foreach (var (id, q) in rs)
        {
            var left = Math.Max(0m, q - qty);
            Db.Exec(c, "UPDATE reservation SET qty=$q, active=$a WHERE id=$i", tx, ("$q", Db.D(left)), ("$a", left > 0 ? 1 : 0), ("$i", id));
        }
    }

    /// <summary>Số dư cuối của bản ghi MỚI NHẤT trong đúng phạm vi (không SUM).</summary>
    public decimal CarryClosing(long orderId, long materialId, string scope, SqliteTransaction? tx = null) =>
        Db.Query(c, "SELECT closing FROM carry_ledger WHERE order_id=$o AND material_id=$m AND scope=$s ORDER BY id DESC LIMIT 1",
            r => Db.D(r.GetString(0)), tx, ("$o", orderId), ("$m", materialId), ("$s", scope)).FirstOrDefault();

    /// <summary>Tổng nhu cầu đã được tính vào sổ (cộng các nhu cầu theo kỳ, không phải số dư).</summary>
    public decimal CarryAccountedDemand(long orderId, long materialId, string scope, SqliteTransaction? tx = null) =>
        Db.Query(c, "SELECT demand FROM carry_ledger WHERE order_id=$o AND material_id=$m AND scope=$s",
            r => Db.D(r.GetString(0)), tx, ("$o", orderId), ("$m", materialId), ("$s", scope)).Sum();

    /// <summary>Lượng đã cấp ròng cho đơn (cấp + bù − trả).</summary>
    public decimal CarryIssued(long orderId, long materialId, string scope, SqliteTransaction? tx = null) =>
        Db.Query(c, "SELECT issued FROM carry_ledger WHERE order_id=$o AND material_id=$m AND scope=$s",
            r => Db.D(r.GetString(0)), tx, ("$o", orderId), ("$m", materialId), ("$s", scope)).Sum();

    /// <summary>Hủy phiếu nháp (chưa phát sinh giao dịch nên được xóa; có ghi nhật ký). Phiếu đã xác nhận thì không xóa.</summary>
    public void DeleteDraft(long docId, string reason)
    {
        using var tx = c.BeginTransaction(deferred: false);
        var d = Db.Query(c, "SELECT status,doc_no,COALESCE(kind,'') FROM stock_doc WHERE id=$i", r => (r.GetString(0), r.GetString(1), r.GetString(2)), tx, ("$i", docId)).SingleOrDefault();
        if (d == default) throw new BusinessException("Phiếu không tồn tại.");
        if (d.Item3 == TransferService.Kind) throw new BusinessException("Phiếu chuyển NPL phải hủy ở chức năng Chuyển NPL.");
        if (d.Item1 != "DRAFT") throw new BusinessException("Phiếu đã xác nhận, không xóa; dùng trả/điều chỉnh/phiếu đảo.");
        if (Convert.ToInt32(Db.Scalar(c, "SELECT COUNT(*) FROM stock_doc WHERE ref_doc_id=$i", tx, ("$i", docId))) > 0)
            throw new BusinessException("Phiếu đang được phiếu khác tham chiếu.");
        Db.Exec(c, "DELETE FROM stock_doc_line WHERE doc_id=$i", tx, ("$i", docId));
        Db.Exec(c, "DELETE FROM stock_doc WHERE id=$i", tx, ("$i", docId));
        Db.Exec(c, "INSERT INTO audit_log(entity,entity_id,before_json,reason,created_at) VALUES('stock_doc_draft',$i,$b,$r,$t)", tx,
            ("$i", docId), ("$b", d.Item2), ("$r", reason), ("$t", Db.Date(DateTime.Now)));
        tx.Commit();
    }

    public void Reserve(long orderId, string materialCode, decimal qty)
    {
        if (qty <= 0) throw new BusinessException("Số lượng giữ phải dương.");
        var oc = Convert.ToInt64(Db.Scalar(c, "SELECT customer_id FROM \"order\" WHERE id=$o", null, ("$o", orderId)) ?? throw new BusinessException("Đơn hàng không tồn tại."));
        new CatalogRepository(c).RequireMaterialOfCustomer(materialCode, oc);
        var avail = OnHand(materialCode) - ReservedForOthers(materialCode, orderId);
        if (qty > avail) throw new BusinessException($"Không đủ tồn khả dụng để giữ {materialCode}: khả dụng {avail}.");
        var mid = new CatalogRepository(c).MaterialId(materialCode);
        using var tx = c.BeginTransaction();
        Db.Exec(c, "UPDATE reservation SET active=0 WHERE order_id=$o AND material_id=$m AND active=1", tx, ("$o", orderId), ("$m", mid));
        Db.Exec(c, "INSERT INTO reservation(order_id,material_id,qty) VALUES($o,$m,$q)", tx, ("$o", orderId), ("$m", mid), ("$q", Db.D(qty)));
        tx.Commit();
    }

    /// <summary>Kiểm tra khả năng đáp ứng của đơn: nhu cầu từ snapshot so với tồn khả dụng (trừ lượng giữ cho đơn khác).</summary>
    public AvailabilityResult CheckOrder(long orderId, OrderService orders)
    {
        var demand = orders.CalculateDemand(orderId);
        var pos = demand.TotalByMaterial.Keys.Select(m => new StockPosition(m, OnHand(m), ReservedForOthers(m, orderId)));
        return Stock.Check(demand, pos);
    }
}
