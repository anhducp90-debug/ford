# VPN vượt DPI (Nga) – Xray VLESS + Reality + Vision

Server VPN trên VPS Ubuntu, dùng **Xray-core** với **VLESS + XTLS-Reality + `xtls-rprx-vision`**.
Lưu lượng trông như TLS 1.3 thật tới một website hợp lệ (SNI mồi), nên rất khó bị DPI của
Tele2, Beeline, MTS, MegaFon… nhận diện. Client: **Happ** (iOS/Android/Windows/macOS).

## Cài đặt

```bash
git clone <repo> && cd ford
sudo bash install.sh
# tuỳ chọn:
# sudo SNI=www.samsung.com CLIENT=phone1 bash install.sh
```

Script sẽ: cài Xray, sinh UUID + khoá Reality, bật BBR, mở firewall (SSH + 443), chạy dịch vụ
và in ra **link `vless://…` + QR**. Link cũng lưu ở `/root/vpn-link.txt`.

## Kết nối bằng Happ

1. Mở Happ → nhấn **+** → *Import from clipboard* (copy link vless) hoặc quét QR.
2. Chọn profile → **Connect**.

## Quản lý người dùng

```bash
vpnctl add  ten_moi     # thêm client, in link + QR
vpnctl list
vpnctl link ten_moi
vpnctl del  ten_moi
```

## Bot Telegram cấp key + QR

1. Tạo bot với [@BotFather](https://t.me/BotFather) → lấy `BOT_TOKEN`. Lấy Telegram ID của bạn từ @userinfobot.
2. Trên VPS (sau `install.sh`):
   ```bash
   sudo BOT_TOKEN=123456:ABC... ADMIN_IDS=111111111 bash install-bot.sh
   ```
3. Dùng:
   - Người dùng gửi `/start` → admin nhận nút **Duyệt / Từ chối**.
   - Người được duyệt gửi `/key` → bot trả **QR + link** (mỗi người 1 key, tên `tg<id>`).
   - Admin: `/users`, `/allow <id>`, `/revoke <id>` (xoá quyền và key).

Bot chạy bằng user `vpnbot` (không phải root), chỉ được `sudo` đúng lệnh `/usr/local/bin/vpnctl`.
Token lưu ở `/etc/vpnbot.env` (quyền 640).

## Lưu ý quan trọng khi dùng tại Nga

- **VPS đặt ngoài Nga** (Đức, Hà Lan, Phần Lan, Thuỵ Điển… độ trễ thấp từ Nga). IP/nhà cung cấp
  bị chặn thì đổi nhà cung cấp hoặc đổi IP.
- **Chọn SNI** là site hỗ trợ TLS 1.3 + h2, **không bị chặn ở Nga**, lý tưởng là cùng nhà cung cấp/ASN
  hoặc cùng quốc gia với VPS. Nếu bị chặn, đổi `SNI` rồi cài lại (hoặc sửa `serverNames`/`dest` trong
  `/usr/local/etc/xray/config.json` và `reality.env`).
- **Mạng di động có "whitelist"**: khi nhà mạng bật chế độ chỉ cho IP/domain nằm trong danh sách trắng,
  mọi VPS thông thường đều bị chặn bất kể giao thức. Cách phổ biến là dùng IP của VPS thuộc các
  ASN/CDN nằm trong whitelist hoặc chuỗi qua một node trung gian – không có cách chắc chắn 100%.
- Giữ cổng 443/TCP; tránh chia sẻ link công khai (mỗi người một UUID để dễ thu hồi).
- Kiểm tra: `systemctl status xray`, `journalctl -u xray -e`.

## Cấu trúc

- `install.sh` – cài đặt toàn bộ
- `install-bot.sh`, `bot/` – bot Telegram (python-telegram-bot)
- `scripts/vpnctl.sh` – quản lý client (được cài thành `/usr/local/bin/vpnctl`)
