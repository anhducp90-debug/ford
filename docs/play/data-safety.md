# Google Play – Câu trả lời mục "An toàn dữ liệu" và các khai báo

## Data safety (An toàn dữ liệu)
| Câu hỏi | Trả lời |
|---|---|
| Ứng dụng có thu thập hoặc chia sẻ dữ liệu người dùng thuộc các loại bắt buộc không? | **Có** – chỉ do SDK của Google (ML Kit) có thể gửi dữ liệu chẩn đoán. Nếu bạn muốn khai thận trọng: chọn *Có*, rồi khai mục bên dưới. |
| Loại dữ liệu | **Thông tin và hiệu suất ứng dụng → Nhật ký sự cố, Chẩn đoán** (do ML Kit); **Mã nhận dạng thiết bị hoặc mã khác** (ML Kit có thể dùng ID cài đặt). |
| Thu thập hay chia sẻ? | Thu thập (bởi SDK), **không chia sẻ** với bên thứ ba khác. |
| Mục đích | Phân tích / chức năng ứng dụng (vận hành và cải thiện SDK). |
| Bắt buộc hay tuỳ chọn? | Bắt buộc khi dùng nhận dạng chữ / quét tài liệu. |
| Dữ liệu có được mã hoá khi truyền? | **Có** (HTTPS của Google Play services). |
| Người dùng có thể yêu cầu xoá dữ liệu? | Dữ liệu bản vẽ: có (xoá trong app / gỡ app). Dữ liệu SDK: theo chính sách của Google. |
| Bản vẽ, ảnh, PDF | **Không thu thập** (chỉ lưu trên thiết bị). |
| Vị trí, danh bạ, tài khoản, tài chính, sức khoẻ, tin nhắn, ảnh/video | **Không thu thập.** |

> Ghi chú: hướng dẫn chính thức của ML Kit về Data safety: https://developers.google.com/ml-kit/android-data-disclosure — hãy đối chiếu bảng trên với trang này tại thời điểm nộp.

## Các khai báo khác trong Play Console
- **Quảng cáo:** Không chứa quảng cáo.
- **Quyền truy cập ứng dụng:** Toàn bộ chức năng dùng được không cần đăng nhập.
- **Đối tượng mục tiêu:** 13+ (hoặc 18+). Không nhắm trẻ em.
- **Phân loại nội dung (IARC):** Ứng dụng tiện ích/năng suất; không bạo lực, không nội dung người dùng chia sẻ công khai, không mua hàng → thường ra "Mọi người / PEGI 3".
- **Ứng dụng tin tức / chính phủ / tài chính / y tế:** Không.
- **Quyền nhạy cảm:** Không dùng (không có quyền vị trí, danh bạ, SMS, ảnh – ảnh/PDF chọn qua trình chọn hệ thống).
- **Dịch vụ nền trước (foreground service):** ứng dụng không tự khai báo; quyền FOREGROUND_SERVICE đến từ thư viện Google. Nếu Console hỏi loại dịch vụ, trả lời theo mô tả của thư viện (tải mô hình ML).
