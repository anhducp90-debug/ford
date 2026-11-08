#!/usr/bin/env bash
# Cài Telegram bot cấp key. Chạy SAU install.sh:
#   sudo BOT_TOKEN=123:abc ADMIN_IDS=111,222 bash install-bot.sh
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "Cần root" >&2; exit 1; }
: "${BOT_TOKEN:?Thiếu BOT_TOKEN (lấy từ @BotFather)}"
: "${ADMIN_IDS:?Thiếu ADMIN_IDS (Telegram ID của bạn, hỏi @userinfobot)}"
[[ -x /usr/local/bin/vpnctl ]] || { echo "Chưa có vpnctl, hãy chạy install.sh trước" >&2; exit 1; }
HERE=$(dirname "$(readlink -f "$0")")

apt-get install -y -qq python3 python3-venv sudo >/dev/null
id vpnbot &>/dev/null || useradd --system --home /opt/vpnbot --shell /usr/sbin/nologin vpnbot
mkdir -p /opt/vpnbot /var/lib/vpnbot
install -m 644 "$HERE/bot/bot.py" /opt/vpnbot/bot.py
python3 -m venv /opt/vpnbot/venv
/opt/vpnbot/venv/bin/pip install -q -r "$HERE/bot/requirements.txt"
chown -R vpnbot:vpnbot /var/lib/vpnbot

# chỉ cho phép đúng lệnh vpnctl (không shell)
echo 'vpnbot ALL=(root) NOPASSWD: /usr/local/bin/vpnctl' > /etc/sudoers.d/vpnbot
chmod 440 /etc/sudoers.d/vpnbot
visudo -cf /etc/sudoers.d/vpnbot >/dev/null

umask 077
printf 'BOT_TOKEN=%s\nADMIN_IDS=%s\n' "$BOT_TOKEN" "$ADMIN_IDS" > /etc/vpnbot.env
chown root:vpnbot /etc/vpnbot.env; chmod 640 /etc/vpnbot.env

install -m 644 "$HERE/bot/vpnbot.service" /etc/systemd/system/vpnbot.service
systemctl daemon-reload
systemctl enable --now vpnbot
sleep 2
systemctl is-active --quiet vpnbot && echo "Bot đang chạy. Mở bot trên Telegram và gửi /start" \
  || { echo "Lỗi, xem: journalctl -u vpnbot -e" >&2; exit 1; }
