# InkBoard 1.0.1 — Audit lõi theo kịch bản người dùng (14/11/2026)

## Vì sao audit 1.0.0 bỏ sót lỗi
1. **Test chạy không có lõi nhận diện hình.** Lõi Rust chỉ được build cho ARM64; trên máy build, `InkCore` âm thầm không tải được nên *mọi* luồng "vẽ tay → giữ bút → thành hình" chưa từng được kiểm tra. Nay test tải lõi Rust thật (bản build cho máy chủ) qua JNI.
2. **Test kiểm từng hàm, không kiểm luồng của người dùng.** Ví dụ đường nối chỉ được test giữa hai *khối sơ đồ*, trong khi người dùng nối hai *hình chữ nhật vẽ tay*. Nay có bộ 41 kịch bản thao tác thật trên canvas (`ScenarioAuditTest`).

## Lỗi lõi tìm thấy và đã sửa
| # | Lỗi | Nguyên nhân gốc | Sửa |
|---|---|---|---|
| C1 | **Vẽ đường nối 2 hình chữ nhật không thành connector** | Connector chỉ hỗ trợ khối sơ đồ (Node); hình vẽ tay/thư viện là ShapeEl | Connector nối **mọi đối tượng**: hình kín, khối, ghi chú, ảnh, bảng, chữ, khung vẽ tay khép kín; điểm bám tính theo đúng viền (elip, đa giác, thoi…) |
| C2 | Nét nối bị nắn thẳng (giữ bút) hoặc vẽ thẳng bằng ngón giữ → thành đường kẻ thường | Hai nhánh tạo nét này không đi qua kiểm tra connector | Mọi nhánh (nét tự do, nét được nắn, nét thẳng bằng ngón giữ) đều được xét |
| C3 | Nối bên trong một khung lớn bị nối nhầm vào khung | Chọn đối tượng trên cùng | Chọn đối tượng nhỏ nhất chứa đầu nét; không nối khung với vật nằm trong nó |
| C4 | Gạch để xoá không xoá được đường nối | Chỉ quét phần tử thường | Quét cả connector |
| C5 | Nút "Nối" chỉ hiện khi chọn 2 khối sơ đồ | Điều kiện hẹp | Hai đối tượng bất kỳ có thể nối |
| C6 | Chữ viết chậm (vd. "o") có thể bị biến thành hình tròn | Giữ bút áp dụng cho mọi cỡ nét | Nét nhỏ hơn 48 dp không bao giờ bị nắn |
| C7 | Ngón tay vẽ + đặt ngón thứ hai để zoom để lại nét | Nét đang vẽ không bị huỷ | Ngón thứ hai huỷ nét, chuyển sang zoom |
| C8 | Thêm nhánh mindmap / sửa chữ nút cần 2 lần hoàn tác | Hai thay đổi riêng | Gộp thành một bước |
| C9 | **Nguy cơ hết bộ nhớ** với PDF nhiều trang / nhiều ảnh | Ảnh giải mã đủ độ phân giải, bị giữ trong bộ đệm vẽ | Giải mã theo cỡ đang hiển thị (1/2/4…/64), chạy nền, ngân sách 96 MB, khối ngoài màn hình nhả ảnh |
| C10 | Bản đồ thu nhỏ vẽ lại toàn bộ tài liệu trên luồng giao diện sau mỗi nét | Vẽ đồng bộ | Vẽ ở luồng nền từ bản chụp |
| C11 | Kéo vùng chọn lớn bị giật | Vẽ lại từng phần tử mỗi khung | Ghi một lần, mỗi khung chỉ dịch chuyển |
| C12 | Ảnh thu nhỏ tài liệu vẽ lại mỗi lần tự lưu | — | Chỉ khi rời tài liệu / ứng dụng vào nền |
| C13 | Cập nhật vùng chặn cử chỉ mép ~120 lần/giây | — | Chỉ khi bút dời > 40 dp |
| C14 | `select()` quét toàn bộ tài liệu | — | Dùng danh sách connector đã lưu |

## Bổ sung thiếu sót lõi
Tẩy **một phần** (tách nét) + cỡ tẩy · **Tô màu nền** cho hình/khối/ghi chú · **Đổi chiều** mũi tên · **Sao chép / Dán** giữa các tài liệu.

## Ma trận kịch bản (41/41 đạt)
Vẽ: nét tự do · chữ nhật/tròn/đường thẳng khi giữ bút · chữ nhỏ không bị nắn.
Nối: giữa hình vẽ tay (có/không nắn) · nét được nắn · nét thẳng bằng ngón giữ · khung vẽ tay chưa nắn · hình thư viện · ghi chú↔ảnh · khối↔hình · trong khung lớn · nét ra chỗ trống vẫn là mực · nét trong một hình vẫn là mực · đi theo khi kéo · xoá hình xoá cả nối, hoàn tác khôi phục · một bước hoàn tác · điểm bám đúng viền elip/thoi · nút Nối cho 2 vật bất kỳ · nối khi canvas xoay · đổi chiều.
Chữ & sửa: thả chữ vào hình vẽ tay · thả chữ lên đường nối thành nhãn · chạm đúp đường nối mở sửa nhãn.
Tẩy & chọn: tẩy cả nét xoá connector · gạch xoá connector · tẩy một phần tách nét (hoàn tác 1 bước) · chạm chọn + kéo + hoàn tác · nét viết trong hình đi theo hình · lasso chọn 2 hình + nối · kéo 2.000 nét · trang PDF khoá: chú thích gắn trang, tẩy/lasso không động vào trang.
Khác: lưu/mở giữ connector · SVG/PNG có connector · tô nền · sao chép/dán sang tài liệu khác · ảnh giải mã theo cỡ hiển thị · zoom cực nhỏ/cực lớn · ngón tay vẽ + pinch · thêm nhánh hoàn tác 1 bước.

---

# Phụ lục: Báo cáo audit 1.0.0

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
