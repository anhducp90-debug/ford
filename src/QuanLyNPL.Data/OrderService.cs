using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;

namespace QuanLyNPL.Data;

public sealed record SnapshotVariant(string Code, decimal QtyPerSet);
public sealed record SnapshotMap(string Variant, string DefaultSize, string VariantSize);
public sealed record SnapshotBomLine(string Material, string Size, decimal Usage, decimal Waste, string? Variant);
public sealed record SnapshotBom(string Id, string Name, string? ScopeVariant, long? VersionId, int? VersionNo, List<SnapshotBomLine> Lines, bool PerSet = false);
public sealed record OrderSnapshot(List<SnapshotVariant> Variants, List<SnapshotMap> Maps, List<SnapshotBom> Boms);

public sealed class OrderService(SqliteConnection c)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Lập đơn: chọn mã hàng, khách hàng tự lấy theo mã hàng.</summary>
    public long CreateOrder(long productId, string orderNo, DateTime orderDate,
        IReadOnlyList<OrderLine> lines, DateTime? dueDate = null)
    {
        var prod = Db.Query(c, "SELECT p.customer_id, p.default_grid_id, p.active, k.active FROM product p JOIN customer k ON k.id=p.customer_id WHERE p.id=$p",
            r => (cust: r.GetInt64(0), grid: r.GetInt64(1), pActive: r.GetInt32(2) == 1, kActive: r.GetInt32(3) == 1), null, ("$p", productId)).SingleOrDefault();
        if (prod == default) throw new BusinessException("Mã hàng không tồn tại.");
        if (!prod.pActive) throw new BusinessException("Mã hàng đã ngừng sử dụng.");
        if (!prod.kActive) throw new BusinessException("Khách hàng của mã hàng đã ngừng sử dụng.");
        var customerId = prod.cust;
        if (lines.Count == 0) throw new BusinessException("Đơn hàng chưa có dòng nào.");
        ValidateLines(prod.grid, lines);

        var snapshot = BuildSnapshot(productId, orderDate);

        using var tx = c.BeginTransaction();
        var id = Db.Insert(c, "INSERT INTO \"order\"(customer_id,product_id,order_no,order_date,due_date) VALUES($c,$p,$n,$d,$du)", tx,
            ("$c", customerId), ("$p", productId), ("$n", orderNo), ("$d", Db.Date(orderDate)), ("$du", dueDate is null ? null : Db.Date(dueDate.Value)));
        foreach (var l in lines)
            Db.Exec(c, "INSERT INTO order_line(order_id,default_size_code,sets_qty) VALUES($o,$s,$q)", tx,
                ("$o", id), ("$s", l.DefaultSize), ("$q", Db.D(l.Sets)));
        Db.Exec(c, "INSERT INTO order_snapshot(order_id,created_at,config_json) VALUES($o,$t,$j)", tx,
            ("$o", id), ("$t", Db.Date(DateTime.Now)), ("$j", JsonSerializer.Serialize(snapshot, Json)));
        tx.Commit();
        return id;
    }

    public void ValidateLines(long defaultGridId, IReadOnlyList<OrderLine> lines)
    {
        if (lines.Count == 0) throw new BusinessException("Đơn hàng chưa có dòng nào.");
        var grid = new CatalogRepository(c).GridSizes(defaultGridId);
        var errs = new List<string>();
        foreach (var l in lines)
        {
            if (l.Sets < 0) errs.Add($"Số bộ âm ở size {l.DefaultSize}");
            if (!grid.Contains(l.DefaultSize)) errs.Add($"Size {l.DefaultSize} không thuộc lưới mặc định");
        }
        if (lines.GroupBy(l => l.DefaultSize).Any(g => g.Count() > 1)) errs.Add("Trùng size trong đơn hàng");
        if (errs.Count > 0) throw new BusinessException(string.Join("\n", errs));
    }

    /// <summary>Chụp cấu hình bộ, quy đổi và phiên bản BOM hiệu lực tại ngày lập đơn.</summary>
    public OrderSnapshot BuildSnapshot(long productId, DateTime orderDate)
    {
        var variants = Db.Query(c, "SELECT id,code,qty_per_set FROM variant WHERE product_id=$p ORDER BY code",
            r => (id: r.GetInt64(0), code: r.GetString(1), qty: Db.D(r.GetString(2))), null, ("$p", productId));
        var code = variants.ToDictionary(v => v.id, v => v.code);
        var maps = Db.Query(c, "SELECT m.variant_id,m.default_size_code,m.variant_size_code FROM size_map m JOIN variant v ON v.id=m.variant_id WHERE v.product_id=$p",
            r => new SnapshotMap(code[r.GetInt64(0)], r.GetString(1), r.GetString(2)), null, ("$p", productId));

        var boms = new List<SnapshotBom>();
        var bomRows = Db.Query(c, "SELECT id,name,scope_variant_id,per_set FROM bom WHERE product_id=$p ORDER BY id",
            r => (id: r.GetInt64(0), name: r.GetString(1), scope: r.IsDBNull(2) ? (long?)null : r.GetInt64(2), perSet: r.GetInt32(3) == 1), null, ("$p", productId));
        foreach (var b in bomRows)
        {
            var versions = Db.Query(c, "SELECT id,version_no,created_at FROM bom_version WHERE bom_id=$b",
                r => (id: r.GetInt64(0), no: r.GetInt32(1), at: Db.Date(r.GetString(2))), null, ("$b", b.id));
            var pick = BomVersioning.Pick(versions.Select(v => new BomVersion(b.id.ToString(), v.no, v.at)), orderDate);
            var scope = b.scope is long s ? code[s] : null;
            if (pick is null) { boms.Add(new(b.id.ToString(), b.name, scope, null, null, new(), b.perSet)); continue; }
            var vid = versions.First(v => v.no == pick.VersionNo).id;
            var lines = Db.Query(c, "SELECT m.code,l.size_code,l.usage,l.waste_percent,l.variant_id FROM bom_line l JOIN material m ON m.id=l.material_id WHERE l.bom_version_id=$v",
                r => new SnapshotBomLine(r.GetString(0), r.GetString(1), Db.D(r.GetString(2)), Db.D(r.GetString(3)),
                    r.IsDBNull(4) ? null : code[r.GetInt64(4)]), null, ("$v", vid));
            boms.Add(new(b.id.ToString(), b.name, scope, vid, pick.VersionNo, lines, b.perSet));
        }
        return new(variants.Select(v => new SnapshotVariant(v.code, v.qty)).ToList(), maps, boms);
    }

    public OrderSnapshot LoadSnapshot(long orderId) =>
        JsonSerializer.Deserialize<OrderSnapshot>((string)Db.Scalar(c, "SELECT config_json FROM order_snapshot WHERE order_id=$o ORDER BY id DESC LIMIT 1", null, ("$o", orderId))!, Json)!;

    public List<OrderLine> LoadLines(long orderId) =>
        Db.Query(c, "SELECT default_size_code,sets_qty FROM order_line WHERE order_id=$o", r => new OrderLine(r.GetString(0), Db.D(r.GetString(1))), null, ("$o", orderId));

    /// <summary>Tính nhu cầu hoàn toàn từ snapshot: đổi BOM/cấu hình về sau không ảnh hưởng đơn cũ.</summary>
    public DemandResult CalculateDemand(long orderId) => Calculate(LoadSnapshot(orderId), LoadLines(orderId));

    public static DemandResult Calculate(OrderSnapshot snap, IReadOnlyList<OrderLine> lines)
    {
        var map = new SizeMap();
        foreach (var m in snap.Maps) map.Add(m.Variant, m.DefaultSize, m.VariantSize);
        var result = DemandCalculator.Calculate(lines,
            snap.Variants.Select(v => new VariantConfig(v.Code, v.QtyPerSet)), map,
            snap.Boms.Select(b => new Bom(b.Id, b.ScopeVariant, b.PerSet, b.Name)),
            snap.Boms.SelectMany(b => b.Lines.Select(l => new BomLine(b.Id, l.Material, l.Size, l.Usage, l.Waste, l.Variant))));
        var extra = snap.Boms.Where(b => b.VersionId is null)
            .Select(b => new Issue(IssueKind.MissingBom, $"BOM '{b.Name}' chưa có phiên bản hiệu lực tại ngày lập đơn")).ToList();
        return extra.Count == 0 ? result : result with { Issues = result.Issues.Concat(extra).ToList() };
    }
}
