using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

/// <summary>Loại BOM khi khai báo: chọn chủng loại rồi tích một loại.</summary>
public enum BomType { ToCat, ChungLoai, DongGoi }

public static class BomTypes
{
    public static string Code(BomType t) => t switch { BomType.ToCat => "TO_CAT", BomType.ChungLoai => "CHUNG_LOAI", _ => "DONG_GOI" };
    public static string Label(BomType t) => t switch { BomType.ToCat => "Tổ cắt", BomType.ChungLoai => "Chủng loại", _ => "Đóng gói" };
}

/// <summary>Dòng định mức của một BOM (BOM đã gắn chủng loại nên dòng không cần mã chủng loại). Size "*" là định mức nền.</summary>
public sealed record BomLineInput(string MaterialCode, string SizeCode, decimal Usage, decimal WastePercent = 0m);

public sealed record BomInfo(long Id, long VariantId, string VariantCode, long ProductId, long CustomerId, string Type, string Name);

public sealed class BomRepository(SqliteConnection c)
{
    /// <summary>
    /// Khai báo BOM: chọn chủng loại (tự kế thừa mã hàng và khách hàng) và một loại BOM.
    /// Mỗi chủng loại có tối đa một BOM mỗi loại; định mức thay đổi thì thêm phiên bản.
    /// </summary>
    public long CreateBom(long variantId, BomType type)
    {
        var v = Db.Query(c, "SELECT v.product_id, v.code, p.active FROM variant v JOIN product p ON p.id=v.product_id WHERE v.id=$v",
            r => (prod: r.GetInt64(0), code: r.GetString(1), active: r.GetInt32(2) == 1), null, ("$v", variantId)).SingleOrDefault();
        if (v == default) throw new BusinessException("Chủng loại không tồn tại.");
        if (!v.active) throw new BusinessException("Mã hàng đã ngừng sử dụng.");
        if (Find(variantId, type) is not null)
            throw new BusinessException($"Chủng loại {v.code} đã có BOM {BomTypes.Label(type)}; hãy thêm phiên bản mới.");
        return Db.Insert(c, "INSERT INTO bom(product_id,name,type,scope_variant_id) VALUES($p,$n,$t,$s)", null,
            ("$p", v.prod), ("$n", $"{v.code} – {BomTypes.Label(type)}"), ("$t", BomTypes.Code(type)), ("$s", variantId));
    }

    public long? Find(long variantId, BomType type)
    {
        var v = Db.Scalar(c, "SELECT id FROM bom WHERE scope_variant_id=$v AND type=$t", null, ("$v", variantId), ("$t", BomTypes.Code(type)));
        return v is null ? null : Convert.ToInt64(v);
    }

    public BomInfo Info(long bomId) =>
        Db.Query(c, @"SELECT b.id, b.scope_variant_id, v.code, b.product_id, p.customer_id, COALESCE(b.type,''), b.name
                      FROM bom b JOIN product p ON p.id=b.product_id LEFT JOIN variant v ON v.id=b.scope_variant_id WHERE b.id=$b",
            r => new BomInfo(r.GetInt64(0), r.IsDBNull(1) ? 0 : r.GetInt64(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetString(5), r.GetString(6)),
            null, ("$b", bomId)).SingleOrDefault() ?? throw new BusinessException("BOM không tồn tại.");

    /// <summary>Size hợp lệ cho BOM = size thuộc lưới của chủng loại (dùng cho danh sách chọn và file mẫu Excel).</summary>
    public List<string> Sizes(long bomId)
    {
        var info = Info(bomId);
        return Db.Query(c, "SELECT i.size_code FROM size_grid_item i JOIN variant v ON v.grid_id=i.grid_id WHERE v.id=$v ORDER BY i.id",
            r => r.GetString(0), null, ("$v", info.VariantId));
    }

    /// <summary>
    /// Tạo phiên bản BOM mới (bất biến). Kiểm tra toàn bộ trước, lỗi thì không ghi dòng nào và báo rõ số dòng/nguyên nhân.
    /// NPL phải thuộc khách hàng của mã hàng; size phải thuộc lưới của chủng loại.
    /// </summary>
    public long AddVersion(long bomId, DateTime createdAt, IReadOnlyList<BomLineInput> lines, IReadOnlyList<int>? rowLabels = null)
    {
        var info = Info(bomId);
        var sizes = info.VariantId == 0
            ? Db.Query(c, "SELECT DISTINCT i.size_code FROM size_grid_item i JOIN variant v ON v.grid_id=i.grid_id WHERE v.product_id=$p", r => r.GetString(0), null, ("$p", info.ProductId))
            : Sizes(bomId);
        var mats = Db.Query(c, "SELECT code,id,active,customer_id FROM material",
                r => (code: r.GetString(0), id: r.GetInt64(1), active: r.GetInt32(2) == 1, cust: r.IsDBNull(3) ? (long?)null : r.GetInt64(3)))
            .ToDictionary(x => x.code);
        var errors = new List<string>();
        if (lines.Count == 0) errors.Add("BOM chưa có dòng định mức nào.");
        var seen = new HashSet<(string, string)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i]; var row = rowLabels?[i] ?? i + 1;
            if (!mats.TryGetValue(l.MaterialCode, out var m)) errors.Add($"Dòng {row}: mã NPL '{l.MaterialCode}' không tồn tại");
            else if (!m.active) errors.Add($"Dòng {row}: mã NPL '{l.MaterialCode}' đã ngừng sử dụng");
            else if (m.cust is null) errors.Add($"Dòng {row}: mã NPL '{l.MaterialCode}' chưa gán khách hàng");
            else if (m.cust != info.CustomerId) errors.Add($"Dòng {row}: mã NPL '{l.MaterialCode}' thuộc khách hàng khác với mã hàng");
            if (l.Usage < 0) errors.Add($"Dòng {row}: định mức âm");
            if (l.WastePercent < 0) errors.Add($"Dòng {row}: hao hụt âm");
            if (l.SizeCode != "*" && !sizes.Contains(l.SizeCode)) errors.Add($"Dòng {row}: size '{l.SizeCode}' không thuộc lưới của chủng loại");
            if (!seen.Add((l.MaterialCode, l.SizeCode))) errors.Add($"Dòng {row}: trùng (NPL, size) với dòng trước");
        }
        if (errors.Count > 0) throw new BusinessException(string.Join("\n", errors));

        using var tx = c.BeginTransaction();
        var next = Convert.ToInt32(Db.Scalar(c, "SELECT COALESCE(MAX(version_no),0)+1 FROM bom_version WHERE bom_id=$b", tx, ("$b", bomId)));
        var vidNew = Db.Insert(c, "INSERT INTO bom_version(bom_id,version_no,created_at) VALUES($b,$n,$d)", tx,
            ("$b", bomId), ("$n", next), ("$d", Db.Date(createdAt)));
        foreach (var l in lines)
            Db.Exec(c, "INSERT INTO bom_line(bom_version_id,material_id,size_code,usage,waste_percent) VALUES($v,$m,$s,$u,$w)", tx,
                ("$v", vidNew), ("$m", mats[l.MaterialCode].id), ("$s", l.SizeCode), ("$u", Db.D(l.Usage)), ("$w", Db.D(l.WastePercent)));
        tx.Commit();
        return vidNew;
    }
}
