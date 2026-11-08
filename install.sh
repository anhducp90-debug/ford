#!/usr/bin/env bash
# Cài đặt VPN Xray (VLESS + XTLS-Reality + Vision) trên Ubuntu 20.04/22.04/24.04
# Dùng với app Happ (và v2rayNG, Hiddify, Streisand... đều được).
#
# Chạy:   sudo bash install.sh
# Tuỳ chọn (biến môi trường):
#   SNI=www.microsoft.com   domain "mồi" cho Reality (phải hỗ trợ TLS1.3 + h2)
#   PORT=443                cổng lắng nghe
#   CLIENT=phone1           tên client đầu tiên
#   SKIP_SNI_CHECK=1        bỏ qua kiểm tra SNI
set -euo pipefail

SNI="${SNI:-www.microsoft.com}"
PORT="${PORT:-443}"
CLIENT="${CLIENT:-client1}"
XRAY_DIR=/usr/local/etc/xray
CONF="$XRAY_DIR/config.json"
ENVF="$XRAY_DIR/reality.env"

log() { printf '\033[1;32m[+]\033[0m %s\n' "$*"; }
err() { printf '\033[1;31m[!]\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || err "Hãy chạy bằng root: sudo bash install.sh"
. /etc/os-release
[[ "${ID:-}" == "ubuntu" || "${ID_LIKE:-}" == *debian* ]] || err "Chỉ hỗ trợ Ubuntu/Debian"
[[ "$PORT" =~ ^[0-9]+$ ]] || err "PORT không hợp lệ"

log "Cài gói phụ thuộc..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq curl jq openssl qrencode ufw ca-certificates >/dev/null

# --- Kiểm tra SNI ---
if [[ "${SKIP_SNI_CHECK:-0}" != 1 ]]; then
  log "Kiểm tra SNI $SNI (TLS1.3 + h2)..."
  if ! echo | timeout 10 openssl s_client -connect "$SNI:443" -servername "$SNI" \
        -tls1_3 -alpn h2 2>/dev/null | grep -q "ALPN protocol: h2"; then
    err "$SNI không hỗ trợ TLS1.3/h2 hoặc không truy cập được. Chọn SNI khác (SNI=...) hoặc SKIP_SNI_CHECK=1"
  fi
fi

# --- Cài Xray (script chính thức của XTLS) ---
if ! command -v xray >/dev/null; then
  log "Cài Xray-core..."
  bash -c "$(curl -fsSL https://github.com/XTLS/Xray-install/raw/main/install-release.sh)" @ install
fi

# --- Sinh khoá ---
log "Sinh UUID / khoá Reality..."
UUID=$(xray uuid)
KEYS=$(xray x25519)
PRIV=$(awk -F': *' 'tolower($1) ~ /private/ {print $2}' <<<"$KEYS" | tr -d ' \r')
PUB=$(awk -F': *' 'tolower($1) ~ /public|password/ {print $2; exit}' <<<"$KEYS" | tr -d ' \r')
[[ -n "$PRIV" && -n "$PUB" ]] || err "Không đọc được khoá x25519: $KEYS"
SID=$(openssl rand -hex 8)

# --- Config ---
log "Ghi cấu hình $CONF"
mkdir -p "$XRAY_DIR"
jq -n --arg uuid "$UUID" --arg email "$CLIENT" --arg sni "$SNI" \
      --arg priv "$PRIV" --arg sid "$SID" --argjson port "$PORT" '
{
  log: {loglevel: "warning"},
  inbounds: [{
    tag: "vless-reality", listen: "0.0.0.0", port: $port, protocol: "vless",
    settings: {clients: [{id: $uuid, email: $email, flow: "xtls-rprx-vision"}], decryption: "none"},
    streamSettings: {
      network: "tcp", security: "reality",
      realitySettings: {show: false, dest: ($sni + ":443"), xver: 0,
        serverNames: [$sni], privateKey: $priv, shortIds: [$sid]}
    },
    sniffing: {enabled: true, destOverride: ["http","tls","quic"], routeOnly: true}
  }],
  outbounds: [{protocol: "freedom", tag: "direct"}, {protocol: "blackhole", tag: "block"}],
  routing: {domainStrategy: "IPIfNonMatch", rules: [
    {type: "field", protocol: ["bittorrent"], outboundTag: "block"},
    {type: "field", ip: ["geoip:private"], outboundTag: "block"}
  ]}
}' > "$CONF"
chown root:nogroup "$CONF"; chmod 640 "$CONF"
xray run -test -config "$CONF" >/dev/null || err "Config không hợp lệ"

IP=$(curl -4 -fsS --max-time 10 https://api.ipify.org || curl -4 -fsS --max-time 10 https://ifconfig.me)
cat > "$ENVF" <<ENV
IP=$IP
PORT=$PORT
SNI=$SNI
PUB=$PUB
SID=$SID
ENV
chmod 600 "$ENVF"

# --- Tối ưu mạng: BBR ---
log "Bật BBR..."
cat > /etc/sysctl.d/99-xray-bbr.conf <<SYS
net.core.default_qdisc=fq
net.ipv4.tcp_congestion_control=bbr
SYS
sysctl --system >/dev/null || true

# --- Firewall ---
SSH_PORT=$(ss -tnlp 2>/dev/null | awk '/sshd/ {n=split($4,a,":"); print a[n]; exit}')
SSH_PORT="${SSH_PORT:-22}"
log "Cấu hình UFW (SSH:$SSH_PORT, VPN:$PORT)..."
ufw allow "$SSH_PORT"/tcp >/dev/null
ufw allow "$PORT"/tcp >/dev/null
ufw --force enable >/dev/null

# --- Công cụ quản lý ---
install -m 755 "$(dirname "$(readlink -f "$0")")/scripts/vpnctl.sh" /usr/local/bin/vpnctl 2>/dev/null \
  || log "Bỏ qua cài vpnctl (không tìm thấy scripts/vpnctl.sh cạnh install.sh)"

systemctl enable xray >/dev/null 2>&1
systemctl restart xray
sleep 1
systemctl is-active --quiet xray || err "Xray không chạy. Xem: journalctl -u xray -e"

LINK="vless://${UUID}@${IP}:${PORT}?encryption=none&flow=xtls-rprx-vision&security=reality&sni=${SNI}&fp=chrome&pbk=${PUB}&sid=${SID}&type=tcp#${CLIENT}"
echo "$LINK" > /root/vpn-link.txt; chmod 600 /root/vpn-link.txt

echo
log "XONG! Link kết nối (cũng lưu ở /root/vpn-link.txt):"
echo "$LINK"
echo
qrencode -t ANSIUTF8 "$LINK"
echo
echo "Happ: nhấn '+' -> 'Import from clipboard' (hoặc quét QR) -> Connect."
echo "Thêm/xoá người dùng: vpnctl add|del|list|link <tên>"
