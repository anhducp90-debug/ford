using Microsoft.Data.Sqlite;
using QuanLyNPL.Data;

namespace QuanLyNPL.App;

/// <summary>Kết nối dữ liệu và các dịch vụ nghiệp vụ dùng chung trong ứng dụng (một người dùng, một tệp SQLite).</summary>
public sealed class Ctx
{
    public string DataDir { get; }
    public string DbPath { get; }
    public SqliteConnection C { get; }
    public CatalogRepository Cat { get; }
    public BomRepository Bom { get; }
    public OrderService Orders { get; }
    public StockService Stock { get; }
    public IssueService Issue { get; }
    public TransferService Transfer { get; }
    public StockCountService Count { get; }
    public ReportService Reports { get; }
    public OrderAdjustmentService Adjust { get; }
    public Queries Q { get; }
    public PinService Pin { get; }
    public BackupService Backup { get; }
    public PlaceService Places { get; }
    public CompanyInfo Company { get; }

    public static string DefaultDataDir() =>
        Environment.GetEnvironmentVariable("QLNPL_DATA_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuanLyNPL");

    public Ctx(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        DbPath = Path.Combine(dataDir, "data.db");
        C = Db.Open(DbPath);
        Cat = new(C); Bom = new(C); Orders = new(C); Stock = new(C);
        Issue = new(C, Orders, Stock); Transfer = new(C, Stock); Count = new(C, Stock);
        Reports = new(C, Orders, Stock); Adjust = new(C, Orders, Stock);
        Q = new(C); Pin = new(C); Backup = new(C); Places = new(C); Company = new(C);
    }

    public string BackupDir => Path.Combine(DataDir, "backups");

    /// <summary>Tự sao lưu mỗi ngày một bản khi mở phần mềm; giữ 14 bản gần nhất.</summary>
    public void AutoBackup()
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            var today = Path.Combine(BackupDir, $"TuDong_{DateTime.Now:yyyy-MM-dd}.db");
            if (!File.Exists(today)) Backup.Backup(today);
            foreach (var old in Directory.GetFiles(BackupDir, "TuDong_*.db").OrderByDescending(f => f).Skip(14)) File.Delete(old);
        }
        catch (Exception ex) { Log.Error("AutoBackup", ex); }
    }
}
