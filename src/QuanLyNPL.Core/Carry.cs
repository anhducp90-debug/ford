namespace QuanLyNPL.Core;

/// <summary>Sổ cân đối cấp phát: closing = opening + issued − demand. Dương = cấp thừa, âm = cấp thiếu.</summary>
public sealed record CarryEntry(decimal Opening, decimal Issued, decimal Demand, DateTime At)
{
    public decimal Closing => Opening + Issued - Demand;
}

public static class Carry
{
    /// <summary>Số dư hiện hành = số dư cuối của bản ghi MỚI NHẤT trong đúng phạm vi (không SUM lịch sử).</summary>
    public static decimal CurrentBalance(IEnumerable<CarryEntry> scopeHistory) =>
        scopeHistory.OrderByDescending(e => e.At).Select(e => e.Closing).FirstOrDefault();

    /// <summary>Nhu cầu cần cấp lần sau = nhu cầu − số dư (dư dương giảm nhu cầu, âm cộng vào), không nhỏ hơn 0.</summary>
    public static decimal NetDemand(decimal demand, decimal balance) => Math.Max(0m, demand - balance);
}
