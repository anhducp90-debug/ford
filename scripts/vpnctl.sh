#!/usr/bin/env bash
# Quản lý client Xray Reality: vpnctl add|del|list|link <tên>
set -euo pipefail
CONF=/usr/local/etc/xray/config.json
ENVF=/usr/local/etc/xray/reality.env
[[ $EUID -eq 0 ]] || { echo "Cần root" >&2; exit 1; }
# shellcheck disable=SC1090
. "$ENVF"

link() { # $1=uuid $2=name
  echo "vless://$1@${IP}:${PORT}?encryption=none&flow=xtls-rprx-vision&security=reality&sni=${SNI}&fp=chrome&pbk=${PUB}&sid=${SID}&type=tcp#$2"
}
save() { # giữ nguyên owner/mode của config
  cat > "$CONF" && xray run -test -config "$CONF" >/dev/null && systemctl restart xray
}

if [[ -n "${2:-}" && ! "$2" =~ ^[A-Za-z0-9_-]{1,32}$ ]]; then
  echo "Tên chỉ gồm chữ/số/_/- (tối đa 32 ký tự)" >&2; exit 1
fi

case "${1:-}" in
  list)
    jq -r '.inbounds[0].settings.clients[] | "\(.email)\t\(.id)"' "$CONF" ;;
  link)
    name="${2:?thiếu tên}"
    id=$(jq -r --arg n "$name" '.inbounds[0].settings.clients[]|select(.email==$n).id' "$CONF")
    [[ -n "$id" ]] || { echo "Không có client $name" >&2; exit 1; }
    l=$(link "$id" "$name"); echo "$l"; qrencode -t ANSIUTF8 "$l" ;;
  add)
    name="${2:?thiếu tên}"
    jq -e --arg n "$name" '.inbounds[0].settings.clients[]|select(.email==$n)' "$CONF" >/dev/null && { echo "Đã tồn tại" >&2; exit 1; }
    id=$(xray uuid)
    jq --arg n "$name" --arg id "$id" \
      '.inbounds[0].settings.clients += [{id:$id,email:$n,flow:"xtls-rprx-vision"}]' "$CONF" | save
    l=$(link "$id" "$name"); echo "$l"; qrencode -t ANSIUTF8 "$l" ;;
  del)
    name="${2:?thiếu tên}"
    jq --arg n "$name" '.inbounds[0].settings.clients |= map(select(.email!=$n))' "$CONF" | save
    echo "Đã xoá $name" ;;
  *) echo "Dùng: vpnctl add|del|list|link <tên>"; exit 1 ;;
esac
