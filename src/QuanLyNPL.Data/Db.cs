using System.Globalization;
using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

public static class Db
{
    public static SqliteConnection Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        var c = new SqliteConnection(cs);
        c.Open();
        Exec(c, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        if (path != ":memory:") Exec(c, "PRAGMA journal_mode=WAL;");
        Migrate(c);
        return c;
    }

    public static void Migrate(SqliteConnection c)
    {
        var v = Convert.ToInt32(Scalar(c, "PRAGMA user_version;"));
        for (var i = v; i < Schema.Migrations.Length; i++)
        {
            using var tx = c.BeginTransaction();
            Exec(c, Schema.Migrations[i], tx);
            if (i == 10) RechainPlaceLedger(c, tx);
            Exec(c, $"PRAGMA user_version={i + 1};", tx);
            tx.Commit();
        }
    }

    /// <summary>Gộp sổ cân đối theo nơi nhận (migration 11): tính lại số dư đầu/cuối theo thứ tự ghi sổ của từng (đơn, nơi nhận, mã NPL).</summary>
    static void RechainPlaceLedger(SqliteConnection c, SqliteTransaction tx)
    {
        var rows = Query(c, "SELECT id,order_id,material_id,place_id,issued,demand,loss FROM carry_ledger WHERE scope='NOI_NHAN' ORDER BY id",
            r => (id: r.GetInt64(0), key: (r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)), delta: D(r.GetString(4)) - D(r.GetString(5)) - D(r.GetString(6))), tx);
        var bal = new Dictionary<(long, long, long), decimal>();
        foreach (var r in rows)
        {
            var op = bal.GetValueOrDefault(r.key);
            bal[r.key] = op + r.delta;
            Exec(c, "UPDATE carry_ledger SET opening=$o, closing=$c WHERE id=$i", tx, ("$o", D(op)), ("$c", D(op + r.delta)), ("$i", r.id));
        }
    }

    public static int Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string, object?)[] p)
    {
        using var cmd = Cmd(c, sql, tx, p);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string, object?)[] p)
    {
        using var cmd = Cmd(c, sql, tx, p);
        return cmd.ExecuteScalar();
    }

    public static long Insert(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string, object?)[] p)
    {
        Exec(c, sql, tx, p);
        return Convert.ToInt64(Scalar(c, "SELECT last_insert_rowid();", tx));
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, SqliteTransaction? tx = null, params (string, object?)[] p)
    {
        using var cmd = Cmd(c, sql, tx, p);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    static SqliteCommand Cmd(SqliteConnection c, string sql, SqliteTransaction? tx, (string, object?)[] p)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    // Số lượng lưu dạng TEXT (invariant) để không mất chính xác như REAL.
    public static string D(decimal d) => (d / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);   // bỏ số 0 thừa: 10.0 -> 10
    public static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
    public static string Date(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    public static DateTime Date(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture);
}

public class BusinessException(string message) : Exception(message);
