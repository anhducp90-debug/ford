# InkBoard (tên tạm) — Tài liệu thiết kế v1.1

Không gian sáng tạo vô hạn cho S Pen: phác thảo, sơ đồ, mindmap, chuyển nét vẽ thành chữ / hình. Một app duy nhất cho mọi người dùng, offline hoàn toàn sau khi cài.

## 1. Nguyên tắc (theo thứ tự ưu tiên)

1. **Trực quan, dễ dùng nhất.** Mở app là vẽ được ngay, không cần học. Một tính năng chỉ được vào v1 nếu người dùng phổ thông dùng nó hằng tuần.
2. **Mượt.** 120 Hz khi pan/zoom, độ trễ bút ≤ 10 ms.
3. **Nhẹ.** APK ≤ 40 MB (chưa gồm model chữ), RAM ≤ 300 MB với 100k nét, khởi động ≤ 1 s.
4. **Vô hạn.** Canvas không biên, di chuyển và zoom 0,1% – 10.000%.
5. **Offline + riêng tư.** Tải một lần là dùng đủ tính năng; không tài khoản, không cloud, không telemetry.

Quy tắc ra quyết định: nếu một tính năng làm giao diện rối hơn mà chỉ ít người dùng, nó bị loại hoặc ẩn khỏi giao diện chính.

## 2. Trải nghiệm người dùng (trọng tâm)

### 2.1 Bố cục màn hình
- Canvas chiếm gần như toàn bộ màn hình. Chỉ có **thanh công cụ nổi, tối đa 6 mục**:
  1. **Bút** (chạm lần nữa để chọn màu / độ dày; có 3 kiểu: bút thường, bút nhấn, bút chì)
  2. **Tẩy**
  3. **Chọn** (lasso)
  4. **Chèn** (hình, chữ, ảnh, PDF, mindmap, node sơ đồ)
  5. **Hoàn tác / làm lại**
  6. **Menu** (tài liệu, xuất, cài đặt)
- **Thanh ngữ cảnh:** khi chọn đối tượng, một thanh nhỏ hiện ngay cạnh nó với đúng những việc cần làm (màu, chuyển thành chữ/hình, nhân đôi, xoá). Không có menu lồng nhiều tầng.
- Các thanh có thể ẩn bằng một chạm; nút "về nội dung" luôn ở góc để không bao giờ bị lạc trong canvas vô hạn; minimap nhỏ chỉ hiện khi đang kéo xa.

### 2.1b Định vị trên canvas vô hạn
- **Thước toạ độ** dọc mép trên (X) và mép trái (Y, dương hướng lên như CAD), bước chia tự co giãn theo mức zoom, có vạch đánh dấu vị trí bút; dấu gốc (0,0) nằm dưới nội dung.
- **Dòng toạ độ** góc dưới: X/Y của bút (khi di bút hoặc vẽ); khi đang chọn thì hiện X, Y, rộng × cao của vùng chọn.
- **Bản đồ thu nhỏ** góc dưới phải: toàn bộ bản vẽ + khung vùng đang xem; chạm hoặc kéo trên bản đồ để nhảy tới.
- **Thành phần**: danh sách mindmap, khối, chữ, ghi chú dán, bảng, ảnh, hình và các cụm nét vẽ (gom theo vị trí) kèm toạ độ, lọc theo loại; chạm để đưa vào giữa màn hình.
- Thước và bản đồ bật/tắt trong Menu (mặc định bật).

### 2.2 Cử chỉ đơn giản, nhất quán
| Hành động | Cách làm |
|---|---|
| Vẽ | Bút S Pen chạm xuống |
| Di chuyển canvas | Một ngón kéo (hoặc giữ nút S Pen + kéo) |
| Zoom | Hai ngón chụm / mở; chạm đúp hai ngón để vừa nội dung |
| Chọn | Khoanh vòng bằng lasso (vẽ vòng kín quanh vùng cần chọn); chạm đối tượng; chọn thêm bằng cách khoanh tiếp |
| Di chuyển | Kéo thả phần đã chọn bằng bút hoặc ngón tay; thả ra là xong |
| Phóng to / xoay | Kéo tay cầm ở góc vùng chọn, hoặc hai ngón chụm / xoay trên vùng chọn |
| Nhân đôi / xoá | Nút trên thanh ngữ cảnh cạnh vùng chọn; chạm ra ngoài để bỏ chọn |
| Xoá nhanh | Gạch ngoằn ngoèo lên chữ/nét (scratch-out) |
| Hoàn tác | Chạm hai ngón / nút trên thanh công cụ |
| Nét → hình | Vẽ xong, giữ bút ~0,5 s ở cuối nét: nét tự đều thành hình chuẩn |
| Nét → chữ | Khoanh chữ viết tay, chạm "Chuyển thành chữ" trên thanh ngữ cảnh |

Mọi chuyển đổi tự động đều **hoàn tác được ngay một chạm** và giữ lại nét gốc.

### 2.2b Đa chạm (multi-touch) và phối hợp hai tay
Canvas **không giới hạn một chạm**: lõi theo dõi từng điểm chạm độc lập (tối đa 10), phân biệt **bút** và **ngón tay** qua loại công cụ (`TOOL_TYPE_STYLUS` / `FINGER`). Quy tắc nền: **bút = làm việc, ngón tay = điều hướng và bổ trợ.**

| Tổ hợp | Kết quả |
|---|---|
| Bút chạm + di chuyển | Vẽ / tẩy / khoanh lasso theo công cụ đang chọn |
| 1 ngón kéo | Di chuyển canvas (nếu đang chọn vùng và ngón đặt trên vùng chọn: kéo vùng chọn) |
| 2 ngón chụm / mở / xoay | Zoom, xoay góc nhìn, kéo canvas cùng lúc |
| 2 ngón chạm nhanh | Hoàn tác; 3 ngón chạm nhanh: làm lại |
| **1 ngón giữ yên + bút di chuyển** | Ngón giữ = phím bổ trợ (như giữ Shift): bút vẽ đường thẳng / hình đều, khoá góc 15°, vuông, tròn chuẩn; hoặc khi đang kéo vùng chọn thì khoá hướng ngang/dọc |
| **2 ngón giữ yên + bút di chuyển** | Canvas được "ghim" cố định: bút vẽ thoải mái ở mức zoom chính xác mà không bị trượt; thả ngón để tiếp tục kéo |
| Bút đang vẽ + **2 ngón** kéo/zoom bằng tay kia | Vẫn hoạt động đồng thời: nét tiếp tục vẽ trong khi canvas di chuyển, nên vẽ được nét dài vượt màn hình. (Một ngón đơn lẻ bị bỏ qua khi bút đang gần/đang vẽ để chống chạm nhầm lòng bàn tay.) |
| Ngón giữ lên một đối tượng + bút chạm đối tượng khác | Thêm vào vùng chọn (chọn nhiều không cần khoanh) |

- Có thể đổi trong Cài đặt: "Ngón tay cũng vẽ được" (cho máy không có bút), "Bút kéo canvas khi giữ nút".
- Palm rejection: lòng bàn tay (vùng chạm lớn) bị bỏ qua khi bút đang gần màn hình hoặc đang vẽ.
- Các tổ hợp trên được kiểm thử bằng bộ test sự kiện chạm giả lập từ Giai đoạn 1; những tổ hợp gây nhầm lẫn khi thử với người dùng thật sẽ bị đơn giản hoá.

### 2.3 Bắt đầu nhanh
- Onboarding tối đa 3 màn hình, mỗi màn một cử chỉ, có thể bỏ qua. Lần mở đầu tải gói nhận dạng chữ (xem §5).
- Màn hình chính là danh sách tài liệu dạng lưới thumbnail, có **thư mục** (kéo thả tài liệu vào thư mục) và **ô tìm kiếm theo tên tài liệu / thư mục**; nút "+" tạo mới với **5 điểm bắt đầu:** Trống · Mindmap · Sơ đồ (flowchart) · Chú thích PDF · Quét tài liệu.
- Tự lưu liên tục; không có nút "Lưu", không có hộp thoại hỏi lưu.
- Giao diện Việt / Anh, chế độ sáng/tối theo hệ thống, nút to đủ cho bút lẫn ngón tay.

## 3. Phạm vi v1: giữ, ẩn, hoãn, loại

### GIỮ (cốt lõi)
- **Chọn vùng và kéo thả (ưu tiên cao):** khoanh vòng lasso quanh nét, chữ, hình, node bất kỳ rồi kéo thả để di chuyển cả cụm cùng lúc; hỗ trợ phóng to, xoay, nhân đôi, xoá. Khi kéo gần mép màn hình, canvas tự cuộn để kéo đi xa trong không gian vô hạn. Connector và nhánh mindmap tự bám theo khi di chuyển. Vùng chọn hiển thị bóng mờ khi kéo để thấy trước vị trí thả. Mọi thao tác hoàn tác được. Lasso chỉ chọn đối tượng nằm trong vòng (nét đi qua vòng cũng được chọn nếu phần lớn nằm trong).
- Canvas vô hạn vector, bút S Pen (áp lực, nghiêng, hover, nút bấm), tẩy nét, lasso, hoàn tác/làm lại không giới hạn.
- **Nét → chữ** (Việt, Anh), **nét → hình** (đường thẳng, tròn, elip, chữ nhật, tam giác, mũi tên…), làm mượt nét.
- **Mindmap tự động:** chạm "+" để thêm nhánh (viết hoặc gõ), tự bố trí, thu gọn/mở rộng, kéo nhánh đổi cha.
- **Sơ đồ (flowchart + hình cơ bản):** hộp và đường nối thông minh bám theo node, snap/căn chỉnh tự động (không có bảng thuộc tính phức tạp).
- Thư viện shape **gọn, tuyển chọn** (~100 hình thường dùng, có tìm kiếm) + "Lưu vào thư viện của tôi" cho nhóm đối tượng.
- Chữ (text box), ảnh, **PDF làm nền để chú thích**.
- **Thư mục + tìm kiếm tên tài liệu:** tổ chức tài liệu trên màn hình chính; sắp xếp theo ngày sửa / tên; thùng rác 30 ngày. Hoàn toàn cục bộ.
- **Quét tài liệu bằng camera:** chụp giấy tờ/bảng trắng, tự cắt viền, nắn thẳng, tăng độ rõ rồi đặt làm ảnh nền hoặc PDF nền để vẽ chú thích lên. Dùng ML Kit Document Scanner (chạy trên máy); nếu không khả dụng thì dùng bộ quét tự dựng đơn giản (cắt + nắn phối cảnh).
- **Bảng đơn giản:** chèn bảng kẻ ô (chọn số hàng/cột), viết tay hoặc gõ trong từng ô, kéo cạnh để đổi kích thước, thêm/xoá hàng cột bằng thanh ngữ cảnh. Không có công thức; nét viết tay trong ô có thể chuyển thành chữ.
- **Sticky note:** ghi chú dán nhiều màu (hình chữ nhật có chữ/nét), kéo thả như mọi đối tượng, tự bám vào mindmap/sơ đồ nếu thả lên node.
- Xuất **PNG, PDF** (vùng chọn / toàn bộ / khung nhìn), **SVG** (rẻ vì đã là vector). Nhập ảnh và PDF.
- Chế độ tối, chia màn hình, tự lưu, file `.inkb`.

### ẨN (có nhưng không hiện trên giao diện chính; vào qua Cài đặt hoặc nhấn giữ)
- Nền canvas (trơn / lưới / chấm / dòng), snap lưới, màu tuỳ chỉnh, độ nhạy áp lực, tuỳ chỉnh nút bút, bookmark vị trí.

### HOÃN (sau v1, chỉ khi có nhu cầu thực)
- **v1.5 (ngay sau v1): tìm kiếm chữ viết tay.** Nhận dạng ngầm khi viết và lập chỉ mục cục bộ để tìm trong nội dung tài liệu (cả nét chưa chuyển thành chữ); chạy nền, tiết kiệm pin, có thể tắt.
- Sau đó, chỉ khi có nhu cầu thực: công thức toán, Air Actions, DeX, khoá tài liệu (vân tay), chế độ trình chiếu, làm đẹp chữ viết tay (kiểu Smart Script).
- Không làm theo hướng: AI cần mạng (tóm tắt/sinh nội dung), ghi âm gắn nét viết.

### LOẠI (không làm, để app nhẹ và dễ dùng)
- Layer nhiều tầng (thay bằng "đưa lên trên / xuống dưới" và "khoá" trên thanh ngữ cảnh), UML/ER chuyên sâu, boolean ops và sửa điểm neo thủ công, component/style hệ thống kiểu Figma, thước đo / DXF, auto layout CSS, Dev Mode, plugin, cộng tác thời gian thực, cloud/đồng bộ, hệ cọ hội hoạ (hàng chục loại cọ), nhập SVG.

## 4. Kiến trúc

```
Kotlin + Compose (UI tối giản, quản lý tài liệu, cài đặt)
        │  JNI (mỏng: sự kiện bút, lệnh, trạng thái)
Rust core ── mô hình vector · camera f64 · quadtree · undo/redo
           ├─ nhận diện hình · làm mượt Bézier · bố trí mindmap/sơ đồ
           ├─ xuất SVG/PDF · định dạng .inkb
           └─ Skia (GPU) render
ML Kit Digital Ink (offline) ── nhận dạng chữ viết tay vi/en
```

### Canvas vô hạn
- Toạ độ thế giới f64; camera = (tâm, tỉ lệ). Không có trang hay biên.
- Chỉ vẽ phần nhìn thấy: quadtree, cắt vùng ngoài màn hình, LOD (nét nhỏ hơn 0,5 px được gộp khi zoom xa), cache tile theo mức zoom. Khi đang pan/zoom hiển thị tile cũ rồi tinh chỉnh dần để luôn 120 fps.
- Điểm nét được nén (delta + lượng tử hoá), đơn giản hoá bằng Bézier fit khi hoàn tất nét.

### Bút
- Front-buffer / `androidx.graphics` low-latency + dự đoán điểm.
- Palm rejection: chỉ bút vẽ, ngón tay di chuyển/zoom.

### Tài liệu & file
- `.inkb`: container nén (SQLite hoặc zip) gồm đối tượng vector, ảnh nhúng, thumbnail, phiên bản schema. Journal tự lưu → không mất dữ liệu khi crash/hết pin.

## 5. Offline "tải một lần"

- Toàn bộ tính năng nằm trong APK, không gọi mạng.
- ML Kit Digital Ink **không cho đóng gói model vào APK**: model Việt + Anh (~20–30 MB mỗi ngôn ngữ) tải một lần ở lần mở đầu qua Google Play Services, rồi chạy offline vĩnh viễn.
- Chưa tải thì mọi thứ khác vẫn dùng bình thường; chỉ "nét → chữ" chờ model, có thông báo rõ ràng.
- Dự phòng (đánh giá ở Giai đoạn 3): tự đóng gói model riêng vào APK/asset pack nếu cần độc lập với Play Services.

## 6. Hiệu năng & chất lượng
- Benchmark ngay Giai đoạn 0: 100k nét, 10k đối tượng, pan/zoom 120 fps trên S25 Ultra; log frame time tự động.
- Kiểm thử: unit test lõi Rust, test hồi quy file, thử nghiệm với người dùng thật ở mỗi giai đoạn (đo "bao lâu thì người mới vẽ và chuyển được nét thành chữ/hình mà không cần hướng dẫn").

## 7. Google Play (miễn phí)
Chính sách quyền riêng tư, Data safety "không thu thập", AAB + App Signing, ảnh store, hỗ trợ màn hình lớn/gập, closed test trước khi công khai. Android 10+ (API 29), tối ưu Galaxy S25 Ultra / Tab S / Note.

## 8. Lộ trình

| GĐ | Nội dung | Kết quả kiểm chứng |
|---|---|---|
| 0 | Khung dự án (Gradle + cargo-ndk), JNI, Skia, prototype canvas + benchmark | 100k nét mượt 120 fps; đo được độ trễ bút |
| 1 | Canvas vô hạn, pan/zoom, bút, tẩy, hoàn tác, lưu/mở, thanh công cụ tối giản | Người mới vẽ được ngay |
| 2 | Lasso chọn + kéo thả/phóng/xoay, shape, chữ, ảnh, sticky note, bảng đơn giản, xuất PNG/PDF/SVG | Kéo cụm 10k đối tượng vẫn mượt; xuất đúng, vector |
| 3 | Nét → chữ (vi/en), nét → hình, scratch-out | Độ chính xác đo trên bộ mẫu |
| 4 | Mindmap, sơ đồ + connector, thư viện shape | Dựng mindmap/flowchart không cần hướng dẫn |
| 5 | PDF nền + chú thích, quét tài liệu bằng camera, thư mục + tìm tên tài liệu, 5 điểm bắt đầu, onboarding | Quét giấy → chú thích → xuất PDF; tìm và sắp xếp được tài liệu |
| 7 (v1.5) | Tìm kiếm chữ viết tay (nhận dạng ngầm + chỉ mục cục bộ) | Tìm được từ khoá trong nét viết tay; không làm chậm bút |
| 6 | Tối ưu, thử đa thiết bị, đưa lên Play | Closed test → release |

## 9. Rủi ro
| Rủi ro | Giảm thiểu |
|---|---|
| Độ trễ bút trên canvas Skia tự dựng | Front-buffer, prototype ngay GĐ 0 trước khi cam kết |
| Hybrid Rust/Kotlin phức tạp | JNI mỏng, CI build đa kiến trúc (arm64 là chính) |
| ML Kit không bundle được | Onboarding tải một lần; phương án model tự đóng gói |
| Nhận dạng chữ Việt có dấu chưa hoàn hảo | Sửa nhanh, giữ nét gốc, gợi ý thay thế |
| Tính năng phình to làm app rối | Quy tắc §1; mọi tính năng mới phải qua kiểm tra "dùng hằng tuần?" |

## 10. Quyết định đã chốt
Rust + Skia lõi, Kotlin/Compose giao diện · Android 10+ · Việt/Anh · Google Play miễn phí · không cloud/đồng bộ · **đơn giản và trực quan là ưu tiên số 1** · danh sách giữ/ẩn/hoãn/loại ở §3.
