namespace QuanLyNPL.Core;

/// <summary>Chủng loại thuộc mã hàng; QtyPerSet = số sản phẩm trong 1 bộ.</summary>
public sealed record VariantConfig(string Code, decimal QtyPerSet);

/// <summary>Dòng đơn hàng: số bộ theo size mặc định.</summary>
public sealed record OrderLine(string DefaultSize, decimal Sets);

/// <summary>Quy đổi: (chủng loại, size mặc định) -> size đích của chủng loại. Nhiều size mặc định có thể về cùng 1 size đích.</summary>
public sealed class SizeMap
{
    private readonly Dictionary<(string, string), string> _map = new();
    public SizeMap Add(string variant, string defaultSize, string variantSize)
    { _map[(variant, defaultSize)] = variantSize; return this; }
    public string? Resolve(string variant, string defaultSize) =>
        _map.TryGetValue((variant, defaultSize), out var s) ? s : null;
}

/// <summary>BOM (đã chọn phiên bản). ScopeVariant = null: áp dụng toàn mã hàng.</summary>
public sealed record Bom(string Id, string? ScopeVariant);

/// <summary>Dòng định mức. Size "*" là định mức nền; VariantCode null = mọi chủng loại trong phạm vi BOM.</summary>
public sealed record BomLine(string BomId, string MaterialCode, string SizeCode,
    decimal Usage, decimal WastePercent = 0m, string? VariantCode = null);

public sealed record BreakdownRow(string Variant, string VariantSize, decimal Qty);

public sealed record DemandSource(string BomId, string Variant, string VariantSize,
    string MaterialCode, decimal Qty);

public enum IssueKind { MissingSizeMap, MissingBom, InvalidData }
public sealed record Issue(IssueKind Kind, string Message);

public sealed record DemandResult(
    IReadOnlyList<BreakdownRow> Breakdown,
    IReadOnlyList<DemandSource> Sources,
    IReadOnlyDictionary<string, decimal> TotalByMaterial,   // đã làm tròn 2 số lẻ
    IReadOnlyList<Issue> Issues)
{
    public bool IsComplete => Issues.Count == 0;
}
