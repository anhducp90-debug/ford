# QuanLyNPL – Quyết định nghiệp vụ & thiết kế dữ liệu (bổ sung cho tài liệu v2.0)

## 1. Quyết định đã chốt (mục 19 của tài liệu v2.0)

| # | Vấn đề | Quyết định |
|---|--------|-----------|
| 1 | Quy đổi size | **Không quy đổi size trong hệ thống.** Mỗi mã hàng có một lưới size; mọi chủng loại và BOM dùng lưới đó. Định mức theo quy ước size khác (ví dụ áo S M L) phải được người dùng quy đổi sang size của mã hàng ngoài hệ thống trước khi khai báo BOM. |
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
- `size_map(...)` – **không dùng nữa** (migration 6 đưa mọi chủng loại về lưới của mã hàng); giữ bảng để tra cứu dữ liệu cũ.

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

1. Với mỗi `order_line` (size S, Q bộ) và mỗi `variant` V: `qty = Q × K(V)`, size của chủng loại = S (không quy đổi).
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
| Mã hàng | Khách hàng, lưới size | trực tiếp | Khách hàng phải đang sử dụng; lưới size dùng cho đơn hàng, mọi chủng loại và BOM |
| Chủng loại | Mã hàng | theo mã hàng | Dùng lưới size của mã hàng; không có quy đổi size |
| NPL | Khách hàng | trực tiếp | **Mã NPL duy nhất toàn hệ thống**; đã dùng thì không đổi khách hàng |
| BOM | Mã hàng, loại (**Tổ cắt / Sản xuất / Đóng gói**), phạm vi (**theo bộ** hoặc **theo chủng loại** → chọn chủng loại) | theo mã hàng | Tổ cắt, Sản xuất: theo bộ hoặc theo chủng loại; **Đóng gói: bắt buộc theo bộ**. Một mã hàng + một loại chỉ chọn MỘT cách (theo bộ hoặc theo chủng loại) để không tính trùng. Theo bộ: số lượng = số bộ, size = lưới mặc định của mã hàng; theo chủng loại: số lượng = số sản phẩm, size = lưới chủng loại. Mỗi phạm vi tối đa 1 BOM mỗi loại (đổi định mức = phiên bản mới); tên tự đặt "<chủng loại> – <loại>" hoặc "<mã hàng> – <loại> (theo bộ)"; NPL của đúng khách hàng |
| Đơn hàng | Mã hàng | theo mã hàng | Mã hàng và khách hàng đang sử dụng |
| Phiếu nhập / kiểm kê | Khách hàng (hoặc suy ra từ NPL) | trực tiếp | Một phiếu chỉ gồm NPL của một khách hàng |
| Phiếu cấp / trả / xuất bù / giữ kho | Đơn hàng | theo đơn | Chỉ NPL của khách hàng của đơn |
| Chuyển NPL A → B | Khách hàng A, khách hàng B, cặp (mã NPL của A → mã NPL của B) | A và B | A ≠ B; cùng đơn vị tính; A đủ tồn khả dụng. Sinh phiếu A xuất (mã của A) và B nhập (mã của B), xác nhận cùng một transaction; hủy/xác nhận chỉ qua chức năng Chuyển NPL |

Tồn kho vẫn tính theo mã NPL; vì mã NPL thuộc một khách hàng nên tồn được kiểm soát theo khách hàng. Dùng NPL của khách hàng khác bắt buộc qua phiếu chuyển, có lịch sử ("Chuyển sang khách hàng khác" / "Nhận từ khách hàng khác").

Dữ liệu cũ: migration 3 để `customer_id` của NPL rỗng (phải gán trước khi dùng trong BOM mới); migration 4 tự điền khách hàng cho phiếu kho cũ (theo đơn, hoặc theo NPL nếu phiếu chỉ có một khách hàng). BOM cũ không gắn chủng loại vẫn tính được nhu cầu; BOM mới luôn tạo theo chủng loại + loại.

### Tính nhu cầu với BOM theo bộ

- BOM theo bộ: nhu cầu = số bộ (theo size mặc định) × định mức size đó (hoặc '*') × (1 + hao hụt). Nguồn nhu cầu ghi chủng loại là "(Bộ)".
- BOM theo chủng loại: nhu cầu = số sản phẩm (số bộ × SL/bộ, cùng size của đơn) × định mức × (1 + hao hụt).
- Đơn hàng đủ dữ liệu khi: mọi BOM đã khai báo đều có định mức cho các size của đơn, và mỗi chủng loại/size có ít nhất một nguồn định mức (BOM riêng của chủng loại hoặc BOM theo bộ).
- Cấp phát theo phạm vi: "ALL" (cả đơn), mã chủng loại, hoặc "(Bộ)" (chỉ NPL từ BOM theo bộ, ví dụ cấp riêng cho tổ đóng gói).
- Migration 5 đổi loại cũ "Chủng loại" thành "Sản xuất". BOM Đóng gói theo chủng loại tạo trước đó vẫn tính được, nhưng không tạo mới được.

## 7. Số bộ tối đa sản xuất được (mục 6.4)

- Theo cơ cấu size của đơn: k = min( (đã cấp cho đơn + tồn khả dụng) / nhu cầu cả đơn ) trên mọi mã NPL; số bộ tối đa = ⌊k × tổng số bộ⌋, không vượt số bộ đặt; báo mã NPL giới hạn.
- Riêng từng size: ⌊min((đã cấp + tồn khả dụng) / định mức 1 bộ của size)⌋ – mỗi size độc lập, không cộng các size.
- Tồn khả dụng = tồn thực tế − lượng giữ cho đơn khác. Thiếu dữ liệu (BOM/định mức) thì không kết luận.
- Kiểm tra đáp ứng: thiếu = max(0, nhu cầu − đã cấp cho đơn − tồn khả dụng).

## 8. Nơi xuất / Nơi nhận và chứng từ

- Danh mục `place` (Quản trị): mã, tên, địa chỉ, loại (chỉ nơi xuất / chỉ nơi nhận / cả hai), ngừng sử dụng.
- Phiếu kho có `from_place_id`, `to_place_id`; chỉ sửa khi phiếu còn nháp; kiểm tra loại và trạng thái; nơi xuất ≠ nơi nhận.
- Mặc định: nơi nhận của phiếu nhập/trả, nơi xuất của phiếu xuất/cấp phát (thường là kho NPL). Xuất bù lấy theo phiếu gốc.
- In chứng từ PDF A4: thông tin đơn vị, số/ngày, khách hàng, đơn hàng, nơi xuất, nơi nhận, phạm vi cấp, bảng NPL (đề xuất/thực cấp), chữ ký (người lập, thủ kho, người giao/nhận, phụ trách). Bản nháp in kèm dòng "BẢN NHÁP – CHƯA XÁC NHẬN".

## 9. Lịch sử, thẻ kho và nhật ký thay đổi

- **Lịch sử giao dịch**: từ `stock_txn` (chỉ phát sinh khi xác nhận) + phiếu: thời gian, số phiếu, loại, khách hàng, mã NPL, số lượng ±, đơn, phạm vi, nơi xuất, nơi nhận, ghi chú.
- **Thẻ kho** (một mã NPL, theo kỳ): tồn đầu kỳ = tổng giao dịch trước ngày bắt đầu; mỗi giao dịch trong kỳ có cột Nhập / Xuất / Tồn sau giao dịch; dòng cuối "Cộng phát sinh / tồn cuối kỳ" khớp Nhập – xuất – tồn và tồn thực tế.
- **Nhật ký thay đổi** (`audit_log`, migration 8 thêm `action`, `ref_text`): mỗi thao tác ghi dữ liệu ghi 1 dòng – thời điểm, nhóm, thao tác, đối tượng (số phiếu, mã…), trước, sau, lý do. Phạm vi: khách hàng, NPL (thêm/sửa/gán khách hàng/ngừng/dùng lại), lưới size, mã hàng, chủng loại, BOM (thêm, tạo phiên bản kèm định mức), đơn (lập, điều chỉnh, đóng/mở), phiếu kho (lập/sửa nháp, nơi xuất/nhận, xác nhận kèm số lượng ±, hủy nháp), giữ/nhả kho, kiểm kê (lập, chốt từng mã, hủy), chuyển NPL, nơi xuất/nhận, cài đặt (nơi mặc định, thông tin đơn vị), bảo mật (tạo/đổi PIN – không ghi giá trị, thời gian tự khóa), sao lưu, phục hồi (ghi vào dữ liệu đã phục hồi, kèm đường dẫn bản an toàn).
- Lọc nhật ký theo khoảng ngày, nhóm, từ khóa; không giới hạn 500 dòng; xuất Excel/PDF.
- Không có người thực hiện (ứng dụng một người dùng, khóa PIN).
- Lịch sử cấu hình đơn chỉ lấy các lần "Điều chỉnh" (không lẫn đóng/mở đơn).

## 10. Cấp phát theo nơi nhận – chọn loại BOM và chủng loại bằng checkbox (bản 1.5.0)

Quy trình thực tế: một đơn hàng do nhiều tổ cùng làm, mỗi tổ làm một hoặc nhiều chủng loại. **Đơn hàng là tổng số lượng cần sản xuất.**

- **Phiếu cấp** = đơn + **nơi nhận** + các **loại BOM được tích** (Tổ cắt / Sản xuất / Đóng gói) + các **chủng loại được tích** + **số bộ theo size**.
  - Tổ cắt: mặc định tích "Cả bộ" → chỉ nhập size và số bộ; bỏ tích để cấp lẻ chủng loại khi cần.
  - Sản xuất: tích 1 chủng loại = cấp lẻ; tích nhiều = cấp gộp; tích tất cả = cả bộ.
  - Tích cả ba loại BOM = **trọn gói A–Z** cho một đơn vị sản xuất.
  - Đóng gói (và mọi BOM khai báo theo bộ) chỉ áp dụng khi tích đủ tất cả chủng loại.
- **Chủng loại tự load theo BOM**: chỉ hiện các chủng loại có BOM (theo chủng loại) của các loại đã tích; loại nào khai báo theo bộ (Đóng gói) thì khóa ở "Cả bộ". "Cả bộ" = mọi chủng loại của đơn, chủng loại không có BOM của loại đó được bỏ qua.
- Số lượng mỗi chủng loại = số bộ × SL chủng loại/bộ; chủng loại được tích nhưng không có BOM của loại đó thì bỏ qua cho loại đó.
- **Kiểm soát số lượng**: với mỗi loại BOM / chủng loại (hoặc bộ) / size, lũy kế số lượng trên phiếu cấp (đã xác nhận + nháp) ≤ số lượng đơn. Màn hình hiện "còn x/y bộ" = số bộ tối đa còn cấp được cho đúng lựa chọn. Hủy phiếu nháp trả lại số lượng. Phiếu bù/trả không cộng số lượng sản phẩm.
- Lưu trữ: `issue_qty(doc, bom_type, object, size, qty)`, `issue_set(doc, size, qty)`, `stock_doc.issue_variants`, `stock_doc.scope` = danh sách loại BOM (vd. `TO_CAT,SAN_XUAT,DONG_GOI`).
- **Nhu cầu phiếu** = Σ định mức các BOM của loại được tích × số lượng (làm tròn 2 số lẻ mỗi mã). Đề xuất = max(0, nhu cầu − số dư của nơi nhận). NPL nơi nhận đang thiếu từ lần trước được thêm vào phiếu kế tiếp.
- **Sổ cân đối** = đơn + **nơi nhận** + mã NPL (phạm vi sổ `NOI_NHAN`, không tách theo loại BOM). Số dư của nơi nhận này **không bao giờ** tự bù cho nơi nhận khác. Migration 11 gộp sổ cũ (bản 1.3/1.4) theo nơi nhận và tính lại số dư.
- Tối đa một phiếu cấp nháp cho mỗi nơi nhận của đơn; các nơi nhận khác nhau lập song song.
- **Xuất bù**: Bù thiếu / Bù hao hụt (hao hụt không làm số dư dương). **Trả NPL** theo nơi nhận, không vượt lượng nơi đó đã nhận.
- **Điều chuyển NPL dư**: từ (đơn, nơi nhận) sang (đơn cùng khách hàng, nơi nhận khác); NPL phải có trong BOM của đơn nhận; chỉ trong số dư dương; không đổi tồn kho.
- **Quyết toán & đóng đơn**: theo nơi nhận / mã NPL, kèm **Tiến độ cấp phát theo size**. Còn dư → không cho đóng; còn thiếu NPL hoặc chưa cấp hết số lượng đơn → phải xác nhận.
- Điều chỉnh đơn: giảm dưới phần đã đưa vào phiếu cấp chỉ cảnh báo ("Cấp vượt đơn"). Đổi BOM chỉ ảnh hưởng phiếu cấp sau.
