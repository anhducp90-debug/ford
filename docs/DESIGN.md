# InkBoard (tên tạm) — Tài liệu thiết kế v1.0

Không gian sáng tạo vô hạn cho S Pen: phác thảo, sơ đồ, mindmap, chuyển nét vẽ thành chữ / hình / vector. Offline hoàn toàn sau khi cài.

## 1. Mục tiêu và nguyên tắc

| Nguyên tắc | Cam kết đo được |
|---|---|
| Không gian vô hạn | Kéo/di chuyển workspace theo mọi hướng không giới hạn; zoom 0,1% – 10.000%; toạ độ f64 nên không vỡ nét |
| Nhẹ | APK ≤ 40 MB (chưa gồm model nhận dạng), RAM ≤ 300 MB với 100k nét, khởi động nguội ≤ 1 s |
| Mượt | 120 Hz trên S25 Ultra khi pan/zoom; độ trễ bút ≤ 10 ms (front-buffer) |
| Offline | Tải một lần (APK + gói model lúc mở app lần đầu), sau đó dùng đủ tính năng, không cần mạng, không có telemetry |
| Riêng tư | Không tài khoản, không cloud; dữ liệu chỉ nằm trên máy |

## 2. Phạm vi

- **Thiết bị:** Android 10+ (API 29), tối ưu Galaxy S25 Ultra / Tab S / Note; máy khác có stylus vẫn chạy (không có tính năng riêng S Pen).
- **Ngôn ngữ giao diện:** Tiếng Việt, Tiếng Anh. **Nhận dạng chữ:** Tiếng Việt, Tiếng Anh.
- **Có trong v1:** canvas vô hạn, bút S Pen, shape, layer, text, snap/lưới, lasso, nét → chữ/hình/vector, mindmap tự động, flowchart/UML/ER + thư viện shape, chú thích PDF/ảnh nền, xuất PNG/PDF/SVG, nhập ảnh/PDF/SVG, chế độ tối, chia màn hình.
- **Để sau v1:** công thức toán, Air Actions, DeX, đồng bộ/cộng tác, khoá tài liệu (vân tay), định dạng draw.io/Samsung Notes.

## 3. Kiến trúc

```
Kotlin + Compose (UI, quản lý file, cài đặt, panel)
        │  JNI (ranh giới mỏng: sự kiện bút, lệnh, trạng thái)
Rust core ── document model · camera · quadtree · undo/redo
           ├─ shape recognition · Bézier smoothing · mindmap/diagram layout
           ├─ export SVG/PDF · file format (.inkb)
           └─ Skia (GPU, Vulkan/GL) render
ML Kit Digital Ink (offline) ── nhận dạng chữ viết tay vi/en
```

### 3.1 Canvas vô hạn
- Tài liệu là danh sách đối tượng vector trong toạ độ thế giới (f64); camera = (tâm, tỉ lệ). Không có "trang" hay biên.
- **Di chuyển workspace:** pan bằng một/hai ngón, hoặc giữ nút S Pen + kéo; quán tính khi thả; minimap để nhảy nhanh; nút "về nội dung" / "về gốc"; bookmark vị trí.
- Chỉ vẽ phần nhìn thấy: quadtree + cắt vùng ngoài màn hình + LOD (nét nhỏ hơn 0,5 px bị bỏ/gộp khi zoom xa).
- Cache tile theo mức zoom; khi đang pan/zoom hiển thị tile cũ rồi tinh chỉnh dần nên luôn 120 fps.
- Ước lượng bộ nhớ: mỗi nét lưu điểm đã nén (delta + lượng tử hoá); nét hoàn tất được đơn giản hoá (RDP/Bézier fit).

### 3.2 Bút S Pen
- Đường nét ngay lập tức dùng front-buffer / `androidx.graphics` low-latency; dự đoán điểm (motion prediction) để giảm trễ cảm nhận.
- Dữ liệu: vị trí, áp lực, nghiêng, hướng, hover, nút bấm. Palm rejection: chỉ bút vẽ, ngón tay để di chuyển/zoom (có thể đổi).
- Công cụ bút: bút bi, bút máy (áp lực), bút highlight, bút chì (nghiêng), tẩy nét/tẩy điểm, laser pointer.
- Nút bấm bút: chuyển nhanh tẩy / lasso (tuỳ chỉnh).

### 3.3 Nét → chữ / hình / vector
1. **Chữ:** chọn nét (lasso) hoặc chế độ "ink-to-text" tự động; ML Kit Digital Ink (vi, en); kết quả là text box chỉnh sửa được; giữ nét gốc để hoàn tác.
2. **Hình:** giữ bút cuối nét (hoặc bật Auto) → nhận diện đường thẳng, tròn, elip, chữ nhật, tam giác, đa giác, mũi tên, ngôi sao… (thuật toán hình học trong Rust, không cần ML nên nhẹ) và chỉnh đều.
3. **Vector:** làm mượt bằng Bézier; chuyển nét thành path có thể sửa điểm neo; xuất SVG.
4. **Sơ đồ:** hộp + mũi tên nối → node + connector thông minh; kéo node, đường nối đi theo.

### 3.4 Mindmap & sơ đồ chuẩn
- **Mindmap:** node trung tâm, thêm nhánh bằng chạm/viết; tự bố trí (radial / cây ngang / cây dọc); thu gọn/mở rộng; kéo nhánh sang cha khác; xuất outline (văn bản).
- **Sơ đồ:** flowchart, UML (class/sequence/use-case cơ bản), ER, org chart, mạng; connector có bám cổng, snap, đường thẳng/gấp khúc/cong, nhãn; align/distribute/guide.
- **Thư viện shape:** stencil dựng sẵn (nhúng trong app, vector SVG nhẹ) + thư viện cá nhân (lưu nhóm đối tượng, dùng lại) + tìm kiếm.

### 3.5 PDF / ảnh nền
- Nhập PDF làm lớp nền (render bằng PDFium/`PdfRenderer`), vẽ chú thích lên trên, xuất PDF có chú thích (nền giữ vector, nét vector).
- Nhập ảnh: đặt, xoay, co giãn, khoá làm nền.

### 3.6 Xuất / nhập / file
- **Xuất:** PNG (độ phân giải tuỳ chọn, vùng chọn/toàn bộ/khung nhìn), PDF (vector; một trang theo vùng, hoặc chia trang A4/A3), SVG.
- **Định dạng `.inkb`:** container (SQLite hoặc zip) gồm đối tượng vector, ảnh nhúng, thumbnail, phiên bản schema. Tự lưu liên tục (journal) → không mất dữ liệu khi crash/hết pin.
- **Nhập:** ảnh, PDF, SVG.

### 3.7 Các công cụ khác (kiểu CAD)
Layer (ẩn/khoá), nhóm, lasso/chọn khung, snap lưới/đối tượng/góc, thước và đo khoảng cách, undo/redo không giới hạn (lệnh), căn chỉnh, màu & palette tuỳ chỉnh, nền (trơn/lưới/chấm/dòng).

## 4. Offline "tải một lần"

- Toàn bộ tính năng (render, shape, mindmap, thư viện, xuất file) nằm trong APK; không gọi mạng.
- **Lưu ý kỹ thuật cần biết:** ML Kit Digital Ink không cho phép đóng gói model vào APK; model (~20–30 MB mỗi ngôn ngữ) phải tải qua Google Play Services lần đầu. Kế hoạch:
  - Màn hình onboarding: "Tải gói nhận dạng Việt + Anh (một lần, cần mạng)"; sau đó offline mãi mãi, lưu bền vững.
  - Nếu chưa tải, mọi tính năng khác vẫn dùng bình thường; chỉ "nét → chữ" chờ model.
  - Phương án dự phòng (Giai đoạn 3, đánh giá): tự đóng gói model ONNX/TFLite vào APK/asset pack để không phụ thuộc Play Services. Tốn thêm công huấn luyện/tích hợp nên chỉ làm nếu cần.
  - Máy không có Google Play Services: tính năng chuyển chữ bị giới hạn.

## 5. Hiệu năng & chất lượng

- Benchmark từ Giai đoạn 0: 100k nét, 10k đối tượng, zoom/pan 120 fps trên S25 Ultra; bài kiểm thử tự động dạng nhật ký frame time.
- Tối ưu: GPU raster, batching, tile cache, bộ nhớ nén, thread render riêng, không cấp phát khi đang vẽ.
- Kiểm thử: unit test lõi Rust (hình học, undo/redo, serialization), test hồi quy file, kiểm thử UI/thiết bị thật.

## 6. Google Play (miễn phí)

Chính sách quyền riêng tư (không thu thập dữ liệu), mục "Data safety: không thu thập", AAB + App Signing, icon/ảnh store, hỗ trợ màn hình lớn/gập, yêu cầu API target mới nhất. Phiên bản thử nghiệm kín trước khi công khai.

## 7. Lộ trình

| GĐ | Nội dung | Kết quả kiểm chứng |
|---|---|---|
| 0 | Khung dự án (Gradle + cargo-ndk), JNI, Skia, prototype canvas + benchmark | 100k nét mượt 120 fps; độ trễ bút đo được |
| 1 | Canvas vô hạn, pan/zoom, bút S Pen, tẩy, undo/redo, lưu/mở `.inkb` | Vẽ/lưu/mở ổn định |
| 2 | Chọn, shape, text, layer, snap/lưới, xuất PNG/PDF/SVG | Xuất đúng, vector |
| 3 | Nét→chữ (vi/en), nét→hình, nét→vector, nhận diện sơ đồ | Độ chính xác đo trên bộ mẫu |
| 4 | Mindmap, connector thông minh, thư viện shape, thư viện cá nhân | Dựng flowchart/UML/ER |
| 5 | PDF/ảnh nền, template, nhập SVG | Chú thích và xuất PDF |
| 6 | Tối ưu, thử nghiệm đa thiết bị, hoàn thiện, đưa lên Play | Closed test → release |

## 8. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Độ trễ bút trên canvas Skia tự dựng | Front-buffer, prototype ngay GĐ 0 trước khi cam kết |
| Hybrid Rust/Kotlin phức tạp | JNI mỏng, API lệnh/sự kiện rõ ràng, CI build đa kiến trúc (arm64 là chính) |
| ML Kit không bundle được | Onboarding tải một lần; phương án model tự đóng gói |
| Nhận dạng chữ Việt có dấu chưa hoàn hảo | Cho phép sửa nhanh, giữ nét gốc, gợi ý thay thế |
| Phình bộ nhớ khi tài liệu rất lớn | LOD, nén điểm, tải/giải phóng tile theo vùng nhìn |

## 9. Quyết định đã chốt
Native hybrid (Rust+Skia, UI Kotlin/Compose) · Android 10+ · vi/en · Google Play miễn phí · không cloud/đồng bộ v1 · công thức toán, Air Actions, DeX, khoá tài liệu, định dạng ngoài: sau v1 · nhập ảnh/PDF/SVG ở v1.
