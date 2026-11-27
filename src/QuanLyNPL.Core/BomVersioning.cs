namespace QuanLyNPL.Core;

public sealed record BomVersion(string BomId, int VersionNo, DateTime CreatedAt);

public static class BomVersioning
{
    /// <summary>Chọn phiên bản có ngày tạo lớn nhất không muộn hơn ngày lập đơn (null nếu chưa có).</summary>
    public static BomVersion? Pick(IEnumerable<BomVersion> versions, DateTime orderDate) =>
        versions.Where(v => v.CreatedAt <= orderDate)
                .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.VersionNo)
                .FirstOrDefault();
}
