using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public sealed record TransferLine(string FromMaterial, string ToMaterial, decimal Qty);

/// <summary>
/// Dùng NPL của khách hàng A cho khách hàng B: một phiếu chuyển sinh ra cặp phiếu
/// A xuất (theo mã NPL của A) và B nhập (theo mã NPL của B), xác nhận cùng một transaction.
/// </summary>
public sealed class TransferService(SqliteConnection c, StockService stock)
{
    public const string Kind = "TRANSFER";

    public long Create(string docNo, long fromCustomerId, long toCustomerId, IReadOnlyList<TransferLine> lines, string? reason = null)
    {
        if (fromCustomerId == toCustomerId) throw new BusinessException("Khách hàng chuyển và nhận phải khác nhau.");
        if (lines.Count == 0) throw new BusinessException("Phiếu chuyển chưa có dòng nào.");
        var cat = new CatalogRepository(c);
        var errors = new List<string>();
        var resolved = new List<(long from, long to, decimal qty)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i]; var row = i + 1;
            if (l.Qty <= 0) errors.Add($"Dòng {row}: số lượng phải dương");
            try { cat.RequireMaterialOfCustomer(l.FromMaterial, fromCustomerId); } catch (BusinessException e) { errors.Add($"Dòng {row}: {e.Message}"); continue; }
            try { cat.RequireMaterialOfCustomer(l.ToMaterial, toCustomerId); } catch (BusinessException e) { errors.Add($"Dòng {row}: {e.Message}"); continue; }
            var (uf, ut) = (Unit(l.FromMaterial), Unit(l.ToMaterial));
            if (uf != ut) { errors.Add($"Dòng {row}: đơn vị tính khác nhau ({l.FromMaterial}: {uf}, {l.ToMaterial}: {ut})"); continue; }
            resolved.Add((cat.MaterialId(l.FromMaterial), cat.MaterialId(l.ToMaterial), l.Qty));
        }
        if (errors.Count > 0) throw new BusinessException(string.Join("\n", errors));

        var now = Db.Date(DateTime.Now);
        var note = $"Chuyển NPL {docNo}" + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}");
        using var tx = c.BeginTransaction(deferred: false);
        var outId = Db.Insert(c, "INSERT INTO stock_doc(type,doc_no,note,created_at,customer_id,kind) VALUES('OUT',$n,$note,$t,$k,$kind)", tx,
            ("$n", docNo + "-X"), ("$note", note), ("$t", now), ("$k", fromCustomerId), ("$kind", Kind));
        var inId = Db.Insert(c, "INSERT INTO stock_doc(type,doc_no,note,created_at,customer_id,kind,ref_doc_id) VALUES('IN',$n,$note,$t,$k,$kind,$r)", tx,
            ("$n", docNo + "-N"), ("$note", note), ("$t", now), ("$k", toCustomerId), ("$kind", Kind), ("$r", outId));
        var id = Db.Insert(c, "INSERT INTO material_transfer(doc_no,from_customer_id,to_customer_id,out_doc_id,in_doc_id,reason,created_at) VALUES($n,$f,$to,$o,$i,$r,$t)", tx,
            ("$n", docNo), ("$f", fromCustomerId), ("$to", toCustomerId), ("$o", outId), ("$i", inId), ("$r", reason), ("$t", now));
        foreach (var (from, to, qty) in resolved)
        {
            Db.Exec(c, "INSERT INTO material_transfer_line(transfer_id,from_material_id,to_material_id,qty) VALUES($id,$f,$to,$q)", tx, ("$id", id), ("$f", from), ("$to", to), ("$q", Db.D(qty)));
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty) VALUES($d,$m,$q)", tx, ("$d", outId), ("$m", from), ("$q", Db.D(qty)));
            Db.Exec(c, "INSERT INTO stock_doc_line(doc_id,material_id,qty) VALUES($d,$m,$q)", tx, ("$d", inId), ("$m", to), ("$q", Db.D(qty)));
        }
        AuditLog.Write(c, "material_transfer", "Lập phiếu nháp", docNo, null, string.Join("; ", lines.Select(l => $"{l.FromMaterial} → {l.ToMaterial}: {AuditLog.N(l.Qty)}")), reason, id, tx);
        tx.Commit();
        return id;
    }

    /// <summary>Xác nhận: kiểm tra tồn khả dụng của A, rồi ghi A xuất và B nhập trong cùng một transaction.</summary>
    public void Confirm(long transferId)
    {
        using var tx = c.BeginTransaction(deferred: false);
        var t = Db.Query(c, "SELECT status,out_doc_id,in_doc_id,doc_no,reason FROM material_transfer WHERE id=$i",
            r => (st: r.GetString(0), outId: r.GetInt64(1), inId: r.GetInt64(2), no: r.GetString(3), reason: r.IsDBNull(4) ? null : r.GetString(4)), tx, ("$i", transferId)).SingleOrDefault();
        if (t == default) throw new BusinessException("Phiếu chuyển không tồn tại.");
        if (t.st != "DRAFT") throw new BusinessException("Phiếu chuyển đã được xác nhận.");
        var lines = Db.Query(c, @"SELECT l.from_material_id,mf.code,l.to_material_id,mt.code,l.qty FROM material_transfer_line l
                                  JOIN material mf ON mf.id=l.from_material_id JOIN material mt ON mt.id=l.to_material_id WHERE l.transfer_id=$i",
            r => (fromId: r.GetInt64(0), from: r.GetString(1), toId: r.GetInt64(2), to: r.GetString(3), qty: Db.D(r.GetString(4))), tx, ("$i", transferId));
        foreach (var g in lines.GroupBy(l => l.from))
        {
            var avail = stock.OnHand(g.Key, tx) - stock.ReservedForOthers(g.Key, null, tx);
            var need = g.Sum(x => x.qty);
            if (need > avail) throw new BusinessException($"Không đủ tồn khả dụng cho {g.Key}: cần {need}, khả dụng {avail}.");
        }
        var now = Db.Date(DateTime.Now);
        foreach (var l in lines)
        {
            Db.Exec(c, "INSERT INTO stock_txn(doc_id,material_id,qty_signed,created_at) VALUES($d,$m,$q,$t)", tx, ("$d", t.outId), ("$m", l.fromId), ("$q", Db.D(-l.qty)), ("$t", now));
            Db.Exec(c, "INSERT INTO stock_txn(doc_id,material_id,qty_signed,created_at) VALUES($d,$m,$q,$t)", tx, ("$d", t.inId), ("$m", l.toId), ("$q", Db.D(l.qty)), ("$t", now));
        }
        Db.Exec(c, "UPDATE stock_doc SET status='CONFIRMED', confirmed_at=$t WHERE id IN ($o,$i)", tx, ("$t", now), ("$o", t.outId), ("$i", t.inId));
        Db.Exec(c, "UPDATE material_transfer SET status='CONFIRMED', confirmed_at=$t WHERE id=$id", tx, ("$t", now), ("$id", transferId));
        AuditLog.Write(c, "material_transfer", "Xác nhận", t.no, null, string.Join("; ", lines.Select(l => $"{l.from} → {l.to}: {AuditLog.N(l.qty)}")), t.reason, transferId, tx);
        tx.Commit();
    }

    /// <summary>Hủy phiếu chuyển nháp (xóa cả cặp phiếu nháp, có nhật ký).</summary>
    public void DeleteDraft(long transferId, string reason)
    {
        using var tx = c.BeginTransaction(deferred: false);
        var t = Db.Query(c, "SELECT status,out_doc_id,in_doc_id,doc_no FROM material_transfer WHERE id=$i",
            r => (st: r.GetString(0), outId: r.GetInt64(1), inId: r.GetInt64(2), no: r.GetString(3)), tx, ("$i", transferId)).SingleOrDefault();
        if (t == default) throw new BusinessException("Phiếu chuyển không tồn tại.");
        if (t.st != "DRAFT") throw new BusinessException("Phiếu chuyển đã xác nhận, không xóa; lập phiếu chuyển ngược nếu cần.");
        Db.Exec(c, "DELETE FROM material_transfer_line WHERE transfer_id=$i", tx, ("$i", transferId));
        Db.Exec(c, "DELETE FROM material_transfer WHERE id=$i", tx, ("$i", transferId));
        Db.Exec(c, "DELETE FROM stock_doc_line WHERE doc_id IN ($o,$n)", tx, ("$o", t.outId), ("$n", t.inId));
        Db.Exec(c, "DELETE FROM stock_doc WHERE id=$n", tx, ("$n", t.inId));
        Db.Exec(c, "DELETE FROM stock_doc WHERE id=$o", tx, ("$o", t.outId));
        AuditLog.Write(c, "material_transfer", "Hủy phiếu nháp", t.no, reason: reason, entityId: transferId, tx: tx);
        tx.Commit();
    }

    string Unit(string code) => (string)Db.Scalar(c, "SELECT unit FROM material WHERE code=$c", null, ("$c", code))!;
}
