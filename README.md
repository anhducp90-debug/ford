# InkBoard

Không gian sáng tạo vô hạn cho S Pen: phác thảo, sơ đồ, mindmap, nét vẽ → chữ / hình. Offline, không tài khoản.

Thiết kế: [docs/DESIGN.md](docs/DESIGN.md)

## Bản 0.4 có gì
- **Gói phác thảo (mới):** chống rung nét, đường cong lực nhấn, thon nét, độ đậm, bút chì nghiêng tô bóng; bảng màu HSV + màu gần đây + giữ ngón tay để hút màu; xoay canvas bằng 2 ngón, lật ngang; đối xứng (dọc/ngang/4 phía/toả), phối cảnh 1–2 điểm tụ có bắt nét; ảnh tham khảo nổi. Chi tiết: docs/DESIGN.md §2.1d.

### Từ bản 0.3
- **Logic kết hợp (mới):** kéo chữ vào hình → chữ nằm gọn, tự vừa hình; chữ vào khối / ghi chú / ô bảng; chữ hoặc ghi chú lên nút mindmap → nhánh con; kéo nhánh sang nút khác → chuyển nhánh; nét vẽ, hình, ảnh thả vào hình / khối / ghi chú / bảng → gắn vào và đi theo; kéo ra ngoài → tách. Gợi ý "Thả để…" hiện khi kéo; một bước hoàn tác. Chi tiết: docs/DESIGN.md §2.1c.

### Từ bản 0.2
- **Định vị (mới):** thước toạ độ X/Y kiểu CAD (Y dương hướng lên), dòng toạ độ bút / vùng chọn (X, Y, rộng × cao), bản đồ thu nhỏ (chạm/kéo để nhảy tới), bảng **Thành phần** liệt kê mindmap, khối, chữ, ghi chú, bảng, ảnh, hình, cụm nét vẽ kèm toạ độ — chạm để tới đó. Bật/tắt trong Menu.

### Từ bản 0.1
- Canvas vô hạn (pan/zoom 0,1%–10.000%), nền trơn/chấm/lưới/dòng
- Bút S Pen theo lực nhấn: bút, bút nhấn, bút chì; 8 màu, 4 độ dày; tẩy nét
- Đa chạm: 1 ngón kéo, 2 ngón zoom, chạm 2 ngón = hoàn tác, 3 ngón = làm lại, nút bên S Pen + kéo = di chuyển
- Giữ bút cuối nét → thành hình chuẩn (lõi Rust); giữ thêm 1 ngón → đường thẳng / tròn / vuông chuẩn
- Lasso chọn, kéo thả (tự cuộn ở mép), phóng to bằng góc, nhân đôi, xoá, lên trên/xuống dưới, đổi màu
- Nét viết tay → chữ (ML Kit, Tiếng Việt / English, tải gói một lần rồi offline)
- Ghi chú dán, chữ, ảnh, bảng, hình, mũi tên, khối flowchart + nối, mindmap tự bố trí
- Màn hình chính: lưới tài liệu, tìm kiếm, thư mục, thùng rác 30 ngày; tự lưu
- Xuất PNG / PDF (vector) – chia sẻ hoặc lưu vào Tải xuống/InkBoard

Chưa có (bản sau): chú thích PDF, quét tài liệu, xuất SVG, tìm kiếm chữ viết tay.

## Cài trên điện thoại
Tải `InkBoard-0.4.0.apk`, mở và cho phép "Cài ứng dụng không rõ nguồn gốc". APK chỉ dành cho máy ARM64 (Galaxy S25 Ultra…), Android 10+.

## Build
Yêu cầu: JDK 17+, Android SDK 35, NDK 27.2.12479018, Rust + `rustup target add aarch64-linux-android` + `cargo install cargo-ndk`.

```bash
./gradlew :app:assembleRelease      # tự build lõi Rust (core/) rồi đóng gói APK
./gradlew :app:testDebugUnitTest    # test mô hình, cử chỉ canvas, UI (Robolectric)
(cd core && cargo test)             # test nhận diện hình
```

`app/inkboard-sideload.jks` là khoá ký **chỉ để cài thử** (cho phép cài bản mới đè bản cũ). Bản Google Play sẽ dùng khoá upload riêng, không đưa vào repo.
