using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

/// <summary>Khóa PIN đơn giản cho ứng dụng 1 người dùng: lưu PBKDF2 + salt, không lưu PIN gốc.</summary>
public sealed class PinService(SqliteConnection c)
{
    const int Iter = 100_000;

    public bool HasPin => Db.Scalar(c, "SELECT 1 FROM app_setting WHERE key='pin_hash'") is not null;

    public void SetPin(string pin)
    {
        if (pin is null || pin.Length < 4 || !pin.All(char.IsDigit)) throw new BusinessException("PIN phải gồm ít nhất 4 chữ số.");
        var first = !HasPin;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iter, HashAlgorithmName.SHA256, 32);
        Set("pin_salt", Convert.ToBase64String(salt)); Set("pin_hash", Convert.ToBase64String(hash));
        AuditLog.Write(c, "security", first ? "Tạo mã PIN" : "Đổi mã PIN", "Mã PIN");   // không ghi giá trị PIN
    }

    public bool Verify(string pin)
    {
        var s = Get("pin_salt"); var h = Get("pin_hash");
        if (s is null || h is null) return false;
        var test = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(s), Iter, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(test, Convert.FromBase64String(h));
    }

    public int IdleMinutes
    {
        get => int.TryParse(Get("idle_minutes"), out var m) ? m : 5;
        set
        {
            var old = IdleMinutes; var v = Math.Max(1, value);
            Set("idle_minutes", v.ToString());
            if (old != v) AuditLog.Write(c, "security", "Đổi thời gian tự khóa", "Tự khóa", $"{old} phút", $"{v} phút");
        }
    }

    string? Get(string k) => Db.Scalar(c, "SELECT value FROM app_setting WHERE key=$k", null, ("$k", k)) as string;
    void Set(string k, string v) => Db.Exec(c, "INSERT INTO app_setting(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", null, ("$k", k), ("$v", v));
}
