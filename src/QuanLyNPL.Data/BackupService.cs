using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

/// <summary>Sao lưu / phục hồi tệp dữ liệu SQLite (dùng SQLite Online Backup, an toàn khi đang mở).</summary>
public sealed class BackupService(SqliteConnection c)
{
    public static string DefaultFileName() => $"QuanLyNPL_{DateTime.Now:yyyy-MM-dd_HHmm}.db";

    public void Backup(string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        AuditLog.Write(c, "backup", "Sao lưu", Path.GetFileName(destinationPath), null, destinationPath);
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString());
        dest.Open();
        c.BackupDatabase(dest);
    }

    /// <summary>Kiểm tra tệp có phải dữ liệu QuanLyNPL không (có bảng chính và phiên bản cấu trúc hợp lệ).</summary>
    public static void Validate(string path)
    {
        if (!File.Exists(path)) throw new BusinessException("Không tìm thấy tệp sao lưu.");
        try
        {
            using var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            src.Open();
            var ver = Convert.ToInt32(Db.Scalar(src, "PRAGMA user_version"));
            var tables = Db.Query(src, "SELECT name FROM sqlite_master WHERE type='table'", r => r.GetString(0));
            if (ver < 1 || !new[] { "customer", "material", "stock_txn" }.All(tables.Contains))
                throw new BusinessException("Tệp không phải dữ liệu của phần mềm Quản lý NPL.");
            if (ver > Schema.Migrations.Length)
                throw new BusinessException("Tệp sao lưu được tạo bởi phiên bản phần mềm mới hơn. Hãy cập nhật phần mềm trước.");
            if ((string)Db.Scalar(src, "PRAGMA integrity_check")! != "ok") throw new BusinessException("Tệp sao lưu bị hỏng.");
        }
        catch (SqliteException) { throw new BusinessException("Tệp không phải cơ sở dữ liệu hợp lệ."); }
    }

    /// <summary>
    /// Phục hồi: kiểm tra tệp, tự sao lưu dữ liệu hiện tại vào <paramref name="safetyDir"/>, rồi ghi đè và nâng cấp cấu trúc nếu cần.
    /// Trả về đường dẫn bản sao lưu an toàn.
    /// </summary>
    public string Restore(string sourcePath, string safetyDir)
    {
        Validate(sourcePath);
        Directory.CreateDirectory(safetyDir);
        var safety = Path.Combine(safetyDir, "TruocPhucHoi_" + DefaultFileName());
        Backup(safety);
        using (var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            src.Open();
            src.BackupDatabase(c);
        }
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        Db.Migrate(c);
        // Ghi vào dữ liệu vừa phục hồi để biết dữ liệu đã được thay từ đâu và bản an toàn nằm ở đâu.
        AuditLog.Write(c, "backup", "Phục hồi", Path.GetFileName(sourcePath), "Dữ liệu trước khi phục hồi: " + safety, sourcePath);
        return safety;
    }
}
