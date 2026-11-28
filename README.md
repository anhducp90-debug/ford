# Quản lý Nguyên phụ liệu (QuanLyNPL)

Phần mềm Windows chạy offline trên một máy, quản lý nhập – BOM – đơn hàng – tính nhu cầu – cảnh báo thiếu – xuất kho nguyên phụ liệu ngành may. Dữ liệu SQLite tại `%LOCALAPPDATA%\QuanLyNPL\data.db`.

- Nghiệp vụ và quy tắc: [`docs/QUYET_DINH_VA_THIET_KE.md`](docs/QUYET_DINH_VA_THIET_KE.md)
- Hướng dẫn nhanh cho người dùng: [`installer/HUONG_DAN.txt`](installer/HUONG_DAN.txt)
- Mô hình giao diện (HTML): [`docs/mockup/index.html`](docs/mockup/index.html)

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `src/QuanLyNPL.Core` | Logic thuần: phân rã đơn, tính nhu cầu theo BOM (theo bộ / theo chủng loại), tồn khả dụng, phiên bản BOM, sổ cân đối, tự khóa |
| `src/QuanLyNPL.Data` | SQLite + migration, danh mục, BOM, đơn hàng (snapshot), kho, cấp phát, chuyển NPL, kiểm kê, báo cáo, Excel/PDF, sao lưu |
| `src/QuanLyNPL.App` | Ứng dụng desktop (Avalonia): 5 phân hệ Quản trị, Khai báo, Nhập kho, Xuất kho, Báo cáo; khóa PIN |
| `tests/` | Unit test (xUnit) |
| `tools/UiSmoke` | Chạy thử giao diện không cần màn hình: mở mọi màn, bấm các quy trình chính, chụp ảnh |
| `installer/` | Script bộ cài NSIS + hướng dẫn |

## Build

```bash
dotnet test tests/QuanLyNPL.Core.Tests && dotnet test tests/QuanLyNPL.Data.Tests
dotnet run --project tools/UiSmoke -- shots          # kiểm thử giao diện
dotnet publish src/QuanLyNPL.App -c Release -r win-x64 --self-contained true -p:DebugType=none -o publish
cp src/QuanLyNPL.App/app.ico installer/HUONG_DAN.txt publish/
makensis -DVERSION=1.0.0 -DSRC=$PWD/publish -DOUT=$PWD/QuanLyNPL_Setup_1.0.0.exe installer/QuanLyNPL.nsi
```

GitHub Actions (`.github/workflows/build-windows.yml`) chạy test, kiểm thử giao diện và đóng gói bộ cài trên Windows; tải bộ cài ở mục Artifacts của lần chạy.
