using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

public sealed class StockService(SqliteConnection c)
{
    static readonly Dictionary<string, int> Sign = new()
    { ["IN"] = 1, ["RETURN"] = 1, ["ADJUST_UP"] = 1, ["OUT"] = -1, ["ADJUST_DOWN"] = -1 };

    public long CreateDraft(string type, string docNo, IReadOnlyList<(string material, decimal qty)> lines, long? orderId = null, string? note = null)
    {
        if (!Sign.ContainsKey(type)) throw new BusinessException($"Loại phiếu không hợp lệ: {type}");
        var cat = new CatalogRepository(c);
        foreach (var (m, q) in lines)
            if (q < 0) throw new BusinessException($"Số lượng âm ở NPL {m}");
        var resolved = lines.Select(l => (id: cat.MaterialId(l.material), l.qty)).ToList();   // tra mã trước khi mở transaction
        using var tx = c.BeginTransaction();
        var id = Db.Insert(c, "INSERT INTO stock_doc(type,doc_no,order_id,note,created_at) VALUES($t,$n,$o,$note,$d)", tx,
            ("$t", type), ("$n", docNo), ("$o", orderId), ("$note", note), ("$d", Db.Date(DateTime.Now)));
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
    /// Xác nhận phiếu trong một transaction nguyên tử: chỉ lúc này mới ghi stock_txn.
    /// Không xác nhận phiếu rỗng/đã xác nhận; xuất không được làm tồn khả dụng âm.
    /// </summary>
    public void Confirm(long docId)
    {
        using var tx = c.BeginTransaction(deferred: false);   // BEGIN IMMEDIATE: khóa ghi, tránh 2 phiếu cùng dùng 1 lượng tồn
        var doc = Db.Query(c, "SELECT type,status,order_id FROM stock_doc WHERE id=$i",
            r => (type: r.GetString(0), status: r.GetString(1), order: r.IsDBNull(2) ? (long?)null : r.GetInt64(2)), tx, ("$i", docId)).SingleOrDefault();
        if (doc == default) throw new BusinessException("Phiếu không tồn tại.");
        if (doc.status != "DRAFT") throw new BusinessException("Phiếu đã được xác nhận.");
        var lines = Db.Query(c, "SELECT l.material_id,m.code,l.qty FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE l.doc_id=$d",
            r => (id: r.GetInt64(0), code: r.GetString(1), qty: Db.D(r.GetString(2))), tx, ("$d", docId));
        if (lines.Count == 0 || lines.All(l => l.qty == 0)) throw new BusinessException("Phiếu rỗng, không thể xác nhận.");
        var sign = Sign[doc.type];

        if (sign < 0)
            foreach (var g in lines.GroupBy(l => l.code))
            {
                var avail = OnHand(g.Key, tx) - ReservedForOthers(g.Key, doc.order, tx);
                var need = g.Sum(x => x.qty);
                if (need > avail) throw new BusinessException($"Không đủ tồn khả dụng cho {g.Key}: cần {need}, khả dụng {avail}.");
            }

        var now = Db.Date(DateTime.Now);
        foreach (var l in lines.Where(l => l.qty > 0))
            Db.Exec(c, "INSERT INTO stock_txn(doc_id,material_id,qty_signed,created_at) VALUES($d,$m,$q,$t)", tx,
                ("$d", docId), ("$m", l.id), ("$q", Db.D(sign * l.qty)), ("$t", now));
        Db.Exec(c, "UPDATE stock_doc SET status='CONFIRMED', confirmed_at=$t WHERE id=$i", tx, ("$t", now), ("$i", docId));
        tx.Commit();
    }

    public void Reserve(long orderId, string materialCode, decimal qty)
    {
        if (qty <= 0) throw new BusinessException("Số lượng giữ phải dương.");
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
