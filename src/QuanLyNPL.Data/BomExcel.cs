using System.Globalization;
using ClosedXML.Excel;

namespace QuanLyNPL.Data;

/// <summary>Mẫu và đọc file Excel BOM của MỘT BOM (theo bộ hoặc theo một chủng loại). Cột: material_code, size_code, usage, waste_percent. '*' là định mức nền.</summary>
public static class BomExcel
{
    public static readonly string[] Headers = { "material_code", "size_code", "usage", "waste_percent" };

    /// <summary>Mẫu cho BOM đã khai báo: khách hàng, mã hàng, chủng loại, loại BOM và danh sách size lấy từ hệ thống.</summary>
    public static void WriteTemplate(Microsoft.Data.Sqlite.SqliteConnection c, long bomId, Stream output)
    {
        var repo = new BomRepository(c); var info = repo.Info(bomId);
        var cust = (string)Db.Scalar(c, "SELECT code FROM customer WHERE id=$i", null, ("$i", info.CustomerId))!;
        var prod = (string)Db.Scalar(c, "SELECT code FROM product WHERE id=$i", null, ("$i", info.ProductId))!;
        WriteTemplate(output, cust, prod, info.PerSet ? "(theo bộ – size mặc định của mã hàng)" : info.VariantCode, info.Name, repo.Sizes(bomId));
    }

    public static void WriteTemplate(Stream output, string customer, string product, string variant, string bomName, IEnumerable<string> sizes)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("BOM");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).SetValue(Headers[i]).Style.Font.Bold = true;
        ws.Cell(2, 1).SetValue("VAI01"); ws.Cell(2, 2).SetValue("*"); ws.Cell(2, 3).SetValue(1.25m); ws.Cell(2, 4).SetValue(3m);
        ws.Columns().AdjustToContents();
        var g = wb.AddWorksheet("HƯỚNG DẪN");
        var guide = new[] {
            $"Khách hàng: {customer}", $"Mã hàng: {product}", $"Chủng loại: {variant}", $"BOM: {bomName}",
            "", "size_code = '*' là định mức nền; dòng size cụ thể ghi đè dòng nền cho đúng size đó.",
            "usage ≥ 0; waste_percent ≥ 0 (để trống = 0). Chỉ dùng NPL của khách hàng trên và size trong sheet DANH SÁCH SIZE.",
            "Nếu có bất kỳ lỗi nào, không ghi dòng nào; phần mềm báo số dòng và nguyên nhân.",
            "Mỗi lần nhập tạo phiên bản BOM mới thay thế toàn bộ định mức của BOM này; phiên bản cũ vẫn được giữ." };
        for (var i = 0; i < guide.Length; i++) g.Cell(i + 1, 1).SetValue(guide[i]);
        var s = wb.AddWorksheet("DANH SÁCH SIZE");
        s.Cell(1, 1).SetValue("size_code").Style.Font.Bold = true;
        var r = 2; foreach (var z in sizes) s.Cell(r++, 1).SetValue(z);
        wb.SaveAs(output);
    }

    /// <summary>Đọc sheet BOM. Trả về dòng hợp lệ về cú pháp và số dòng Excel tương ứng, hoặc danh sách lỗi.</summary>
    public static (List<BomLineInput> lines, List<int> rows, List<string> errors) Read(Stream input)
    {
        using var wb = new XLWorkbook(input);
        var errors = new List<string>(); var lines = new List<BomLineInput>(); var rows = new List<int>();
        if (!wb.TryGetWorksheet("BOM", out var ws)) { errors.Add("Thiếu sheet 'BOM'."); return (lines, rows, errors); }
        for (var i = 0; i < Headers.Length; i++)
            if (!string.Equals(ws.Cell(1, i + 1).GetString().Trim(), Headers[i], StringComparison.OrdinalIgnoreCase))
                errors.Add($"Cột {i + 1} phải là '{Headers[i]}'.");
        if (errors.Count > 0) return (lines, rows, errors);

        var seen = new HashSet<(string, string)>();
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= last; r++)
        {
            if (Enumerable.Range(1, 4).All(c => ws.Cell(r, c).IsEmpty())) continue;
            var mat = ws.Cell(r, 1).GetString().Trim(); var size = ws.Cell(r, 2).GetString().Trim();
            var ok = true;
            if (mat == "") { errors.Add($"Dòng {r}: thiếu material_code"); ok = false; }
            if (size == "") { errors.Add($"Dòng {r}: thiếu size_code"); ok = false; }
            if (!TryNum(ws.Cell(r, 3), out var usage)) { errors.Add($"Dòng {r}: usage không phải số"); ok = false; }
            var waste = 0m;
            if (!ws.Cell(r, 4).IsEmpty() && !TryNum(ws.Cell(r, 4), out waste)) { errors.Add($"Dòng {r}: waste_percent không phải số"); ok = false; }
            if (!ok) continue;
            if (!seen.Add((mat, size))) { errors.Add($"Dòng {r}: trùng (NPL, size) với dòng trước"); continue; }
            lines.Add(new BomLineInput(mat, size, usage, waste)); rows.Add(r);
        }
        if (lines.Count == 0 && errors.Count == 0) errors.Add("File không có dòng định mức nào.");
        return (lines, rows, errors);
    }

    static bool TryNum(IXLCell cell, out decimal v)
    {
        v = 0;
        if (cell.IsEmpty()) return false;
        if (cell.DataType == XLDataType.Number) { v = (decimal)cell.GetDouble(); return true; }
        var t = cell.GetString().Trim();
        if (!t.Contains('.')) t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>Nhập toàn bộ hoặc không gì cả: lỗi cú pháp và lỗi nghiệp vụ đều được gom rồi báo kèm số dòng Excel.</summary>
    public static long Import(BomRepository repo, long bomId, Stream input, DateTime createdAt)
    {
        var (lines, rows, errors) = Read(input);
        if (errors.Count > 0) throw new BusinessException(string.Join("\n", errors));
        return repo.AddVersion(bomId, createdAt, lines, rows);
    }
}
