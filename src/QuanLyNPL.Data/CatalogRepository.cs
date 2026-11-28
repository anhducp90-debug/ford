using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public sealed class CatalogRepository(SqliteConnection c)
{
    public long AddCustomer(string code, string name) =>
        Db.Insert(c, "INSERT INTO customer(code,name) VALUES($c,$n)", null, ("$c", code), ("$n", name));

    /// <summary>NPL phải thuộc một khách hàng đang sử dụng. Mã NPL duy nhất toàn hệ thống.</summary>
    public long AddMaterial(string code, string name, string unit, long customerId, string? note = null)
    {
        RequireActiveCustomer(customerId);
        return Db.Insert(c, "INSERT INTO material(code,name,unit,customer_id,note) VALUES($c,$n,$u,$k,$t)", null,
            ("$c", code), ("$n", name), ("$u", unit), ("$k", customerId), ("$t", note));
    }

    public long? MaterialCustomerId(string code)
    {
        var v = Db.Scalar(c, "SELECT customer_id FROM material WHERE code=$c", null, ("$c", code));
        return v is null || v is DBNull ? null : Convert.ToInt64(v);
    }

    /// <summary>Gán/đổi khách hàng cho NPL. Đã phát sinh BOM hoặc giao dịch kho thì không đổi (chỉ gán được khi chưa có khách hàng).</summary>
    public void AssignMaterialCustomer(string code, long customerId)
    {
        var current = MaterialCustomerId(code);
        if (current == customerId) return;
        if (current is not null)
        {
            var used = Convert.ToInt32(Db.Scalar(c,
                "SELECT (SELECT COUNT(*) FROM bom_line l JOIN material m ON m.id=l.material_id WHERE m.code=$c)+(SELECT COUNT(*) FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE m.code=$c)", null, ("$c", code)));
            if (used > 0) throw new BusinessException($"NPL {code} đã được sử dụng, không đổi khách hàng.");
        }
        MaterialId(code);   // kiểm tra tồn tại
        Db.Exec(c, "UPDATE material SET customer_id=$k WHERE code=$c", null, ("$k", customerId), ("$c", code));
    }

    /// <summary>NPL cũ (trước khi có khai báo theo khách hàng) chưa gán khách hàng.</summary>
    public List<string> UnassignedMaterials() =>
        Db.Query(c, "SELECT code FROM material WHERE customer_id IS NULL ORDER BY code", r => r.GetString(0));

    /// <summary>
    /// Khách hàng của một phiếu: lấy theo tham số nếu có, nếu không thì suy ra từ NPL.
    /// Mọi NPL trong phiếu phải thuộc cùng khách hàng đó.
    /// </summary>
    public long? ResolveDocCustomer(IEnumerable<string> materialCodes, long? customerId)
    {
        var codes = materialCodes.Distinct().ToList();
        if (customerId is long k) { RequireActiveCustomer(k); foreach (var m in codes) RequireMaterialOfCustomer(m, k); return k; }
        var owners = codes.Select(m => (m, owner: MaterialCustomerId(MaterialIdCheck(m)))).ToList();
        var unassigned = owners.Where(o => o.owner is null).Select(o => o.m).ToList();
        if (unassigned.Count > 0) throw new BusinessException("NPL chưa gán khách hàng: " + string.Join(", ", unassigned));
        var distinct = owners.Select(o => o.owner).Distinct().ToList();
        if (distinct.Count > 1) throw new BusinessException("Một phiếu chỉ gồm NPL của một khách hàng. Dùng phiếu chuyển NPL nếu cần dùng NPL của khách hàng khác.");
        return distinct.SingleOrDefault();
    }

    string MaterialIdCheck(string code) { MaterialId(code); return code; }

    public void RequireMaterialOfCustomer(string code, long customerId)
    {
        var owner = MaterialCustomerId(code);
        if (owner is null) throw new BusinessException($"NPL {code} chưa gán khách hàng.");
        if (owner != customerId) throw new BusinessException($"NPL {code} thuộc khách hàng khác.");
    }

    public long MaterialId(string code) =>
        Convert.ToInt64(Db.Scalar(c, "SELECT id FROM material WHERE code=$c", null, ("$c", code)) ?? throw new BusinessException($"Mã NPL không tồn tại: {code}"));

    /// <summary>Tạo lưới size cùng các size (không trùng mã size trong lưới).</summary>
    public long AddGrid(string code, string name, IEnumerable<(string size, string? voc, string? cao)> items)
    {
        using var tx = c.BeginTransaction();
        var id = Db.Insert(c, "INSERT INTO size_grid(code,name) VALUES($c,$n)", tx, ("$c", code), ("$n", name));
        foreach (var (s, voc, cao) in items)
            Db.Exec(c, "INSERT INTO size_grid_item(grid_id,size_code,voc,chieu_cao) VALUES($g,$s,$v,$h)", tx, ("$g", id), ("$s", s), ("$v", voc), ("$h", cao));
        tx.Commit();
        return id;
    }

    public bool IsGridUsed(long gridId) =>
        Convert.ToInt32(Db.Scalar(c, "SELECT (SELECT COUNT(*) FROM product WHERE default_grid_id=$g)+(SELECT COUNT(*) FROM variant WHERE grid_id=$g)", null, ("$g", gridId))) > 0;

    /// <summary>Lưới đã dùng thì không sửa cấu trúc; phải sao chép thành lưới mới.</summary>
    public void AddGridItem(long gridId, string size)
    {
        if (IsGridUsed(gridId)) throw new BusinessException("Lưới size đã được sử dụng, không sửa cấu trúc. Hãy tạo lưới mới.");
        Db.Exec(c, "INSERT INTO size_grid_item(grid_id,size_code) VALUES($g,$s)", null, ("$g", gridId), ("$s", size));
    }

    public long CopyGrid(long gridId, string newCode, string newName)
    {
        var items = Db.Query(c, "SELECT size_code,voc,chieu_cao FROM size_grid_item WHERE grid_id=$g",
            r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)), null, ("$g", gridId));
        return AddGrid(newCode, newName, items);
    }

    public List<string> GridSizes(long gridId) =>
        Db.Query(c, "SELECT size_code FROM size_grid_item WHERE grid_id=$g ORDER BY id", r => r.GetString(0), null, ("$g", gridId));

    /// <summary>Khai báo mã hàng: chọn khách hàng sở hữu và lưới size mặc định.</summary>
    public long AddProduct(long customerId, string code, string name, long defaultGridId)
    {
        RequireActiveCustomer(customerId);
        return Db.Insert(c, "INSERT INTO product(customer_id,code,name,default_grid_id) VALUES($c,$k,$n,$g)", null,
            ("$c", customerId), ("$k", code), ("$n", name), ("$g", defaultGridId));
    }

    void RequireActiveCustomer(long customerId)
    {
        var active = Db.Scalar(c, "SELECT active FROM customer WHERE id=$i", null, ("$i", customerId));
        if (active is null) throw new BusinessException("Khách hàng không tồn tại.");
        if (Convert.ToInt32(active) != 1) throw new BusinessException("Khách hàng đã ngừng sử dụng.");
    }

    /// <summary>Khách hàng của mã hàng (chủng loại, BOM, đơn hàng kế thừa khách hàng này).</summary>
    public long ProductCustomerId(long productId) =>
        Convert.ToInt64(Db.Scalar(c, "SELECT customer_id FROM product WHERE id=$p", null, ("$p", productId)) ?? throw new BusinessException("Mã hàng không tồn tại."));

    public long VariantCustomerId(long variantId) =>
        Convert.ToInt64(Db.Scalar(c, "SELECT p.customer_id FROM variant v JOIN product p ON p.id=v.product_id WHERE v.id=$v", null, ("$v", variantId)) ?? throw new BusinessException("Chủng loại không tồn tại."));

    /// <summary>Khai báo chủng loại: chọn mã hàng, khách hàng tự kế thừa theo mã hàng.</summary>
    public long AddVariant(long productId, string code, string name, string group, decimal qtyPerSet, long gridId)
    {
        if (qtyPerSet <= 0) throw new BusinessException("Số lượng trong bộ phải lớn hơn 0.");
        var pActive = Db.Scalar(c, "SELECT active FROM product WHERE id=$p", null, ("$p", productId)) ?? throw new BusinessException("Mã hàng không tồn tại.");
        if (Convert.ToInt32(pActive) != 1) throw new BusinessException("Mã hàng đã ngừng sử dụng.");
        return Db.Insert(c, "INSERT INTO variant(product_id,code,name,group_name,qty_per_set,grid_id) VALUES($p,$k,$n,$g,$q,$gr)", null,
            ("$p", productId), ("$k", code), ("$n", name), ("$g", group), ("$q", Db.D(qtyPerSet)), ("$gr", gridId));
    }

    /// <summary>Quy đổi: size mặc định phải thuộc lưới mặc định, size đích phải thuộc lưới của chủng loại.</summary>
    public void SetSizeMap(long variantId, string defaultSize, string variantSize)
    {
        var (defGrid, varGrid) = Db.Query(c,
            "SELECT p.default_grid_id, v.grid_id FROM variant v JOIN product p ON p.id=v.product_id WHERE v.id=$v",
            r => (r.GetInt64(0), r.GetInt64(1)), null, ("$v", variantId)).Single();
        if (!GridSizes(defGrid).Contains(defaultSize)) throw new BusinessException($"Size {defaultSize} không thuộc lưới mặc định.");
        if (!GridSizes(varGrid).Contains(variantSize)) throw new BusinessException($"Size {variantSize} không thuộc lưới của chủng loại.");
        Db.Exec(c, "INSERT INTO size_map(variant_id,default_size_code,variant_size_code) VALUES($v,$d,$s) " +
                   "ON CONFLICT(variant_id,default_size_code) DO UPDATE SET variant_size_code=$s", null,
            ("$v", variantId), ("$d", defaultSize), ("$s", variantSize));
    }
}
