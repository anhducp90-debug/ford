using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public sealed class CatalogRepository(SqliteConnection c)
{
    public long AddCustomer(string code, string name) =>
        Db.Insert(c, "INSERT INTO customer(code,name) VALUES($c,$n)", null, ("$c", code), ("$n", name));

    public long AddMaterial(string code, string name, string unit, string? note = null) =>
        Db.Insert(c, "INSERT INTO material(code,name,unit,note) VALUES($c,$n,$u,$t)", null, ("$c", code), ("$n", name), ("$u", unit), ("$t", note));

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

    public long AddProduct(long customerId, string code, string name, long defaultGridId) =>
        Db.Insert(c, "INSERT INTO product(customer_id,code,name,default_grid_id) VALUES($c,$k,$n,$g)", null,
            ("$c", customerId), ("$k", code), ("$n", name), ("$g", defaultGridId));

    public long AddVariant(long productId, string code, string name, string group, decimal qtyPerSet, long gridId)
    {
        if (qtyPerSet <= 0) throw new BusinessException("Số lượng trong bộ phải lớn hơn 0.");
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
