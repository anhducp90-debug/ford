# Hướng dẫn đưa InkBoard lên Google Play

## Tệp cần dùng (trong `dist/`, không đưa lên git)
| Tệp | Dùng để |
|---|---|
| `InkBoard-1.0.0.aab` | **Tải lên Google Play** (đã ký bằng khoá upload) |
| `InkBoard-1.0.0.apk` | Cài thử trực tiếp trên máy (ký bằng khoá sideload) |
| `play-signing/inkboard-upload.jks` + `keystore.properties` | **Khoá upload** – cất giữ an toàn (password manager + bản sao ngoại tuyến). Mất khoá vẫn có thể xin Google đặt lại khoá upload, nhưng mất thời gian. |
| `play-assets/icon-512.png` | Biểu tượng cửa hàng 512×512 |
| `play-assets/feature-graphic-1024x500.png` | Ảnh nổi bật 1024×500 |
| `play-assets/screenshot-*.png` | Ảnh chụp màn hình điện thoại (1080×2400) |

## Các bước
1. **Tạo tài khoản nhà phát triển** tại play.google.com/console (phí đăng ký một lần; cần xác minh danh tính).
   - Tài khoản *cá nhân* mới: Google yêu cầu **thử nghiệm kín với ít nhất 12 người trong 14 ngày liên tục** trước khi được phát hành chính thức. Chuẩn bị danh sách email người thử.
2. **Tạo ứng dụng**: tên "InkBoard", ngôn ngữ mặc định Tiếng Việt, loại *Ứng dụng*, *Miễn phí*.
3. **Thiết lập ứng dụng** (mục Policy/App content): điền theo `docs/play/data-safety.md`; dán URL chính sách quyền riêng tư (xem bước 4).
4. **Đăng chính sách quyền riêng tư** ở một URL công khai, ví dụ: bật GitHub Pages cho repo (nếu repo công khai) với tệp `docs/play/privacy-policy.html`, hoặc dán nội dung vào Google Sites. Điền email hỗ trợ vào tệp trước khi đăng.
5. **Trang cửa hàng chính**: chép tên, mô tả ngắn, mô tả đầy đủ từ `docs/play/store-listing.md`; tải icon, ảnh nổi bật, ≥ 2 ảnh chụp màn hình. (Nên chụp thêm ảnh thật trên S25 Ultra/Tab.)
6. **Play App Signing**: khi tạo bản phát hành đầu tiên, chọn để Google quản lý khoá ký ứng dụng (mặc định). Tệp `.aab` đã ký bằng khoá upload.
7. **Thử nghiệm kín (Closed testing)** → tạo track → tải `InkBoard-1.0.0.aab` → thêm người thử → phát hành. Thu thập phản hồi 14 ngày.
8. **Phát hành chính thức (Production)**: sau khi đủ điều kiện, nâng bản lên Production, chọn quốc gia (Việt Nam + các nước khác), gửi xét duyệt (thường vài ngày).

## Build lại bản mới
```bash
cp dist/play-signing/keystore.properties ./keystore.properties   # (đường dẫn storeFile tương đối từ thư mục app/)
# tăng versionCode / versionName trong app/build.gradle.kts
./gradlew :app:bundleRelease     # → app/build/outputs/bundle/release/app-release.aab
./gradlew :app:assembleRelease   # → APK
```
Nếu không có `keystore.properties`, bản release sẽ ký bằng khoá sideload (chỉ để cài thử, **không** dùng cho Play).

## Lưu ý
- Bản cài thử (APK ký sideload) và bản từ Google Play có chữ ký khác nhau: muốn chuyển sang bản Play phải **gỡ bản cài thử** (hãy *Sao lưu tất cả* trước).
- Yêu cầu hiện hành: targetSdk 36 (Android 16) – đã đáp ứng; thư viện native căn 16 KB – đã đáp ứng.
