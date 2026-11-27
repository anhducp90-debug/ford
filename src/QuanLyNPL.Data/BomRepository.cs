using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public sealed record BomLineInput(string MaterialCode, string SizeCode, decimal Usage, decimal WastePercent = 0m, string? VariantCode = null);

public sealed class BomRepository(SqliteConnection c)
{
    public long CreateBom(long productId, string name, string? type = null, long? scopeVariantId = null) =>
        Db.Insert(c, "INSERT INTO bom(product_id,name,type,scope_variant_id) VALUES($p,$n,$t,$s)", null,
            ("$p", productId), ("$n", name), ("$t", type), ("$s", scopeVariantId));

    /// <summary>
    /// Tạo phiên bản BOM mới (bất biến). Kiểm tra toàn bộ trước, lỗi thì không ghi dòng nào
    /// và báo rõ số dòng/nguyên nhân.
    /// </summary>
    public long AddVersion(long bomId, DateTime createdAt, IReadOnlyList<BomLineInput> lines)
    {
        var (productId, scopeVariant) = Db.Query(c, "SELECT product_id, scope_variant_id FROM bom WHERE id=$b",
            r => (r.GetInt64(0), r.IsDBNull(1) ? (long?)null : r.GetInt64(1)), null, ("$b", bomId)).Single();
        var errors = new List<string>();
        var resolved = new List<(long mat, long? variant, BomLineInput l)>();
        var mats = Db.Query(c, "SELECT code,id,active FROM material", r => (r.GetString(0), r.GetInt64(1), r.GetInt32(2) == 1))
                     .ToDictionary(x => x.Item1, x => (x.Item2, x.Item3));
        var variants = Db.Query(c, "SELECT code,id,grid_id FROM variant WHERE product_id=$p", r => (r.GetString(0), r.GetInt64(1), r.GetInt64(2)), null, ("$p", productId))
                         .ToDictionary(x => x.Item1, x => (x.Item2, x.Item3));
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i]; var row = i + 1;
            if (!mats.TryGetValue(l.MaterialCode, out var m)) errors.Add($"Dòng {row}: mã NPL '{l.MaterialCode}' không tồn tại");
            else if (!m.Item2) errors.Add($"Dòng {row}: mã NPL '{l.MaterialCode}' đã ngừng sử dụng");
            if (l.Usage < 0) errors.Add($"Dòng {row}: định mức âm");
            if (l.WastePercent < 0) errors.Add($"Dòng {row}: hao hụt âm");
            long? vid = null;
            if (l.VariantCode is not null)
            {
                if (!variants.TryGetValue(l.VariantCode, out var v)) errors.Add($"Dòng {row}: chủng loại '{l.VariantCode}' không thuộc mã hàng");
                else vid = v.Item1;
            }
            var effVariant = vid ?? scopeVariant;
            if (l.SizeCode != "*")
            {
                var grids = effVariant is long ev
                    ? variants.Values.Where(v => v.Item1 == ev).Select(v => v.Item2).ToList()
                    : variants.Values.Select(v => v.Item2).Distinct().ToList();
                if (grids.Count == 0 || !grids.Any(g => Db.Query(c, "SELECT 1 FROM size_grid_item WHERE grid_id=$g AND size_code=$s", r => 1, null, ("$g", g), ("$s", l.SizeCode)).Count > 0))
                    errors.Add($"Dòng {row}: size '{l.SizeCode}' không hợp lệ");
            }
            if (mats.ContainsKey(l.MaterialCode)) resolved.Add((mats[l.MaterialCode].Item1, vid, l));
        }
        if (errors.Count > 0) throw new BusinessException(string.Join("\n", errors));

        using var tx = c.BeginTransaction();
        var next = Convert.ToInt32(Db.Scalar(c, "SELECT COALESCE(MAX(version_no),0)+1 FROM bom_version WHERE bom_id=$b", tx, ("$b", bomId)));
        var vidNew = Db.Insert(c, "INSERT INTO bom_version(bom_id,version_no,created_at) VALUES($b,$n,$d)", tx,
            ("$b", bomId), ("$n", next), ("$d", Db.Date(createdAt)));
        foreach (var (mat, variant, l) in resolved)
            Db.Exec(c, "INSERT INTO bom_line(bom_version_id,material_id,size_code,usage,waste_percent,variant_id) VALUES($v,$m,$s,$u,$w,$vr)", tx,
                ("$v", vidNew), ("$m", mat), ("$s", l.SizeCode), ("$u", Db.D(l.Usage)), ("$w", Db.D(l.WastePercent)), ("$vr", variant));
        tx.Commit();
        return vidNew;
    }
}
