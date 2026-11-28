namespace QuanLyNPL.Core;

public static class DemandCalculator
{
    public const string BaseSize = "*";
    public const int Decimals = 2;

    public static decimal Round(decimal v) => Math.Round(v, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>Phân rã số bộ theo size mặc định thành số lượng chủng loại/size đích (gộp cùng chủng loại + size).</summary>
    public static (List<BreakdownRow> rows, List<Issue> issues) Breakdown(
        IEnumerable<OrderLine> lines, IEnumerable<VariantConfig> variants, SizeMap map)
    {
        var issues = new List<Issue>();
        var acc = new Dictionary<(string, string), decimal>();
        var vlist = variants.ToList();
        foreach (var l in lines)
        {
            if (l.Sets < 0) { issues.Add(new(IssueKind.InvalidData, $"Số bộ âm ở size {l.DefaultSize}")); continue; }
            foreach (var v in vlist)
            {
                if (v.QtyPerSet < 0) { issues.Add(new(IssueKind.InvalidData, $"Số lượng/bộ âm ở {v.Code}")); continue; }
                var target = map.Resolve(v.Code, l.DefaultSize);
                if (target is null)
                { issues.Add(new(IssueKind.MissingSizeMap, $"Thiếu quy đổi size {l.DefaultSize} cho chủng loại {v.Code}")); continue; }
                var key = (v.Code, target);
                acc[key] = acc.GetValueOrDefault(key) + l.Sets * v.QtyPerSet;
            }
        }
        var rows = acc.Select(kv => new BreakdownRow(kv.Key.Item1, kv.Key.Item2, kv.Value))
                      .OrderBy(r => r.Variant, StringComparer.Ordinal).ThenBy(r => r.VariantSize, StringComparer.Ordinal).ToList();
        return (rows, issues);
    }

    /// <summary>Giá trị cột "chủng loại" của nguồn nhu cầu đến từ BOM theo bộ.</summary>
    public const string SetScope = "(Bộ)";

    public static DemandResult Calculate(IEnumerable<OrderLine> orderLines, IEnumerable<VariantConfig> variants,
        SizeMap map, IEnumerable<Bom> boms, IEnumerable<BomLine> bomLines)
    {
        var lines = orderLines.ToList(); var vlist = variants.ToList();
        var (rows, issues) = Breakdown(lines, vlist, map);
        var bomList = boms.ToList();
        var linesByBom = bomLines.GroupBy(l => l.BomId).ToDictionary(g => g.Key, g => g.ToList());
        var sources = new List<DemandSource>();

        // Chọn dòng định mức cho một size: dòng size cụ thể ghi đè dòng nền '*' của cùng (BOM, NPL).
        List<BomLine> Pick(IEnumerable<BomLine> ls, string size) =>
            ls.GroupBy(l => l.MaterialCode).SelectMany(g =>
            {
                var specific = g.Where(l => l.SizeCode == size).ToList();
                return specific.Count > 0 ? specific : g.Where(l => l.SizeCode == BaseSize).ToList();
            }).ToList();

        bool Add(Bom bom, string variant, string size, decimal qty, IEnumerable<BomLine> chosen)
        {
            var any = false;
            foreach (var l in chosen)
            {
                if (l.Usage < 0 || l.WastePercent < 0)
                { issues.Add(new(IssueKind.InvalidData, $"Định mức/hao hụt âm: BOM {bom.Label}, NPL {l.MaterialCode}")); continue; }
                any = true;
                sources.Add(new(bom.Id, variant, size, l.MaterialCode, qty * l.Usage * (1 + l.WastePercent / 100m)));
            }
            return any;
        }

        // 1) BOM theo bộ: số bộ × định mức theo size mặc định.
        var setCovered = new HashSet<string>();
        foreach (var bom in bomList.Where(b => b.PerSet))
        {
            var ls = linesByBom.GetValueOrDefault(bom.Id) ?? new();
            foreach (var ol in lines.Where(l => l.Sets > 0))
            {
                if (Add(bom, SetScope, ol.DefaultSize, ol.Sets, Pick(ls, ol.DefaultSize))) setCovered.Add(ol.DefaultSize);
                else issues.Add(new(IssueKind.MissingBom, $"BOM {bom.Label} chưa có định mức cho size {ol.DefaultSize}"));
            }
        }

        // Chủng loại/size đã có định mức nhờ BOM theo bộ (không báo thiếu cho các dòng này).
        var coveredBySet = new HashSet<(string, string)>();
        foreach (var ol in lines.Where(l => l.Sets > 0 && setCovered.Contains(l.DefaultSize)))
            foreach (var v in vlist)
                if (map.Resolve(v.Code, ol.DefaultSize) is string t) coveredBySet.Add((v.Code, t));

        // 2) BOM theo chủng loại (và BOM cũ áp dụng mọi chủng loại): số sản phẩm × định mức theo size quy đổi.
        foreach (var r in rows)
        {
            var any = false;
            foreach (var bom in bomList.Where(b => !b.PerSet && (b.ScopeVariant is null || b.ScopeVariant == r.Variant)))
            {
                var ls = (linesByBom.GetValueOrDefault(bom.Id) ?? new()).Where(l => l.VariantCode is null || l.VariantCode == r.Variant);
                var added = Add(bom, r.Variant, r.VariantSize, r.Qty, Pick(ls, r.VariantSize));
                any |= added;
                if (!added && bom.ScopeVariant is not null)
                    issues.Add(new(IssueKind.MissingBom, $"BOM {bom.Label} chưa có định mức cho size {r.VariantSize}"));
            }
            if (!any && !coveredBySet.Contains((r.Variant, r.VariantSize)))
                issues.Add(new(IssueKind.MissingBom, $"Chưa có định mức cho chủng loại {r.Variant} size {r.VariantSize}"));
        }

        var totals = sources.GroupBy(s => s.MaterialCode)
            .ToDictionary(g => g.Key, g => Round(g.Sum(s => s.Qty)));
        return new DemandResult(rows, sources, totals, issues);
    }
}
