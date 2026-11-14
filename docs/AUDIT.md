# InkBoard 1.0.0 — Báo cáo audit (14/11/2026)

Phiên bản trước: [audit 0.4.0](#phụ-lục-đối-chiếu-với-audit-040). Phạm vi: toàn bộ mã nguồn nhánh `ccr-acb2d1e8-5fdjtx`, `InkBoard-1.0.0.aab` (Play) và `InkBoard-1.0.0.apk` (cài thử).

## 1. Kết luận

- **Kỹ thuật: sẵn sàng nộp Google Play.** Đáp ứng targetSdk 36, căn 16 KB, AAB ký bằng khoá upload, lint 0 lỗi, 99 test tự động đạt.
- **Còn phải do bạn làm:** tạo tài khoản Play Console, đăng chính sách quyền riêng tư ở URL công khai (điền email), thử nghiệm kín 12 người × 14 ngày (bắt buộc với tài khoản cá nhân mới), và **thử trên S25 Ultra thật**.
- Chưa kiểm chứng trên máy thật: độ trễ bút, chống chạm lòng bàn tay, khung hình/giây, nhận dạng chữ và máy quét (cần Google Play services).

## 2. Kiểm chứng

| Hạng mục | Kết quả |
|---|---|
| Test Kotlin (mô hình, canvas, kéo thả, phác thảo, tính năng mới, giao diện đầu-cuối) | **86/86** đạt |
| Test Rust (nhận diện hình) | **13/13** đạt |
| Android Lint (release) | **0 lỗi** (0.4.0: 3 lỗi), 52 cảnh báo nhỏ (kiểu viết KTX, phiên bản thư viện) |
| targetSdk / compileSdk | **36** (yêu cầu Play từ 31/8/2026) |
| Thư viện native căn 16 KB | Đạt |
| AAB | 8,9 MB, ký `CN=InkBoard Upload` |
| APK cài thử | 12,1 MB, ký sideload (cài đè bản 0.x) |
| Mã nguồn | 7.425 dòng Kotlin app, 1.885 dòng test, 667 dòng Rust |

## 3. Hiệu năng (JVM máy build)

| Thao tác | 0.4.0 | 1.0.0 |
|---|---|---|
| Tự lưu sau mỗi thay đổi, 100k nét | 4.899 ms | **275 ms** (cache mã hoá từng phần tử + nén nhanh) |
| Kích thước file 100k nét | 22 MB | **10 MB** (định dạng v4 nén toạ độ) |
| Tìm phần tử dưới điểm chạm, 100k nét | 2,6 ms | **~0,9 ms** (chỉ mục lưới không gian) |
| Tẩy / lasso | quét toàn bộ | chỉ quét vùng lân cận |
| Vẽ khung hình | tạo khoá cho mọi phần tử mỗi khung, vẽ cả phần ngoài màn hình | khoá chỉ dựng lại khi có thay đổi; bỏ qua khối ngoài màn hình; ghi khối khi lần đầu hiện ra |
| Nét đang vẽ | — | thêm dự đoán chuyển động (androidx motion prediction) |

## 4. Xử lý các vấn đề của audit 0.4.0

| # | Vấn đề | Trạng thái |
|---|---|---|
| 1 | targetSdk 35 | ✅ Nâng 36 (AGP 8.13.2), predictive back |
| 2 | Crash Android 10 (API 30 call) | ✅ `WindowInsetsCompat` |
| 3 | Hút màu kích hoạt nhầm | ✅ Chỉ trên nội dung đã vẽ, giữ 0,6 s, áp dụng khi nhấc tay |
| 4 | Tự lưu ghi cả file | ✅ Nhanh ~18× (xem mục 3); journal không còn cần thiết |
| 5 | Chưa tối ưu tài liệu lớn | ✅ Chỉ mục không gian, cắt khối ngoài màn hình, ghi lười |
| 6 | Chưa có nét độ trễ thấp | ⚠️ Có dự đoán chuyển động; **chưa** dùng front-buffer (cần chuyển canvas sang SurfaceView — để sau khi đo trên máy thật) |
| 7 | Chèn ảnh trên luồng UI | ✅ Giải mã/nén trên luồng nền |
| 8 | Xoá vĩnh viễn không hỏi | ✅ Hộp thoại xác nhận |
| 9 | Không sao lưu / `allowBackup` | ✅ Sao lưu tất cả (.zip), khôi phục, mở/chia sẻ .inkb; quy tắc sao lưu Android tường minh (chỉ tài liệu + cài đặt) |
| 10 | Cử chỉ "quay lại" khi viết sát mép | ✅ Loại trừ dải mép quanh vị trí bút |
| 11 | Tuyên bố quyền riêng tư | ✅ Sửa trong app; chính sách quyền riêng tư + câu trả lời Data safety trong `docs/play/` |
| 12 | Khoá ký trong repo | ✅ Khoá upload mới ngoài repo (`dist/play-signing/`, bị `.gitignore`); khoá sideload chỉ dùng cài thử |
| 13 | Chỉ tiếng Việt | ⏳ Giữ tiếng Việt cho 1.0 (thị trường chính); đa ngôn ngữ ở 1.1 |
| 14 | Số phiên bản sai | ✅ Đọc từ gói cài đặt |
| 15 | Rủi ro đa luồng | ✅ Cache ô bảng an toàn đa luồng; lưu cùng tài liệu được tuần tự hoá |
| 16 | Tự cuộn khi canvas xoay | ⏳ Còn lệch hướng nhẹ khi đang xoay (hiếm gặp) |
| 17 | Chấm mực khi chạm 2 ngón ở chế độ "ngón tay vẽ" | ⏳ Còn |
| 18 | Toạ độ 32-bit | ⏳ Giữ (đủ ±1 triệu đơn vị); lưu file lượng tử 1/64 đơn vị |

**Phát hiện mới trong lần audit này (đã sửa):** thanh công cụ tràn khỏi màn hình điện thoại và che nút Quay lại (thấy được nhờ ảnh chụp màn hình tự động); lưới tài liệu chỉ 1 cột trên điện thoại.

## 5. Tính năng còn thiếu của thiết kế v1 — đã bổ sung

✅ Chú thích PDF (trang được khoá) · ✅ Quét tài liệu (ML Kit) · ✅ Xuất SVG · ✅ Gạch để xoá · ✅ Thư viện hình (55 hình, tìm không dấu) + thư viện của tôi · ✅ Nét tay nối hai khối → mũi tên · ✅ Nhãn đường nối · ✅ Thu gọn nhánh mindmap · ✅ Chuyển hình vẽ → khối sơ đồ · ✅ Chữ viết tay trong khung → chữ của khung · ✅ Ảnh nhận nét chú thích gắn kèm · ✅ Khoá phần tử · ✅ Đường gióng khi kéo · ✅ Đánh dấu vị trí · ✅ Bút laser · ✅ Tuỳ chỉnh nút bên S Pen · ✅ Xuất vùng chọn chỉ gồm phần đã chọn.

**Chưa làm (để 1.1):** xoay từng đối tượng · cổng nối cố định trên khối · giao diện tiếng Anh · front-buffer rendering · tìm kiếm chữ viết tay (v1.5 theo kế hoạch).

## 6. Gói Google Play

| Mục | Tệp |
|---|---|
| Bundle tải lên | `dist/InkBoard-1.0.0.aab` |
| Khoá upload | `dist/play-signing/` (giữ bí mật) |
| Icon 512, ảnh nổi bật 1024×500, 4 ảnh chụp màn hình | `dist/play-assets/` |
| Tên, mô tả (vi + en), ghi chú phát hành | `docs/play/store-listing.md` |
| Chính sách quyền riêng tư (vi + en) | `docs/play/privacy-policy.html` |
| Data safety, phân loại nội dung, khai báo | `docs/play/data-safety.md` |
| Hướng dẫn từng bước | `docs/play/RELEASE_GUIDE.md` |

## Phụ lục: đối chiếu với audit 0.4.0
Báo cáo 0.4.0 nằm trong lịch sử git (commit `Add audit report for 0.4.0`).
