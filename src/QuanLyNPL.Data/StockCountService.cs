using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

/// <summary>Kiểm kê theo mã NPL. Chênh lệch tính tại thời điểm chốt; dương → điều chỉnh tăng, âm → điều chỉnh giảm.</summary>
public sealed class StockCountService(SqliteConnection c, StockService stock)
{
    /// <summary>Phiếu kiểm kê thuộc một khách hàng (chọn hoặc suy ra từ NPL); mọi NPL phải thuộc khách hàng đó.</summary>
    public long Create(string docNo, IReadOnlyList<(string material, decimal counted)> lines, string? reason = null, long? customerId = null)
    {
        if (lines.Any(l => l.counted < 0)) throw new BusinessException("Số thực đếm không được âm.");
        if (lines.GroupBy(l => l.material).Any(g => g.Count() > 1)) throw new BusinessException("Trùng mã NPL trong phiếu kiểm kê.");
        var cat = new CatalogRepository(c);
        var cust = cat.ResolveDocCustomer(lines.Select(l => l.material), customerId);
        var ids = lines.Select(l => (id: cat.MaterialId(l.material), l.counted)).ToList();
        using var tx = c.BeginTransaction();
        var id = Db.Insert(c, "INSERT INTO stock_count(doc_no,reason,created_at,customer_id) VALUES($n,$r,$t,$k)", tx, ("$n", docNo), ("$r", reason), ("$t", Db.Date(DateTime.Now)), ("$k", cust));
        foreach (var (m, q) in ids)
            Db.Exec(c, "INSERT INTO stock_count_line(count_id,material_id,counted) VALUES($c,$m,$q)", tx, ("$c", id), ("$m", m), ("$q", Db.D(q)));
        tx.Commit();
        return id;
    }

    /// <summary>Hủy phiếu kiểm kê nháp (chưa phát sinh điều chỉnh), có ghi nhật ký.</summary>
    public void DeleteDraft(long countId, string reason)
    {
        using var tx = c.BeginTransaction();
        var st = Db.Scalar(c, "SELECT status FROM stock_count WHERE id=$i", tx, ("$i", countId)) as string ?? throw new BusinessException("Phiếu kiểm kê không tồn tại.");
        if (st != "DRAFT") throw new BusinessException("Phiếu kiểm kê đã chốt, không xóa.");
        Db.Exec(c, "DELETE FROM stock_count_line WHERE count_id=$i", tx, ("$i", countId));
        Db.Exec(c, "DELETE FROM stock_count WHERE id=$i", tx, ("$i", countId));
        Db.Exec(c, "INSERT INTO audit_log(entity,entity_id,reason,created_at) VALUES('stock_count_draft',$i,$r,$t)", tx, ("$i", countId), ("$r", reason), ("$t", Db.Date(DateTime.Now)));
        tx.Commit();
    }

    public void Confirm(long countId)
    {
        using var tx = c.BeginTransaction(deferred: false);
        var st = Db.Query(c, "SELECT status,doc_no FROM stock_count WHERE id=$i", r => (r.GetString(0), r.GetString(1)), tx, ("$i", countId)).SingleOrDefault();
        if (st == default) throw new BusinessException("Phiếu kiểm kê không tồn tại.");
        if (st.Item1 != "DRAFT") throw new BusinessException("Phiếu kiểm kê đã xác nhận.");
        var lines = Db.Query(c, "SELECT l.id,l.material_id,m.code,l.counted FROM stock_count_line l JOIN material m ON m.id=l.material_id WHERE l.count_id=$c",
            r => (lid: r.GetInt64(0), mid: r.GetInt64(1), code: r.GetString(2), counted: Db.D(r.GetString(3))), tx, ("$c", countId));
        if (lines.Count == 0) throw new BusinessException("Phiếu kiểm kê rỗng.");
        var now = Db.Date(DateTime.Now);
        long? up = null, down = null;
        foreach (var l in lines)
        {
            var book = stock.OnHand(l.code, tx);
            var diff = l.counted - book;
            Db.Exec(c, "UPDATE stock_count_line SET book_qty=$b, diff=$d WHERE id=$i", tx, ("$b", Db.D(book)), ("$d", Db.D(diff)), ("$i", l.lid));
            if (diff == 0) continue;
            ref long? target = ref (diff > 0 ? ref up : ref down);
            target ??= Db.Insert(c, "INSERT INTO stock_doc(type,doc_no,status,note,created_at,confirmed_at,customer_id) VALUES($t,$n,'CONFIRMED',$note,$d,$d,(SELECT customer_id FROM stock_count WHERE id=$cid))", tx, ("$cid", countId),
                ("$t", diff > 0 ? "ADJUST_UP" : "ADJUST_DOWN"), ("$n", st.Item2 + (diff > 0 ? "-TANG" : "-GIAM")), ("$note", "Điều chỉnh từ kiểm kê"), ("$d", now));
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty) VALUES($d,$m,$q)", tx, ("$d", target), ("$m", l.mid), ("$q", Db.D(Math.Abs(diff))));
            Db.Exec(c, "INSERT INTO stock_txn(doc_id,material_id,qty_signed,created_at) VALUES($d,$m,$q,$t)", tx, ("$d", target), ("$m", l.mid), ("$q", Db.D(diff)), ("$t", now));
            Db.Exec(c, "INSERT INTO audit_log(entity,entity_id,before_json,after_json,reason,created_at) VALUES('stock_count',$i,$b,$a,$r,$t)", tx,
                ("$i", countId), ("$b", JsonSerializer.Serialize(new { l.code, book })), ("$a", JsonSerializer.Serialize(new { l.code, counted = l.counted })),
                ("$r", Db.Scalar(c, "SELECT reason FROM stock_count WHERE id=$i", tx, ("$i", countId)) as string), ("$t", now));
        }
        Db.Exec(c, "UPDATE stock_count SET status='CONFIRMED', confirmed_at=$t WHERE id=$i", tx, ("$t", now), ("$i", countId));
        tx.Commit();
    }
}
