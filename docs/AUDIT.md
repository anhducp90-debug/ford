# InkBoard 0.4.0 — Báo cáo audit (14/11/2026)

Phạm vi: toàn bộ mã nguồn nhánh `ccr-acb2d1e8-5fdjtx` (commit `1a21cf0`), APK `InkBoard-0.4.0.apk`, đối chiếu với `docs/DESIGN.md`.

## 1. Kết luận

- **Dùng thử cá nhân trên S25 Ultra: sẵn sàng**, với điều kiện chấp nhận rằng chưa có lần chạy nào trên máy thật.
- **Đưa lên Google Play: chưa sẵn sàng.** Có 2 điểm chặn (mục 3, P0) và một số việc bắt buộc về chính sách.
- So với phạm vi v1 trong thiết kế: **22 hạng mục xong, 9 làm một phần, 9 chưa làm** (mục 5).

## 2. Đã kiểm chứng

| Hạng mục | Kết quả |
|---|---|
| Test Kotlin (mô hình, cử chỉ canvas, quy tắc kéo thả, phác thảo, giao diện đầu-cuối) | 66/66 đạt |
| Test Rust (nhận diện hình) | 13/13 đạt |
| Build release có R8 + chữ ký | Đạt, 11,8 MB (mục tiêu ≤ 40 MB) |
| Thư viện native căn 16 KB (yêu cầu Play cho Android 15+) | Đạt cả 3 file `.so` |
| Android Lint (release) | 3 lỗi (cùng một nguyên nhân, mục 3), 39 cảnh báo nhỏ |
| Mã nguồn | 5.803 dòng Kotlin app, 1.328 dòng test, 667 dòng Rust; không còn TODO/FIXME |

**Chưa kiểm chứng được (không có máy thật / máy ảo):** độ trễ bút, chống chạm lòng bàn tay với S Pen thật, cảm biến nghiêng của bút, tốc độ khung hình, nhận dạng chữ tiếng Việt (ML Kit không chạy trong môi trường test), tải gói nhận dạng, bản release sau khi R8 rút gọn chạy trên thiết bị, chia đôi màn hình.

## 3. Vấn đề phát hiện

### P0 — chặn phát hành
1. **targetSdk 35 không còn được Google Play chấp nhận.** Từ 31/8/2026, app mới phải nhắm API 36 (Android 16). Cần nâng `compileSdk`/`targetSdk` lên 36 và thử lại (Android 16 bắt buộc tràn viền, đổi hành vi nút quay lại).
2. **Crash trên Android 10 (API 29).** `InkCanvasView.insets()` gọi `WindowInsets.getInsets()` (chỉ có từ API 30) trong mỗi lần vẽ, trong khi `minSdk = 29`. Không ảnh hưởng S25 Ultra. Sửa: dùng `WindowInsetsCompat` hoặc nâng `minSdk` lên 30.

### P1 — ảnh hưởng trải nghiệm hoặc dữ liệu
3. **Hút màu dễ kích hoạt nhầm.** Giữ yên một ngón ~0,45 s ở bất kỳ đâu là đổi màu bút ngay, kể cả trên nền trống: bút nhận màu nền và nét vẽ sau đó gần như vô hình. Sửa: chỉ áp dụng khi nhấc tay, bỏ qua nền trống, kéo dài thời gian giữ.
4. **Tự lưu ghi lại toàn bộ file mỗi lần.** Đo trên máy build: 10.000 nét = 2,4 MB; 100.000 nét = 22 MB, ghi mất ~4,9 s (điện thoại có thể chậm hơn), lặp lại 1,2 s sau mỗi thay đổi. Tài liệu lớn sẽ hao pin, nóng máy. Thiết kế ghi "journal" nhưng chưa làm.
5. **Chưa có tối ưu cho tài liệu lớn như thiết kế cam kết.** Không có quadtree, LOD, cache tile; đường vẽ tăng tốc phần cứng phát lại *mọi* phần tử mỗi khung hình, không cắt vùng ngoài màn hình. Mục tiêu "100k nét, 120 fps" chưa có cơ sở.
6. **Chưa có nét vẽ độ trễ thấp.** Thiết kế cam kết front-buffer + dự đoán điểm (≤ 10 ms); hiện chỉ có `requestUnbufferedDispatch`.
7. **Chèn ảnh giải mã trên luồng giao diện** → có thể khựng/ANR với ảnh lớn.
8. **"Xoá vĩnh viễn" trong thùng rác không hỏi xác nhận.**
9. **Không có sao lưu/khôi phục**; gỡ app là mất dữ liệu. Đồng thời `allowBackup="true"` cho phép Android tự sao lưu lên Google Drive của người dùng (tối đa 25 MB) — cần quyết định rõ vì thông điệp là "dữ liệu chỉ nằm trên máy".
10. **Chưa chặn cử chỉ "quay lại" của hệ thống khi viết sát mép** (lỗi người dùng Samsung Notes phàn nàn nhiều).

### P2 — chính sách và hoàn thiện
11. **Tuyên bố quyền riêng tư cần chỉnh.** ML Kit kéo theo các quyền `INTERNET`, `ACCESS_NETWORK_STATE`, `WAKE_LOCK`, `RECEIVE_BOOT_COMPLETED`, `FOREGROUND_SERVICE` và có thể gửi số liệu sử dụng về Google. Câu "không thu thập dữ liệu" trong app và mục Data safety phải khai đúng điều này; chưa có trang chính sách quyền riêng tư.
12. **Khoá ký nằm trong repo** (`app/inkboard-sideload.jks`, mật khẩu trong `build.gradle.kts`). Chỉ hợp cho cài thử; bản Play cần khoá upload riêng.
13. **Giao diện chỉ có tiếng Việt**, chuỗi viết cứng trong code (thiết kế: Việt + Anh).
14. **Số phiên bản trong Cài đặt ghi "InkBoard 0.1"** (thực tế 0.4.0).
15. **Rủi ro luồng nhỏ:** ảnh thu nhỏ được vẽ ở luồng nền trên cùng các phần tử mà giao diện đang vẽ; bộ nhớ đệm ô bảng (`TableEl.cellFits`) không an toàn đa luồng.
16. Khi canvas đang xoay, vùng tự cuộn lúc kéo ra mép tính theo toạ độ chưa xoay (lệch hướng).
17. Chế độ "ngón tay cũng vẽ": chạm ngón thứ hai để zoom để lại một chấm mực.
18. Toạ độ dùng số thực 32-bit thay vì 64-bit như thiết kế (đủ dùng tới khoảng ±1 triệu đơn vị).

## 4. Số đo hiệu năng (JVM máy build, không phải điện thoại)

| Thao tác | 10.000 nét | 100.000 nét |
|---|---|---|
| Kích thước file | 2,4 MB | 22 MB |
| Ghi file (tự lưu) | 567 ms* | 4.899 ms |
| Mở file | 64 ms | 507 ms |
| Chụp nhanh để lưu (luồng giao diện) | 0,1 ms | 0,4 ms |
| Tìm phần tử dưới điểm chạm | 0,4 ms | 2,6 ms |
| Tính gợi ý thả khi kéo | 0,04 ms | 0,16 ms |
| Dựng bảng Thành phần | 27 ms | 103 ms |
| Xoá 1 phần tử | 10,8 ms* | 23 ms |

\* gồm thời gian khởi động JIT của lần chạy đầu.

Nhận xét: thao tác tương tác (chọn, kéo, thêm nét) vẫn nhanh ở 100k nét; điểm nghẽn là **ghi file** và (chưa đo được) **vẽ khung hình**.

## 5. Đối chiếu phạm vi v1 trong thiết kế

**Xong (22):** canvas vô hạn + pan/zoom; bút theo lực nhấn (bút, bút nhấn, bút chì); tẩy nét; lasso chọn/kéo/phóng/nhân đôi/xoá; hoàn tác không giới hạn; đa chạm (kéo, zoom, chạm 2–3 ngón, ngón giữ làm phím bổ trợ, nút bên S Pen); giữ bút thành hình (lõi Rust); nét → chữ (ML Kit vi/en); mindmap tự bố trí; khối flowchart + đường nối; chữ, ghi chú dán, ảnh, bảng; thư mục + tìm tên + thùng rác; tự lưu; xuất PNG/PDF; nền trơn/chấm/lưới/dòng; chế độ tối; onboarding; thước, dòng toạ độ, bản đồ thu nhỏ, bảng Thành phần; quy tắc kéo thả thông minh; gói phác thảo (chống rung, lực nhấn, màu HSV, hút màu, xoay/lật, đối xứng, phối cảnh, ảnh tham khảo).

**Một phần (9):** thư viện hình (6 hình + 3 khối, thiết kế ~100 hình có tìm kiếm); mindmap (chưa thu gọn/mở rộng nhánh); đường nối (chưa bám cổng, chưa nhãn, chưa kéo đầu mũi tên); biến đổi vùng chọn (chưa xoay đối tượng); bắt dính (chưa có lưới/đường gióng); độ trễ thấp; hiệu năng tài liệu lớn; ngôn ngữ giao diện; lưu an toàn (ghi nguyên tử có, journal chưa).

**Chưa làm (9):** chú thích PDF; quét tài liệu; xuất SVG; gạch để xoá (scratch-out); thư viện cá nhân; nhận diện sơ đồ từ nét tay (hộp + mũi tên → khối + đường nối); bookmark vị trí; laser pointer; tuỳ chỉnh nút bút. (Tìm kiếm chữ viết tay thuộc v1.5.)

## 6. Thứ tự xử lý đề xuất

1. Sửa nhanh, rủi ro thấp: #2 (insets), #3 (hút màu), #7, #8, #14.
2. Nâng targetSdk 36 (#1) và thử lại toàn bộ.
3. **Thử trên S25 Ultra** — quyết định mức ưu tiên của #5, #6 dựa trên cảm giác thật.
4. Lưu gia tăng (#4), sao lưu/khôi phục (#9), chặn cử chỉ quay lại (#10).
5. Chính sách Play: quyền riêng tư (#11), khoá ký (#12), tiếng Anh (#13).
6. Các hạng mục thiết kế còn thiếu theo ưu tiên P1 đã chốt (đường gióng, nối bằng nét tay, scratch-out…).

Số liệu hiệu năng tái tạo bằng: `PERF_AUDIT=1 ./gradlew :app:testDebugUnitTest --tests '*PerfAuditTest*' -i`.
