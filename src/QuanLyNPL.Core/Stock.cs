namespace QuanLyNPL.Core;

/// <summary>IssuedToOrder: lượng đã cấp ròng cho chính đơn (cấp + bù − trả) – phần nhu cầu này đã được đáp ứng.</summary>
public sealed record StockPosition(string MaterialCode, decimal OnHand, decimal ReservedForOthers, decimal IssuedToOrder = 0m);

public enum FulfillStatus { Enough, Short, Incomplete }

public sealed record AvailabilityLine(string MaterialCode, decimal Demand, decimal OnHand,
    decimal Reserved, decimal Available, decimal Shortage, decimal Issued = 0m);

public sealed record AvailabilityResult(FulfillStatus Status, IReadOnlyList<AvailabilityLine> Lines);

public static class Stock
{
    /// <summary>Tồn thực tế = nhập + điều chỉnh tăng + trả − xuất − điều chỉnh giảm (qty đã mang dấu).</summary>
    public static decimal OnHand(IEnumerable<decimal> signedConfirmedTxns) => signedConfirmedTxns.Sum();

    public static AvailabilityResult Check(DemandResult demand, IEnumerable<StockPosition> stock)
    {
        var pos = stock.ToDictionary(s => s.MaterialCode);
        var lines = new List<AvailabilityLine>();
        foreach (var (mat, need) in demand.TotalByMaterial.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            pos.TryGetValue(mat, out var p);
            var onHand = p?.OnHand ?? 0m;
            var reserved = p?.ReservedForOthers ?? 0m;
            var avail = onHand - reserved;
            var issued = p?.IssuedToOrder ?? 0m;
            // Thiếu = nhu cầu − phần đã cấp cho đơn − tồn khả dụng.
            lines.Add(new(mat, need, onHand, reserved, avail, Math.Max(0m, need - issued - avail), issued));
        }
        // Dữ liệu chưa đủ thì không bao giờ kết luận ĐỦ.
        var status = !demand.IsComplete ? FulfillStatus.Incomplete
            : lines.Any(l => l.Shortage > 0) ? FulfillStatus.Short : FulfillStatus.Enough;
        return new(status, lines);
    }
}
