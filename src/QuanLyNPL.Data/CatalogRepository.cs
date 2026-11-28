using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public sealed class CatalogRepository(SqliteConnection c)
{
    public long AddCustomer(string code, string name)
    {
        var id = Db.Insert(c, "INSERT INTO customer(code,name) VALUES($c,$n)", null, ("$c", code), ("$n", name));
        AuditLog.Write(c, "customer", "Thêm", code, null, name, entityId: id);
        return id;
    }

    /// <summary>NPL phải thuộc một khách hàng đang sử dụng. Mã NPL duy nhất toàn hệ thống.</summary>
    public long AddMaterial(string code, string name, string unit, long customerId, string? note = null)
    {
        RequireActiveCustomer(customerId);
        var id = Db.Insert(c, "INSERT INTO material(code,name,unit,customer_id,note) VALUES($c,$n,$u,$k,$t)", null,
            ("$c", code), ("$n", name), ("$u", unit), ("$k", customerId), ("$t", note));
        AuditLog.Write(c, "material", "Thêm", code, null, $"{name} ({unit}) · KH {Db.Scalar(c, "SELECT code FROM customer WHERE id=$k", null, ("$k", customerId))}", note, id);
        return id;
    }

    /// <summary>Không xóa dữ liệu đã phát sinh: chỉ chuyển trạng thái ngừng sử dụng / dùng lại.</summary>
    public void SetCustomerActive(long id, bool active) => SetActive("customer", id, active);
    public void SetProductActive(long id, bool active) => SetActive("product", id, active);
    public void SetGridActive(long id, bool active) => SetActive("size_grid", id, active);
    public void SetMaterialActive(string code, bool active) => SetActive("material", MaterialId(code), active);

    void SetActive(string table, long id, bool active)
    {
        if (Db.Exec(c, $"UPDATE {table} SET active=$a WHERE id=$i", null, ("$a", active ? 1 : 0), ("$i", id)) == 0)
            throw new BusinessException("Không tìm thấy đối tượng.");
        AuditLog.Write(c, table, active ? "Dùng lại" : "Ngừng sử dụng", Db.Scalar(c, $"SELECT code FROM {table} WHERE id=$i", null, ("$i", id)) as string, entityId: id);
    }

    public void UpdateMaterial(string code, string name, string unit, string? note)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit)) throw new BusinessException("Nhập đủ tên và đơn vị tính.");
        var used = Convert.ToInt32(Db.Scalar(c, "SELECT (SELECT COUNT(*) FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE m.code=$c)", null, ("$c", code)));
        var oldUnit = (string?)Db.Scalar(c, "SELECT unit FROM material WHERE code=$c", null, ("$c", code)) ?? throw new BusinessException($"Mã NPL không tồn tại: {code}");
        if (used > 0 && oldUnit != unit) throw new BusinessException("NPL đã phát sinh giao dịch, không đổi đơn vị tính.");
        var old = Db.Query(c, "SELECT name,unit,COALESCE(note,'') FROM material WHERE code=$c", r => $"{r.GetString(0)} ({r.GetString(1)}) {r.GetString(2)}".Trim(), null, ("$c", code)).Single();
        Db.Exec(c, "UPDATE material SET name=$n, unit=$u, note=$t WHERE code=$c", null, ("$n", name), ("$u", unit), ("$t", note), ("$c", code));
        AuditLog.Write(c, "material", "Sửa", code, old, $"{name} ({unit}) {note}".Trim());
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
        AuditLog.Write(c, "material", "Gán khách hàng", code, current is long cur ? Db.Scalar(c, "SELECT code FROM customer WHERE id=$k", null, ("$k", cur)) as string : "(chưa gán)",
            Db.Scalar(c, "SELECT code FROM customer WHERE id=$k", null, ("$k", customerId)) as string);
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
        var list = items.ToList();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) throw new BusinessException("Nhập mã và tên lưới size.");
        if (list.Count == 0) throw new BusinessException("Lưới size chưa có size nào.");
        if (list.Any(i => string.IsNullOrWhiteSpace(i.size) || i.size.Trim() == "*")) throw new BusinessException("Mã size không hợp lệ.");
        if (list.GroupBy(i => i.size).Any(g => g.Count() > 1)) throw new BusinessException("Trùng mã size trong lưới.");
        items = list;
        using var tx = c.BeginTransaction();
        var id = Db.Insert(c, "INSERT INTO size_grid(code,name) VALUES($c,$n)", tx, ("$c", code), ("$n", name));
        foreach (var (s, voc, cao) in items)
            Db.Exec(c, "INSERT INTO size_grid_item(grid_id,size_code,voc,chieu_cao) VALUES($g,$s,$v,$h)", tx, ("$g", id), ("$s", s), ("$v", voc), ("$h", cao));
        AuditLog.Write(c, "size_grid", "Thêm", code, null, $"{name}: {string.Join(", ", list.Select(i => i.size))}", entityId: id, tx: tx);
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
        AuditLog.Write(c, "size_grid", "Thêm size", Db.Scalar(c, "SELECT code FROM size_grid WHERE id=$g", null, ("$g", gridId)) as string, null, size, entityId: gridId);
    }

    public long CopyGrid(long gridId, string newCode, string newName)
    {
        var items = Db.Query(c, "SELECT size_code,voc,chieu_cao FROM size_grid_item WHERE grid_id=$g",
            r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)), null, ("$g", gridId));
        return AddGrid(newCode, newName, items);
    }

    public List<string> GridSizes(long gridId) =>
        Db.Query(c, "SELECT size_code FROM size_grid_item WHERE grid_id=$g ORDER BY id", r => r.GetString(0), null, ("$g", gridId));

    /// <summary>Khai báo mã hàng: chọn khách hàng sở hữu và lưới size (dùng cho đơn hàng, mọi chủng loại và BOM).</summary>
    public long AddProduct(long customerId, string code, string name, long defaultGridId)
    {
        RequireActiveCustomer(customerId);
        var id = Db.Insert(c, "INSERT INTO product(customer_id,code,name,default_grid_id) VALUES($c,$k,$n,$g)", null,
            ("$c", customerId), ("$k", code), ("$n", name), ("$g", defaultGridId));
        AuditLog.Write(c, "product", "Thêm", code, null,
            $"{name} · KH {Db.Scalar(c, "SELECT code FROM customer WHERE id=$k", null, ("$k", customerId))} · lưới {Db.Scalar(c, "SELECT code FROM size_grid WHERE id=$g", null, ("$g", defaultGridId))}", entityId: id);
        return id;
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
    /// <remarks>
    /// Chủng loại dùng lưới size của mã hàng; hệ thống không quy đổi size.
    /// Định mức theo quy ước size khác phải được quy đổi ngoài hệ thống trước khi khai báo BOM.
    /// </remarks>
    public long AddVariant(long productId, string code, string name, string group, decimal qtyPerSet)
    {
        if (qtyPerSet <= 0) throw new BusinessException("Số lượng trong bộ phải lớn hơn 0.");
        var pActive = Db.Scalar(c, "SELECT active FROM product WHERE id=$p", null, ("$p", productId)) ?? throw new BusinessException("Mã hàng không tồn tại.");
        if (Convert.ToInt32(pActive) != 1) throw new BusinessException("Mã hàng đã ngừng sử dụng.");
        var grid = Convert.ToInt64(Db.Scalar(c, "SELECT default_grid_id FROM product WHERE id=$p", null, ("$p", productId)));
        var id = Db.Insert(c, "INSERT INTO variant(product_id,code,name,group_name,qty_per_set,grid_id) VALUES($p,$k,$n,$g,$q,$gr)", null,
            ("$p", productId), ("$k", code), ("$n", name), ("$g", group), ("$q", Db.D(qtyPerSet)), ("$gr", grid));
        AuditLog.Write(c, "variant", "Thêm", $"{Db.Scalar(c, "SELECT code FROM product WHERE id=$p", null, ("$p", productId))} / {code}", null, $"{name} · {group} · SL/bộ {AuditLog.N(qtyPerSet)}", entityId: id);
        return id;
    }


    // ---------------- Sửa danh mục ----------------

    public void UpdateCustomer(long id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Chưa nhập tên khách hàng.");
        var old = Db.Scalar(c, "SELECT name FROM customer WHERE id=$i", null, ("$i", id)) as string ?? throw new BusinessException("Khách hàng không tồn tại.");
        Db.Exec(c, "UPDATE customer SET name=$n WHERE id=$i", null, ("$n", name.Trim()), ("$i", id));
        AuditLog.Write(c, "customer", "Sửa", Db.Scalar(c, "SELECT code FROM customer WHERE id=$i", null, ("$i", id)) as string, old, name.Trim(), entityId: id);
    }

    public void UpdateProduct(long id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Chưa nhập tên mã hàng.");
        var old = Db.Scalar(c, "SELECT name FROM product WHERE id=$i", null, ("$i", id)) as string ?? throw new BusinessException("Mã hàng không tồn tại.");
        Db.Exec(c, "UPDATE product SET name=$n WHERE id=$i", null, ("$n", name.Trim()), ("$i", id));
        AuditLog.Write(c, "product", "Sửa", Db.Scalar(c, "SELECT code FROM product WHERE id=$i", null, ("$i", id)) as string, old, name.Trim(), entityId: id);
    }

    /// <summary>Sửa chủng loại. SL/bộ chỉ đổi được khi mã hàng chưa có đơn (đơn đã lập giữ cấu hình đã chụp).</summary>
    public void UpdateVariant(long id, string name, string group, decimal qtyPerSet)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Chưa nhập tên chủng loại.");
        if (qtyPerSet <= 0) throw new BusinessException("Số lượng trong bộ phải lớn hơn 0.");
        var old = Db.Query(c, "SELECT v.code,v.name,COALESCE(v.group_name,''),v.qty_per_set,v.product_id,p.code FROM variant v JOIN product p ON p.id=v.product_id WHERE v.id=$i",
            r => (code: r.GetString(0), name: r.GetString(1), group: r.GetString(2), qty: Db.D(r.GetString(3)), prod: r.GetInt64(4), pcode: r.GetString(5)), null, ("$i", id)).SingleOrDefault();
        if (old == default) throw new BusinessException("Chủng loại không tồn tại.");
        if (old.qty != qtyPerSet && Convert.ToInt32(Db.Scalar(c, "SELECT COUNT(*) FROM \"order\" WHERE product_id=$p", null, ("$p", old.prod))) > 0)
            throw new BusinessException("Mã hàng đã có đơn hàng: không đổi SL/bộ (khai báo chủng loại mới nếu cấu thành bộ thay đổi).");
        Db.Exec(c, "UPDATE variant SET name=$n, group_name=$g, qty_per_set=$q WHERE id=$i", null, ("$n", name.Trim()), ("$g", group), ("$q", Db.D(qtyPerSet)), ("$i", id));
        AuditLog.Write(c, "variant", "Sửa", $"{old.pcode} / {old.code}", $"{old.name} · {old.group} · SL/bộ {AuditLog.N(old.qty)}", $"{name.Trim()} · {group} · SL/bộ {AuditLog.N(qtyPerSet)}", entityId: id);
    }

    // ---------------- Quy đổi đơn vị nhập ----------------

    /// <summary>
    /// Đơn vị nhập phụ của NPL (vd. vải nhập theo kg, tồn và cấp theo m) và hệ số: 1 đơn vị nhập = hệ số × đơn vị tồn.
    /// altUnit rỗng = bỏ quy đổi. Hệ số mặc định, sửa được theo từng lô khi nhập.
    /// </summary>
    public void SetMaterialConversion(string code, string? altUnit, decimal? factor)
    {
        var unit = Db.Scalar(c, "SELECT unit FROM material WHERE code=$c", null, ("$c", code)) as string ?? throw new BusinessException($"Mã NPL không tồn tại: {code}");
        altUnit = string.IsNullOrWhiteSpace(altUnit) ? null : altUnit.Trim();
        if (altUnit is not null)
        {
            if (string.Equals(altUnit, unit, StringComparison.OrdinalIgnoreCase)) throw new BusinessException("Đơn vị nhập phụ phải khác đơn vị tồn.");
            if (factor is not > 0) throw new BusinessException($"Hệ số quy đổi phải lớn hơn 0 ({unit}/{altUnit}).");
        }
        var old = Db.Query(c, "SELECT alt_unit,alt_factor FROM material WHERE code=$c", r => r.IsDBNull(0) ? "" : $"1 {r.GetString(0)} = {(r.IsDBNull(1) ? "" : AuditLog.N(Db.D(r.GetString(1))))} {unit}", null, ("$c", code)).Single();
        Db.Exec(c, "UPDATE material SET alt_unit=$a, alt_factor=$f WHERE code=$c", null, ("$a", altUnit), ("$f", altUnit is null ? null : Db.D(factor!.Value)), ("$c", code));
        AuditLog.Write(c, "material", "Quy đổi đơn vị", code, old, altUnit is null ? "(bỏ quy đổi)" : $"1 {altUnit} = {AuditLog.N(factor!.Value)} {unit}");
    }

    /// <summary>Số mét vải trong 1 kg từ định lượng (g/m²) và khổ (cm): m/kg = 1000 / (GSM × khổ m). Làm tròn 4 số lẻ.</summary>
    public static decimal MetersPerKg(decimal gsm, decimal widthCm)
    {
        if (gsm <= 0 || widthCm <= 0) throw new BusinessException("Định lượng và khổ vải phải lớn hơn 0.");
        return Math.Round(1000m / (gsm * widthCm / 100m), 4);
    }
}
