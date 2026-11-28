using ClosedXML.Excel;

namespace QuanLyNPL.Data;

/// <summary>Dòng phiếu nhập đọc từ Excel: mã NPL, số lượng, đơn vị nhập (tùy chọn, vd. kg), hệ số quy đổi (tùy chọn).</summary>
public sealed record ReceiptExcelLine(string Material, decimal Qty, string? Unit, decimal? Factor);

/// <summary>Nhập dòng phiếu nhập kho từ Excel (cột A mã NPL, B số lượng, C đơn vị nhập, D hệ số quy đổi).</summary>
public static class ReceiptExcel
{
    static readonly string[] Headers = { "Mã NPL", "Số lượng", "Đơn vị nhập (để trống = đơn vị tồn)", "Hệ số quy đổi (để trống = mặc định)" };

    public static void WriteTemplate(IEnumerable<MaterialRow> materials, Stream output)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Phieu nhap");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var m in materials) { ws.Cell(r, 1).Value = m.Code; if (m.AltUnit is not null) { ws.Cell(r, 3).Value = m.AltUnit; ws.Cell(r, 4).Value = m.AltFactor; } r++; }
        var list = wb.AddWorksheet("Danh muc NPL");
        list.Cell(1, 1).Value = "Mã NPL"; list.Cell(1, 2).Value = "Tên"; list.Cell(1, 3).Value = "ĐVT tồn"; list.Cell(1, 4).Value = "Đơn vị nhập phụ"; list.Cell(1, 5).Value = "Hệ số";
        r = 2;
        foreach (var m in materials) { list.Cell(r, 1).Value = m.Code; list.Cell(r, 2).Value = m.Name; list.Cell(r, 3).Value = m.Unit; list.Cell(r, 4).Value = m.AltUnit ?? ""; if (m.AltFactor is decimal f) list.Cell(r, 5).Value = f; r++; }
        ws.Columns().AdjustToContents(); list.Columns().AdjustToContents();
        wb.SaveAs(output);
    }

    /// <summary>Đọc dòng; bỏ dòng không có số lượng. Lỗi định dạng báo theo số dòng Excel, có lỗi thì không trả dòng nào.</summary>
    public static List<ReceiptExcelLine> Read(Stream input)
    {
        using var wb = new XLWorkbook(input);
        var ws = wb.Worksheet(1);
        var lines = new List<ReceiptExcelLine>(); var errs = new List<string>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var code = row.Cell(1).GetString().Trim();
            var qtyCell = row.Cell(2);
            if (code.Length == 0 && qtyCell.IsEmpty()) continue;
            if (qtyCell.IsEmpty()) continue;
            if (code.Length == 0) { errs.Add($"Dòng {row.RowNumber()}: thiếu mã NPL."); continue; }
            if (!TryDec(qtyCell, out var q)) { errs.Add($"Dòng {row.RowNumber()}: số lượng không hợp lệ."); continue; }
            var unit = row.Cell(3).GetString().Trim();
            decimal? factor = null;
            if (!row.Cell(4).IsEmpty()) { if (TryDec(row.Cell(4), out var f)) factor = f; else { errs.Add($"Dòng {row.RowNumber()}: hệ số không hợp lệ."); continue; } }
            lines.Add(new ReceiptExcelLine(code, q, unit.Length == 0 ? null : unit, factor));
        }
        if (errs.Count > 0) throw new BusinessException(string.Join("\n", errs));
        if (lines.Count == 0) throw new BusinessException("File không có dòng nào có số lượng.");
        return lines;
    }

    static bool TryDec(IXLCell cell, out decimal v)
    {
        if (cell.DataType == XLDataType.Number) { v = (decimal)cell.GetDouble(); return true; }
        var t = cell.GetString().Trim().Replace(" ", "");
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(',', '.'); else t = t.Replace(',', '.');
        return decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out v);
    }
}
