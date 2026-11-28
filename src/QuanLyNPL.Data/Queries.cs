using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public sealed record CustomerRow(long Id, string Code, string Name, bool Active);
public sealed record MaterialRow(long Id, string Code, string Name, string Unit, long? CustomerId, string CustomerCode, bool Active, string? Note);
public sealed record SizeGridRow(long Id, string Code, string Name, bool Active, string Sizes, bool Used);
public sealed record ProductRow(long Id, string Code, string Name, long CustomerId, string CustomerCode, long GridId, string GridCode, bool Active, int VariantCount);
public sealed record VariantRow(long Id, string Code, string Name, string Group, decimal QtyPerSet);
public sealed record BomRow(long Id, string Name, string Type, bool PerSet, long? VariantId, string VariantCode, int VersionCount);
public sealed record BomVersionRow(long Id, int No, DateTime CreatedAt);
public sealed record BomLineRow(string Material, string MaterialName, string Size, decimal Usage, decimal Waste);
public sealed record OrderRow(long Id, string No, long CustomerId, string CustomerCode, long ProductId, string ProductCode, DateTime OrderDate, DateTime? DueDate, string Status, decimal TotalSets);
public sealed record DocRow(long Id, string Type, string DocNo, string Status, long? OrderId, string OrderNo, string CustomerCode, string? Scope, string? Kind, long? RefDocId, DateTime CreatedAt, DateTime? ConfirmedAt, string? Note,
    long? FromPlaceId = null, string FromPlace = "", long? ToPlaceId = null, string ToPlace = "");
public sealed record PlaceRow(long Id, string Code, string Name, string? Address, string Kind, bool Active);
public sealed record DocLineRow(string Material, string MaterialName, string Unit, decimal Qty, decimal? Suggested, bool Entered);
public sealed record CountRow(long Id, string DocNo, string Status, string CustomerCode, string? Reason, DateTime CreatedAt);
public sealed record CountLineRow(string Material, string MaterialName, decimal Counted, decimal? Book, decimal? Diff);
public sealed record TransferRow(long Id, string DocNo, string FromCustomer, string ToCustomer, string Status, string? Reason, DateTime CreatedAt);
public sealed record TransferLineRow(string From, string To, decimal Qty);
public sealed record AuditRow(DateTime At, string Entity, long? EntityId, string? Before, string? After, string? Reason, string? Action = null, string? Ref = null)
{
    public string Group => AuditLog.Group(Entity);
    public string ActionText => Action ?? (Entity.EndsWith("_draft") ? "Hủy phiếu nháp" : Entity == "stock_count" ? "Chốt kiểm kê" : Entity == "material_transfer" ? "Xác nhận" : "");
    public string RefText => Ref ?? (EntityId?.ToString() ?? "");
}

/// <summary>Lọc nhật ký: khoảng ngày (tính hết ngày Đến), nhóm, từ khóa (tìm trong đối tượng, thao tác, trước/sau, lý do).</summary>
public sealed record AuditFilter(DateTime? From = null, DateTime? To = null, string? Entity = null, string? Keyword = null, int Limit = 100000);
public sealed record CarryRow(string OrderNo, string Material, string Scope, decimal Opening, decimal Issued, decimal Demand, decimal Closing, string DocNo, DateTime At);
public sealed record ReservationRow(long Id, string OrderNo, string Material, decimal Qty);

/// <summary>Truy vấn danh sách cho giao diện (chỉ đọc).</summary>
public sealed class Queries(SqliteConnection c)
{
    static DateTime? OptDate(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : Db.Date(r.GetString(i));
    static string? OptStr(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    static long? OptLong(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    public List<CustomerRow> Customers(bool activeOnly = false) =>
        Db.Query(c, "SELECT id,code,name,active FROM customer WHERE ($a=0 OR active=1) ORDER BY code",
            r => new CustomerRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1), null, ("$a", activeOnly ? 1 : 0));

    public List<MaterialRow> Materials(long? customerId = null, bool activeOnly = false) =>
        Db.Query(c, @"SELECT m.id,m.code,m.name,m.unit,m.customer_id,COALESCE(k.code,''),m.active,m.note FROM material m LEFT JOIN customer k ON k.id=m.customer_id
                      WHERE ($k IS NULL OR m.customer_id=$k) AND ($a=0 OR m.active=1) ORDER BY m.code",
            r => new MaterialRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), OptLong(r, 4), r.GetString(5), r.GetInt32(6) == 1, OptStr(r, 7)),
            null, ("$k", customerId), ("$a", activeOnly ? 1 : 0));

    public List<SizeGridRow> Grids() =>
        Db.Query(c, @"SELECT g.id,g.code,g.name,g.active,
                        COALESCE((SELECT group_concat(size_code,', ') FROM (SELECT size_code FROM size_grid_item WHERE grid_id=g.id ORDER BY id)),''),
                        (SELECT COUNT(*) FROM product WHERE default_grid_id=g.id)+(SELECT COUNT(*) FROM variant WHERE grid_id=g.id)
                      FROM size_grid g ORDER BY g.code",
            r => new SizeGridRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1, r.GetString(4), r.GetInt64(5) > 0));

    public List<ProductRow> Products(long? customerId = null, bool activeOnly = false) =>
        Db.Query(c, @"SELECT p.id,p.code,p.name,p.customer_id,k.code,p.default_grid_id,g.code,p.active,(SELECT COUNT(*) FROM variant WHERE product_id=p.id)
                      FROM product p JOIN customer k ON k.id=p.customer_id JOIN size_grid g ON g.id=p.default_grid_id
                      WHERE ($k IS NULL OR p.customer_id=$k) AND ($a=0 OR p.active=1) ORDER BY k.code,p.code",
            r => new ProductRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetString(4), r.GetInt64(5), r.GetString(6), r.GetInt32(7) == 1, r.GetInt32(8)),
            null, ("$k", customerId), ("$a", activeOnly ? 1 : 0));

    public List<VariantRow> Variants(long productId) =>
        Db.Query(c, "SELECT id,code,name,COALESCE(group_name,''),qty_per_set FROM variant WHERE product_id=$p ORDER BY id",
            r => new VariantRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Db.D(r.GetString(4))), null, ("$p", productId));

    public List<string> ProductSizes(long productId) =>
        Db.Query(c, "SELECT i.size_code FROM size_grid_item i JOIN product p ON p.default_grid_id=i.grid_id WHERE p.id=$p ORDER BY i.id", r => r.GetString(0), null, ("$p", productId));

    public List<BomRow> Boms(long productId) =>
        Db.Query(c, @"SELECT b.id,b.name,COALESCE(b.type,''),b.per_set,b.scope_variant_id,COALESCE(v.code,''),(SELECT COUNT(*) FROM bom_version WHERE bom_id=b.id)
                      FROM bom b LEFT JOIN variant v ON v.id=b.scope_variant_id WHERE b.product_id=$p ORDER BY b.type,b.per_set DESC,v.id",
            r => new BomRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1, OptLong(r, 4), r.GetString(5), r.GetInt32(6)), null, ("$p", productId));

    public List<BomVersionRow> BomVersions(long bomId) =>
        Db.Query(c, "SELECT id,version_no,created_at FROM bom_version WHERE bom_id=$b ORDER BY version_no DESC",
            r => new BomVersionRow(r.GetInt64(0), r.GetInt32(1), Db.Date(r.GetString(2))), null, ("$b", bomId));

    public List<BomLineRow> BomLines(long versionId) =>
        Db.Query(c, "SELECT m.code,m.name,l.size_code,l.usage,l.waste_percent FROM bom_line l JOIN material m ON m.id=l.material_id WHERE l.bom_version_id=$v ORDER BY m.code, l.size_code<>'*', l.size_code",
            r => new BomLineRow(r.GetString(0), r.GetString(1), r.GetString(2), Db.D(r.GetString(3)), Db.D(r.GetString(4))), null, ("$v", versionId));

    public List<OrderRow> Orders(long? customerId = null, string? status = null) =>
        Db.Query(c, @"SELECT o.id,o.order_no,o.customer_id,k.code,o.product_id,p.code,o.order_date,o.due_date,o.status,
                        COALESCE((SELECT group_concat(sets_qty,'|') FROM order_line WHERE order_id=o.id),'')
                      FROM ""order"" o JOIN customer k ON k.id=o.customer_id JOIN product p ON p.id=o.product_id
                      WHERE ($k IS NULL OR o.customer_id=$k) AND ($s IS NULL OR o.status=$s) ORDER BY o.id DESC",
            r => new OrderRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetInt64(4), r.GetString(5), Db.Date(r.GetString(6)), OptDate(r, 7), r.GetString(8),
                r.GetString(9).Split('|', StringSplitOptions.RemoveEmptyEntries).Sum(Db.D)), null, ("$k", customerId), ("$s", status));

    public List<DocRow> Docs(string? type = null, long? orderId = null, string? status = null) =>
        Db.Query(c, @"SELECT d.id,d.type,d.doc_no,d.status,d.order_id,COALESCE(o.order_no,''),COALESCE(k.code,''),d.scope,d.kind,d.ref_doc_id,d.created_at,d.confirmed_at,d.note,
                        d.from_place_id,COALESCE(pf.name,''),d.to_place_id,COALESCE(pt.name,'')
                      FROM stock_doc d LEFT JOIN ""order"" o ON o.id=d.order_id LEFT JOIN customer k ON k.id=d.customer_id
                      LEFT JOIN place pf ON pf.id=d.from_place_id LEFT JOIN place pt ON pt.id=d.to_place_id
                      WHERE ($t IS NULL OR d.type=$t) AND ($o IS NULL OR d.order_id=$o) AND ($s IS NULL OR d.status=$s) ORDER BY d.id DESC",
            r => new DocRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), OptLong(r, 4), r.GetString(5), r.GetString(6), OptStr(r, 7), OptStr(r, 8), OptLong(r, 9),
                Db.Date(r.GetString(10)), OptDate(r, 11), OptStr(r, 12), OptLong(r, 13), r.GetString(14), OptLong(r, 15), r.GetString(16)), null, ("$t", type), ("$o", orderId), ("$s", status));

    /// <summary>Nơi xuất / nơi nhận. forFrom: lấy nơi dùng làm "nơi xuất" (XUAT, CA_HAI); forFrom=false: "nơi nhận" (NHAN, CA_HAI).</summary>
    public List<PlaceRow> Places(bool? forFrom = null, bool activeOnly = false) =>
        Db.Query(c, @"SELECT id,code,name,address,kind,active FROM place
                      WHERE ($a=0 OR active=1) AND ($f IS NULL OR kind='CA_HAI' OR kind=CASE WHEN $f=1 THEN 'XUAT' ELSE 'NHAN' END) ORDER BY code",
            r => new PlaceRow(r.GetInt64(0), r.GetString(1), r.GetString(2), OptStr(r, 3), r.GetString(4), r.GetInt32(5) == 1),
            null, ("$a", activeOnly ? 1 : 0), ("$f", forFrom is null ? null : forFrom.Value ? 1 : 0));

    public List<DocLineRow> DocLines(long docId) =>
        Db.Query(c, "SELECT m.code,m.name,m.unit,l.qty,l.qty_suggested,l.entered FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE l.doc_id=$d ORDER BY l.id",
            r => new DocLineRow(r.GetString(0), r.GetString(1), r.GetString(2), Db.D(r.GetString(3)), r.IsDBNull(4) ? null : Db.D(r.GetString(4)), r.GetInt32(5) == 1), null, ("$d", docId));

    public List<CountRow> Counts() =>
        Db.Query(c, "SELECT s.id,s.doc_no,s.status,COALESCE(k.code,''),s.reason,s.created_at FROM stock_count s LEFT JOIN customer k ON k.id=s.customer_id ORDER BY s.id DESC",
            r => new CountRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), OptStr(r, 4), Db.Date(r.GetString(5))));

    public List<CountLineRow> CountLines(long countId) =>
        Db.Query(c, "SELECT m.code,m.name,l.counted,l.book_qty,l.diff FROM stock_count_line l JOIN material m ON m.id=l.material_id WHERE l.count_id=$c ORDER BY m.code",
            r => new CountLineRow(r.GetString(0), r.GetString(1), Db.D(r.GetString(2)), r.IsDBNull(3) ? null : Db.D(r.GetString(3)), r.IsDBNull(4) ? null : Db.D(r.GetString(4))), null, ("$c", countId));

    public List<TransferRow> Transfers() =>
        Db.Query(c, @"SELECT t.id,t.doc_no,a.code,b.code,t.status,t.reason,t.created_at FROM material_transfer t
                      JOIN customer a ON a.id=t.from_customer_id JOIN customer b ON b.id=t.to_customer_id ORDER BY t.id DESC",
            r => new TransferRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), OptStr(r, 5), Db.Date(r.GetString(6))));

    public List<TransferLineRow> TransferLines(long transferId) =>
        Db.Query(c, "SELECT a.code,b.code,l.qty FROM material_transfer_line l JOIN material a ON a.id=l.from_material_id JOIN material b ON b.id=l.to_material_id WHERE l.transfer_id=$t ORDER BY l.id",
            r => new TransferLineRow(r.GetString(0), r.GetString(1), Db.D(r.GetString(2))), null, ("$t", transferId));

    public List<AuditRow> Audit(AuditFilter? f = null)
    {
        f ??= new();
        var entities = f.Entity is null ? null : AuditLog.Groups.Where(g => g.Value == AuditLog.Group(f.Entity)).Select(g => g.Key).ToList();
        var kw = string.IsNullOrWhiteSpace(f.Keyword) ? null : "%" + f.Keyword.Trim() + "%";
        return Db.Query(c, @"SELECT created_at,entity,entity_id,before_json,after_json,reason,action,ref_text FROM audit_log
                             WHERE ($from IS NULL OR created_at >= $from) AND ($to IS NULL OR created_at < $to)
                               AND ($kw IS NULL OR COALESCE(ref_text,'')||' '||COALESCE(action,'')||' '||COALESCE(before_json,'')||' '||COALESCE(after_json,'')||' '||COALESCE(reason,'') LIKE $kw)
                             ORDER BY id DESC LIMIT $n",
                r => new AuditRow(Db.Date(r.GetString(0)), r.GetString(1), OptLong(r, 2), OptStr(r, 3), OptStr(r, 4), OptStr(r, 5), OptStr(r, 6), OptStr(r, 7)), null,
                ("$from", f.From is DateTime a ? Db.Date(a.Date) : null), ("$to", f.To is DateTime b ? Db.Date(b.Date.AddDays(1)) : null), ("$kw", kw), ("$n", f.Limit))
            .Where(a => entities is null || entities.Contains(a.Entity)).ToList();
    }

    public List<CarryRow> Carry(long? orderId = null) =>
        Db.Query(c, @"SELECT o.order_no,m.code,l.scope,l.opening,l.issued,l.demand,l.closing,COALESCE(d.doc_no,''),l.created_at FROM carry_ledger l
                      JOIN ""order"" o ON o.id=l.order_id JOIN material m ON m.id=l.material_id LEFT JOIN stock_doc d ON d.id=l.doc_id
                      WHERE ($o IS NULL OR l.order_id=$o) ORDER BY l.id DESC",
            r => new CarryRow(r.GetString(0), r.GetString(1), r.GetString(2), Db.D(r.GetString(3)), Db.D(r.GetString(4)), Db.D(r.GetString(5)), Db.D(r.GetString(6)), r.GetString(7), Db.Date(r.GetString(8))),
            null, ("$o", orderId));

    public List<ReservationRow> Reservations(long? orderId = null) =>
        Db.Query(c, @"SELECT r.id,o.order_no,m.code,r.qty FROM reservation r JOIN ""order"" o ON o.id=r.order_id JOIN material m ON m.id=r.material_id
                      WHERE r.active=1 AND ($o IS NULL OR r.order_id=$o) ORDER BY o.order_no,m.code",
            r => new ReservationRow(r.GetInt64(0), r.GetString(1), r.GetString(2), Db.D(r.GetString(3))), null, ("$o", orderId));

    /// <summary>Số phiếu kế tiếp theo tiền tố, ví dụ NK-0001 (xét phiếu kho, kiểm kê, chuyển và đơn hàng).</summary>
    public string NextNo(string prefix)
    {
        var like = prefix + "-%";
        var nums = Db.Query(c, @"SELECT doc_no FROM stock_doc WHERE doc_no LIKE $l UNION ALL SELECT doc_no FROM stock_count WHERE doc_no LIKE $l
                                 UNION ALL SELECT doc_no FROM material_transfer WHERE doc_no LIKE $l UNION ALL SELECT order_no FROM ""order"" WHERE order_no LIKE $l",
            r => r.GetString(0), null, ("$l", like));
        var max = nums.Select(n => n[(prefix.Length + 1)..]).Select(t => new string(t.TakeWhile(char.IsDigit).ToArray()))
                      .Select(t => int.TryParse(t, out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}-{max + 1:0000}";
    }
}
