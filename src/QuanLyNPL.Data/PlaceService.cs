using Microsoft.Data.Sqlite;

namespace QuanLyNPL.Data;

/// <summary>Loại nơi: chỉ làm nơi xuất, chỉ làm nơi nhận, hoặc cả hai (ví dụ kho, tổ cắt).</summary>
public enum PlaceKind { Xuat, Nhan, CaHai }

/// <summary>
/// Danh mục Nơi xuất / Nơi nhận dùng để hoàn thiện chứng từ nhập kho, xuất kho:
/// phiếu nhập: nơi xuất = nhà cung cấp/khách hàng giao, nơi nhận = kho; phiếu xuất: nơi xuất = kho, nơi nhận = tổ cắt/chuyền...
/// </summary>
public sealed class PlaceService(SqliteConnection c)
{
    public static string Code(PlaceKind k) => k switch { PlaceKind.Xuat => "XUAT", PlaceKind.Nhan => "NHAN", _ => "CA_HAI" };
    public static string Label(string code) => code switch { "XUAT" => "Nơi xuất", "NHAN" => "Nơi nhận", _ => "Nơi xuất và nơi nhận" };

    public long Add(string code, string name, PlaceKind kind, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) throw new BusinessException("Nhập mã và tên nơi xuất/nơi nhận.");
        var id = Db.Insert(c, "INSERT INTO place(code,name,address,kind) VALUES($c,$n,$a,$k)", null, ("$c", code.Trim()), ("$n", name.Trim()), ("$a", address), ("$k", Code(kind)));
        AuditLog.Write(c, "place", "Thêm", code.Trim(), null, $"{name.Trim()} · {Label(Code(kind))}", address, id);
        return id;
    }

    public void Update(long id, string name, PlaceKind kind, string? address)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Nhập tên.");
        var old = Db.Query(c, "SELECT code,name,kind,COALESCE(address,'') FROM place WHERE id=$i", r => (code: r.GetString(0), text: $"{r.GetString(1)} · {Label(r.GetString(2))} {r.GetString(3)}".Trim()), null, ("$i", id)).SingleOrDefault();
        if (Db.Exec(c, "UPDATE place SET name=$n,address=$a,kind=$k WHERE id=$i", null, ("$n", name.Trim()), ("$a", address), ("$k", Code(kind)), ("$i", id)) == 0)
            throw new BusinessException("Không tìm thấy nơi xuất/nơi nhận.");
        AuditLog.Write(c, "place", "Sửa", old.code, old.text, $"{name.Trim()} · {Label(Code(kind))} {address}".Trim(), entityId: id);
    }

    public void SetActive(long id, bool active)
    {
        Db.Exec(c, "UPDATE place SET active=$a WHERE id=$i", null, ("$a", active ? 1 : 0), ("$i", id));
        AuditLog.Write(c, "place", active ? "Dùng lại" : "Ngừng sử dụng", Db.Scalar(c, "SELECT code FROM place WHERE id=$i", null, ("$i", id)) as string, entityId: id);
    }

    /// <summary>Nơi mặc định khi lập phiếu: nơi nhận của phiếu nhập (kho) và nơi xuất của phiếu xuất (kho).</summary>
    public long? DefaultInTo { get => GetId("place_in_to"); set => SetId("place_in_to", value); }
    public long? DefaultOutFrom { get => GetId("place_out_from"); set => SetId("place_out_from", value); }

    /// <summary>Ghi nơi xuất / nơi nhận cho phiếu NHÁP (phiếu đã xác nhận là chứng từ khóa, không sửa).</summary>
    public void SetDocPlaces(long docId, long? fromPlaceId, long? toPlaceId)
    {
        var d = Db.Query(c, "SELECT status,COALESCE(kind,'') FROM stock_doc WHERE id=$i", r => (st: r.GetString(0), kind: r.GetString(1)), null, ("$i", docId)).SingleOrDefault();
        if (d == default) throw new BusinessException("Phiếu không tồn tại.");
        if (d.st != "DRAFT") throw new BusinessException("Phiếu đã xác nhận, không sửa nơi xuất/nơi nhận.");
        if (fromPlaceId is long f) Require(f, fromSide: true);
        if (toPlaceId is long t) Require(t, fromSide: false);
        if (fromPlaceId is not null && fromPlaceId == toPlaceId) throw new BusinessException("Nơi xuất và nơi nhận phải khác nhau.");
        string Name(long? p) => p is long x ? (string)Db.Scalar(c, "SELECT name FROM place WHERE id=$i", null, ("$i", x))! : "—";
        var old = Db.Query(c, "SELECT doc_no,from_place_id,to_place_id FROM stock_doc WHERE id=$i", r => (no: r.GetString(0), f: r.IsDBNull(1) ? (long?)null : r.GetInt64(1), t: r.IsDBNull(2) ? (long?)null : r.GetInt64(2)), null, ("$i", docId)).Single();
        Db.Exec(c, "UPDATE stock_doc SET from_place_id=$f, to_place_id=$t WHERE id=$i", null, ("$f", fromPlaceId), ("$t", toPlaceId), ("$i", docId));
        if (old.f != fromPlaceId || old.t != toPlaceId)
            AuditLog.Write(c, "stock_doc", "Nơi xuất / nhận", old.no, $"{Name(old.f)} → {Name(old.t)}", $"{Name(fromPlaceId)} → {Name(toPlaceId)}", entityId: docId);
    }

    void Require(long id, bool fromSide)
    {
        var p = Db.Query(c, "SELECT kind,active,name FROM place WHERE id=$i", r => (kind: r.GetString(0), active: r.GetInt32(1) == 1, name: r.GetString(2)), null, ("$i", id)).SingleOrDefault();
        if (p == default) throw new BusinessException("Nơi xuất/nơi nhận không tồn tại.");
        if (!p.active) throw new BusinessException($"{p.name} đã ngừng sử dụng.");
        if (fromSide && p.kind == "NHAN") throw new BusinessException($"{p.name} chỉ là nơi nhận, không dùng làm nơi xuất.");
        if (!fromSide && p.kind == "XUAT") throw new BusinessException($"{p.name} chỉ là nơi xuất, không dùng làm nơi nhận.");
    }

    long? GetId(string key) => long.TryParse(Db.Scalar(c, "SELECT value FROM app_setting WHERE key=$k", null, ("$k", key)) as string, out var v) ? v : null;
    void SetId(string key, long? v)
    {
        if (GetId(key) != v)
            AuditLog.Write(c, "setting", "Đổi nơi mặc định", key == "place_in_to" ? "Nơi nhận của phiếu nhập" : "Nơi xuất của phiếu xuất", null,
                v is long x ? Db.Scalar(c, "SELECT name FROM place WHERE id=$i", null, ("$i", x)) as string : "(không)");
        if (v is null) Db.Exec(c, "DELETE FROM app_setting WHERE key=$k", null, ("$k", key));
        else Db.Exec(c, "INSERT INTO app_setting(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", null, ("$k", key), ("$v", v.Value.ToString()));
    }
}

/// <summary>Thông tin đơn vị in trên đầu chứng từ.</summary>
public sealed class CompanyInfo(SqliteConnection c)
{
    public string Name { get => Get("company_name") ?? ""; set => Set("company_name", value); }
    public string Address { get => Get("company_address") ?? ""; set => Set("company_address", value); }
    string? Get(string k) => Db.Scalar(c, "SELECT value FROM app_setting WHERE key=$k", null, ("$k", k)) as string;
    void Set(string k, string v)
    {
        var old = Get(k) ?? "";
        if (old == (v ?? "")) return;
        Db.Exec(c, "INSERT INTO app_setting(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", null, ("$k", k), ("$v", v ?? ""));
        AuditLog.Write(c, "setting", "Sửa", k == "company_name" ? "Tên đơn vị" : "Địa chỉ đơn vị", old, v);
    }
}
