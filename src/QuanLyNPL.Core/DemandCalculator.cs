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

    public static DemandResult Calculate(IEnumerable<OrderLine> orderLines, IEnumerable<VariantConfig> variants,
        SizeMap map, IEnumerable<Bom> boms, IEnumerable<BomLine> bomLines)
    {
        var (rows, issues) = Breakdown(orderLines, variants, map);
        var bomList = boms.ToList();
        var linesByBom = bomLines.GroupBy(l => l.BomId).ToDictionary(g => g.Key, g => g.ToList());
        var sources = new List<DemandSource>();

        foreach (var r in rows)
        {
            var any = false;
            foreach (var bom in bomList.Where(b => b.ScopeVariant is null || b.ScopeVariant == r.Variant))
            {
                if (!linesByBom.TryGetValue(bom.Id, out var ls)) continue;
                var applicable = ls.Where(l => l.VariantCode is null || l.VariantCode == r.Variant).ToList();
                foreach (var g in applicable.GroupBy(l => l.MaterialCode))
                {
                    // Dòng size cụ thể ghi đè dòng nền '*' cho cùng (BOM, NPL).
                    var specific = g.Where(l => l.SizeCode == r.VariantSize).ToList();
                    var chosen = specific.Count > 0 ? specific : g.Where(l => l.SizeCode == BaseSize).ToList();
                    foreach (var l in chosen)
                    {
                        if (l.Usage < 0 || l.WastePercent < 0)
                        { issues.Add(new(IssueKind.InvalidData, $"Định mức/hao hụt âm: BOM {bom.Id}, NPL {l.MaterialCode}")); continue; }
                        any = true;
                        sources.Add(new(bom.Id, r.Variant, r.VariantSize, l.MaterialCode,
                            r.Qty * l.Usage * (1 + l.WastePercent / 100m)));
                    }
                }
            }
            if (!any)
                issues.Add(new(IssueKind.MissingBom, $"Chưa có định mức cho chủng loại {r.Variant} size {r.VariantSize}"));
        }

        var totals = sources.GroupBy(s => s.MaterialCode)
            .ToDictionary(g => g.Key, g => Round(g.Sum(s => s.Qty)));
        return new DemandResult(rows, sources, totals, issues);
    }
}
