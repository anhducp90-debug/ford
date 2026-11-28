# QuanLyNPL – Quyết định nghiệp vụ & thiết kế dữ liệu (bổ sung cho tài liệu v2.0)

## 1. Quyết định đã chốt (mục 19 của tài liệu v2.0)

| # | Vấn đề | Quyết định |
|---|--------|-----------|
| 1 | Quy đổi size | Ánh xạ (size mặc định, chủng loại) → đúng 1 size đích của chủng loại. Nhiều size mặc định (Vóc/Chiều cao) được phép cùng về 1 size đích (S/M/L/XL). Không cần tỷ lệ. |
| 2 | Phiên bản BOM | Mỗi lần sửa BOM tạo phiên bản mới với `created_at`. Khi lập đơn, chọn phiên bản có `created_at` lớn nhất mà <= ngày lập đơn, ghi cố định vào snapshot của đơn. |
| 3 | Làm tròn | Tính bằng `decimal` đủ độ chính xác, làm tròn half-up 2 chữ số thập phân ở **tổng nhu cầu mỗi mã NPL** (không làm tròn từng dòng trung gian). Các dòng chi tiết hiển thị 2 số lẻ, có ghi chú "tổng có thể lệch do làm tròn hiển thị". |
| 4 | Bảo mật | Không đăng nhập. Khóa PIN (lưu hash PBKDF2 + salt), tự khóa sau N phút không thao tác (mặc định 5, cấu hình được). Thao tác nhạy cảm (xác nhận xuất, phục hồi DB) không đòi PIN lại, trừ khi ứng dụng đang khóa. |

Điểm còn mở (đề xuất mặc định, đổi được):
- Giữ kho: chỉ giữ khi người dùng bấm "Duyệt kế hoạch cấp"; nhả khi hủy/xác nhận xuất.
- Trả NPL vật lý: tăng tồn **và** giảm lượng đã cấp của đơn (một nghiệp vụ, hai bút toán cùng transaction, đánh dấu liên kết để không đếm hai lần).
- Xuất âm kho: không cho phép.

## 2. Kiến trúc

- .NET 8, WPF, SQLite (Microsoft.Data.Sqlite + Dapper), ClosedXML, QuestPDF, xUnit.
- Giải pháp 3 dự án:
  - `QuanLyNPL.Core` – thuần logic (phân rã, BOM, nhu cầu, tồn, carry). Không phụ thuộc UI/DB. Unit test chính ở đây.
  - `QuanLyNPL.Data` – schema, migration, repository, transaction.
  - `QuanLyNPL.App` – WPF (MVVM), màn hình khóa PIN, các màn hình nghiệp vụ.
- DB: `%LOCALAPPDATA%\QuanLyNPL\data.db`, `PRAGMA foreign_keys=ON`, `journal_mode=WAL`, `busy_timeout`.
- Số lượng: lưu `TEXT` decimal hoặc `INTEGER` đơn vị nhỏ nhất (đề xuất nhân 10^4) để tránh sai số float của SQLite.

## 3. Lược đồ dữ liệu (rút gọn)

Danh mục
- `customer(id, code UNIQUE, name, active)`
- `material(id, code UNIQUE, name, unit, customer_id → customer, active, note)` — mỗi NPL thuộc đúng một khách hàng
- `size_grid(id, code UNIQUE, name, active, used_flag)`; `size_grid_item(id, grid_id, size_code, voc, chieu_cao, UNIQUE(grid_id,size_code))`
- `product(id, customer_id, code, name, default_grid_id, active, UNIQUE(customer_id,code))`
- `variant(id, product_id, code, name, group_name, qty_per_set, grid_id)`
- `size_map(id, variant_id, default_size_code, variant_size_code, UNIQUE(variant_id, default_size_code))`

BOM
- `bom(id, product_id, name, type, scope_variant_id NULL)`
- `bom_version(id, bom_id, version_no, created_at, status)`
- `bom_line(id, bom_version_id, material_id, size_code /* '*' = nền */, usage, waste_percent, variant_id NULL)`

Đơn hàng
- `order(id, customer_id, product_id, order_no UNIQUE, order_date, due_date, status)`
- `order_line(id, order_id, default_size_code, sets_qty)`
- `order_snapshot(id, order_id, created_at, config_json)` – cấu hình bộ, lưới, quy đổi, các `bom_version_id` được chọn.
- `order_breakdown(id, order_id, variant_id, variant_size_code, qty)`
- `order_change_log(...)`

Kho
- `stock_doc(id, type /* IN,OUT,RETURN,ADJUST,COUNT */, doc_no UNIQUE, status /* DRAFT,CONFIRMED */, order_id NULL, scope, ref_doc_id NULL, created_at, confirmed_at)`
- `stock_doc_line(id, doc_id, material_id, qty_suggested, qty_actual)`
- `stock_txn(id, doc_id, material_id, qty_signed, created_at)` – chỉ ghi khi xác nhận; tồn = SUM.
- `reservation(id, order_id, material_id, qty, status)`
- `carry_ledger(id, order_id, material_id, scope, variant_id NULL, opening, issued, demand, closing, doc_id, created_at)` – đọc dòng mới nhất theo (order, material, scope, variant), **không SUM**.
- `audit_log(id, entity, entity_id, before_json, after_json, reason, created_at)`
- `app_setting(key, value)` – PIN hash, thời gian tự khóa.

## 4. Thuật toán nhu cầu (Core)

1. Với mỗi `order_line` (size mặc định S, Q bộ) và mỗi `variant` V: `qty = Q × K(V)`, size đích = `size_map(V, S)`; thiếu map → trạng thái CHƯA ĐỦ DỮ LIỆU.
2. Cộng gộp theo (V, size đích).
3. Với mỗi (V, size đích) lấy các dòng của mọi BOM áp dụng (bản đã snapshot): dòng size cụ thể thắng dòng `*` **trong cùng (bom, material, variant)**; không có dòng nào → CHƯA ĐỦ DỮ LIỆU.
4. Lượng = qty × usage × (1 + waste/100). Cộng theo (material) giữ nguyên nguồn (bom, V, size) để drill-down. Dùng GROUP BY trên bảng đã khử trùng, không join nhân dòng.
5. Làm tròn 2 số lẻ half-up ở tổng mỗi mã NPL.
6. Thiếu = max(0, nhu cầu − (tồn thực tế − đã giữ cho đơn khác)).

## 5. Lộ trình & test

Theo mục 20 của tài liệu v2.0. Bộ test ưu tiên: phân rã (100 bộ size M), n→1 quy đổi, ghi đè `*`, không nhân đôi khi một NPL ở nhiều BOM, làm tròn half-up, tồn khả dụng, carry âm/dương, xác nhận trùng, rollback transaction.

## 6. Quy tắc khai báo và khách hàng (đồng bộ toàn hệ thống)

Nguyên tắc: mỗi màn khai báo chỉ chọn **đối tượng cấp trên**; khách hàng được kế thừa, người dùng không phải gán lại.

| Khai báo | Người dùng chọn | Khách hàng | Ràng buộc chính |
|---|---|---|---|
| Khách hàng | – | (gốc) | Mã không trùng; đã phát sinh dữ liệu thì chỉ ngừng sử dụng |
| Lưới size | – | dùng chung mọi khách hàng | Đã dùng thì khóa cấu trúc, sao chép thành lưới mới |
| Mã hàng | Khách hàng, lưới size mặc định | trực tiếp | Khách hàng phải đang sử dụng |
| Chủng loại + quy đổi size | Mã hàng | theo mã hàng | Mã hàng phải đang sử dụng; size đích thuộc lưới chủng loại |
| NPL | Khách hàng | trực tiếp | **Mã NPL duy nhất toàn hệ thống**; đã dùng thì không đổi khách hàng |
| BOM | Chủng loại, rồi tích **Tổ cắt / Chủng loại / Đóng gói** | theo chủng loại → mã hàng | Mỗi chủng loại tối đa 1 BOM mỗi loại (đổi định mức = phiên bản mới); tên BOM tự đặt "<chủng loại> – <loại>"; NPL của đúng khách hàng; size thuộc lưới chủng loại hoặc '*' |
| Đơn hàng | Mã hàng | theo mã hàng | Mã hàng và khách hàng đang sử dụng |
| Phiếu nhập / kiểm kê | Khách hàng (hoặc suy ra từ NPL) | trực tiếp | Một phiếu chỉ gồm NPL của một khách hàng |
| Phiếu cấp / trả / xuất bù / giữ kho | Đơn hàng | theo đơn | Chỉ NPL của khách hàng của đơn |
| Chuyển NPL A → B | Khách hàng A, khách hàng B, cặp (mã NPL của A → mã NPL của B) | A và B | A ≠ B; cùng đơn vị tính; A đủ tồn khả dụng. Sinh phiếu A xuất (mã của A) và B nhập (mã của B), xác nhận cùng một transaction; hủy/xác nhận chỉ qua chức năng Chuyển NPL |

Tồn kho vẫn tính theo mã NPL; vì mã NPL thuộc một khách hàng nên tồn được kiểm soát theo khách hàng. Dùng NPL của khách hàng khác bắt buộc qua phiếu chuyển, có lịch sử ("Chuyển sang khách hàng khác" / "Nhận từ khách hàng khác").

Dữ liệu cũ: migration 3 để `customer_id` của NPL rỗng (phải gán trước khi dùng trong BOM mới); migration 4 tự điền khách hàng cho phiếu kho cũ (theo đơn, hoặc theo NPL nếu phiếu chỉ có một khách hàng). BOM cũ không gắn chủng loại vẫn tính được nhu cầu; BOM mới luôn tạo theo chủng loại + loại.
