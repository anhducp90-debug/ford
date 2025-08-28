#!/bin/bash

# ==============================================================================
# WireGuard Client Management Script
# ==============================================================================
# 
# Description: Add, remove and manage WireGuard VPN clients
# Usage: ./manage-client.sh {add|remove|list|show} [arguments...]
# 
# Examples:
#   ./manage-client.sh add "John Laptop" john@example.com
#   ./manage-client.sh remove "John Laptop"
#   ./manage-client.sh list
#   ./manage-client.sh show "John Laptop"
# ==============================================================================

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_DIR="/etc/vpn-server"
INSTALL_DIR="/opt/vpn-server"
WG_CONFIG="$CONFIG_DIR/wireguard/wg0.conf"
SERVER_DOMAIN="vpn.vietnga.info.vn"
VPN_SUBNET="10.66.66"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Logging
log() {
    echo -e "${GREEN}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1"
}

log_error() {
    echo -e "${RED}[$(date '+%Y-%m-%d %H:%M:%S')] ERROR:${NC} $1" >&2
}

log_warning() {
    echo -e "${YELLOW}[$(date '+%Y-%m-%d %H:%M:%S')] WARNING:${NC} $1"
}

# Check if script is run as root
check_root() {
    if [[ $EUID -ne 0 ]]; then
        log_error "This script must be run as root. Use: sudo $0"
        exit 1
    fi
}

# Generate client configuration
add_client() {
    local name="$1"
    local email="${2:-}"
    
    if [[ -z "$name" ]]; then
        log_error "Usage: $0 add <client-name> [email]"
        exit 1
    fi
    
    # Validate name format
    if [[ ! "$name" =~ ^[a-zA-Z0-9\ \-\_]+$ ]]; then
        log_error "Client name can only contain letters, numbers, spaces, hyphens, and underscores"
        exit 1
    fi
    
    # Check if client already exists
    if [[ -d "$CONFIG_DIR/clients/$name" ]]; then
        log_error "Client '$name' already exists!"
        exit 1
    fi
    
    log "Adding new client: $name"
    
    # Ensure WireGuard config directory exists
    mkdir -p "$CONFIG_DIR/wireguard"
    mkdir -p "$CONFIG_DIR/clients"
    
    # Generate client keys
    cd "$CONFIG_DIR/wireguard"
    CLIENT_PRIVATE_KEY=$(wg genkey)
    CLIENT_PUBLIC_KEY=$(echo "$CLIENT_PRIVATE_KEY" | wg pubkey)
    PRESHARED_KEY=$(wg genpsk)
    
    # Find next available IP
    LAST_IP=1
    if [[ -f "$WG_CONFIG" ]]; then
        LAST_IP=$(grep -oP "AllowedIPs = $VPN_SUBNET\.\K\d+" "$WG_CONFIG" 2>/dev/null | sort -n | tail -1)
        LAST_IP=${LAST_IP:-1}
    fi
    NEXT_IP=$((LAST_IP + 1))
    
    # Ensure IP is within valid range
    if [[ $NEXT_IP -gt 254 ]]; then
        log_error "Maximum number of clients (254) reached!"
        exit 1
    fi
    
    CLIENT_IP="$VPN_SUBNET.$NEXT_IP"
    
    # Get server public key
    if [[ ! -f "$CONFIG_DIR/wireguard/server_public.key" ]]; then
        log_error "Server public key not found. Please run the installation script first."
        exit 1
    fi
    SERVER_PUBLIC_KEY=$(cat "$CONFIG_DIR/wireguard/server_public.key")
    
    # Add peer to server config
    if [[ ! -f "$WG_CONFIG" ]]; then
        log_error "WireGuard server config not found. Please run the installation script first."
        exit 1
    fi
    
    cat >> "$WG_CONFIG" << EOF

# Client: $name
# Email: ${email:-N/A}
# Created: $(date)
[Peer]
PublicKey = $CLIENT_PUBLIC_KEY
PresharedKey = $PRESHARED_KEY
AllowedIPs = $CLIENT_IP/32
EOF
    
    # Create client directory
    mkdir -p "$CONFIG_DIR/clients/$name"
    
    # Create standard WireGuard client config
    cat > "$CONFIG_DIR/clients/$name/wg0.conf" << EOF
[Interface]
PrivateKey = $CLIENT_PRIVATE_KEY
Address = $CLIENT_IP/24
DNS = 1.1.1.1, 8.8.8.8
MTU = 1420

[Peer]
PublicKey = $SERVER_PUBLIC_KEY
PresharedKey = $PRESHARED_KEY
Endpoint = $SERVER_DOMAIN:51820
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25
EOF
    
    # Create WebSocket tunnel client config
    cat > "$CONFIG_DIR/clients/$name/wg0-websocket.conf" << EOF
[Interface]
PrivateKey = $CLIENT_PRIVATE_KEY
Address = $CLIENT_IP/24
DNS = 1.1.1.1, 8.8.8.8
MTU = 1420

[Peer]
PublicKey = $SERVER_PUBLIC_KEY
PresharedKey = $PRESHARED_KEY
Endpoint = 127.0.0.1:51820
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25
EOF
    
    # Generate QR codes
    if command -v qrencode &> /dev/null; then
        qrencode -t ansiutf8 < "$CONFIG_DIR/clients/$name/wg0.conf" > "$CONFIG_DIR/clients/$name/qr.txt"
        qrencode -t png -o "$CONFIG_DIR/clients/$name/qr.png" < "$CONFIG_DIR/clients/$name/wg0.conf"
        qrencode -t svg -o "$CONFIG_DIR/clients/$name/qr.svg" < "$CONFIG_DIR/clients/$name/wg0.conf"
    else
        log_warning "qrencode not found. QR codes not generated."
    fi
    
    # Create client info file
    cat > "$CONFIG_DIR/clients/$name/info.json" << EOF
{
    "name": "$name",
    "email": "${email:-}",
    "ip": "$CLIENT_IP",
    "publicKey": "$CLIENT_PUBLIC_KEY",
    "created": "$(date -Iseconds)",
    "configPath": "$CONFIG_DIR/clients/$name/wg0.conf",
    "websocketConfigPath": "$CONFIG_DIR/clients/$name/wg0-websocket.conf",
    "qrCode": "$CONFIG_DIR/clients/$name/qr.png"
}
EOF
    
    # Set proper permissions
    chmod 600 "$CONFIG_DIR/clients/$name"/*.conf
    chmod 644 "$CONFIG_DIR/clients/$name"/*.json
    chmod 644 "$CONFIG_DIR/clients/$name"/qr.*
    
    # Restart WireGuard if it's running
    if systemctl is-active --quiet wg-quick@wg0; then
        systemctl restart wg-quick@wg0
        log "WireGuard service restarted"
    fi
    
    # Restart Docker services to pick up changes
    if systemctl is-active --quiet vpn-server.service; then
        cd "$INSTALL_DIR/docker"
        docker-compose restart wg-easy
        log "WG-Easy service restarted"
    fi
    
    log "Client '$name' added successfully!"
    echo ""
    echo "📋 Client Information:"
    echo "   Name: $name"
    echo "   Email: ${email:-N/A}"
    echo "   IP: $CLIENT_IP"
    echo "   Config: $CONFIG_DIR/clients/$name/wg0.conf"
    echo "   WebSocket Config: $CONFIG_DIR/clients/$name/wg0-websocket.conf"
    if [[ -f "$CONFIG_DIR/clients/$name/qr.png" ]]; then
        echo "   QR Code: $CONFIG_DIR/clients/$name/qr.png"
    fi
    echo ""
    echo "🔗 Download links will be available at:"
    echo "   https://$SERVER_DOMAIN/downloads/clients/$name/"
}

# Remove client
remove_client() {
    local name="$1"
    
    if [[ -z "$name" ]]; then
        log_error "Usage: $0 remove <client-name>"
        exit 1
    fi
    
    if [[ ! -d "$CONFIG_DIR/clients/$name" ]]; then
        log_error "Client '$name' not found!"
        exit 1
    fi
    
    log "Removing client: $name"
    
    # Get client public key for removal
    if [[ -f "$CONFIG_DIR/clients/$name/info.json" ]]; then
        CLIENT_PUBLIC_KEY=$(jq -r '.publicKey' "$CONFIG_DIR/clients/$name/info.json")
    else
        log_warning "Client info file not found. Attempting to find in server config..."
        CLIENT_PUBLIC_KEY=$(awk "/# Client: $name/{getline; getline; if(/PublicKey/) print \$3}" "$WG_CONFIG")
    fi
    
    # Remove from server config
    if [[ -n "$CLIENT_PUBLIC_KEY" ]]; then
        # Create temporary file without the client
        awk "
        /# Client: $name/{
            for(i=0; i<4; i++) {
                getline
            }
            next
        }
        {print}
        " "$WG_CONFIG" > "$WG_CONFIG.tmp"
        
        mv "$WG_CONFIG.tmp" "$WG_CONFIG"
    else
        log_warning "Could not find client public key. Manual cleanup may be required."
    fi
    
    # Remove client directory
    rm -rf "$CONFIG_DIR/clients/$name"
    
    # Restart services
    if systemctl is-active --quiet wg-quick@wg0; then
        systemctl restart wg-quick@wg0
        log "WireGuard service restarted"
    fi
    
    if systemctl is-active --quiet vpn-server.service; then
        cd "$INSTALL_DIR/docker"
        docker-compose restart wg-easy
        log "WG-Easy service restarted"
    fi
    
    log "Client '$name' removed successfully!"
}

# List all clients
list_clients() {
    echo "📋 Configured VPN Clients:"
    echo "=========================="
    
    if [[ ! -d "$CONFIG_DIR/clients" ]] || [[ -z "$(ls -A "$CONFIG_DIR/clients" 2>/dev/null)" ]]; then
        echo "No clients configured yet."
        echo ""
        echo "To add a client, run:"
        echo "  sudo $0 add \"Client Name\" client@example.com"
        return
    fi
    
    printf "%-20s %-15s %-25s %-20s\n" "NAME" "IP ADDRESS" "EMAIL" "CREATED"
    printf "%-20s %-15s %-25s %-20s\n" "----" "----------" "-----" "-------"
    
    for client_dir in "$CONFIG_DIR/clients"/*; do
        if [[ -d "$client_dir" ]]; then
            client_name=$(basename "$client_dir")
            
            if [[ -f "$client_dir/info.json" ]]; then
                ip=$(jq -r '.ip' "$client_dir/info.json")
                email=$(jq -r '.email' "$client_dir/info.json")
                created=$(jq -r '.created' "$client_dir/info.json" | cut -d'T' -f1)
            else
                # Fallback to parsing config files
                ip=$(grep "Address" "$client_dir/wg0.conf" 2>/dev/null | cut -d' ' -f3 | cut -d'/' -f1 || echo "Unknown")
                email="Unknown"
                created=$(stat -c %y "$client_dir" | cut -d' ' -f1)
            fi
            
            printf "%-20s %-15s %-25s %-20s\n" "$client_name" "$ip" "${email:-N/A}" "$created"
        fi
    done
    
    echo ""
    echo "Total clients: $(find "$CONFIG_DIR/clients" -maxdepth 1 -type d | wc -l | awk '{print $1-1}')"
}

# Show detailed client information
show_client() {
    local name="$1"
    
    if [[ -z "$name" ]]; then
        log_error "Usage: $0 show <client-name>"
        exit 1
    fi
    
    if [[ ! -d "$CONFIG_DIR/clients/$name" ]]; then
        log_error "Client '$name' not found!"
        exit 1
    fi
    
    echo "📋 Client Details: $name"
    echo "========================"
    
    if [[ -f "$CONFIG_DIR/clients/$name/info.json" ]]; then
        jq -r '
        "Name: " + .name,
        "Email: " + (.email // "N/A"),
        "IP Address: " + .ip,
        "Created: " + .created,
        "Config Path: " + .configPath,
        "WebSocket Config: " + .websocketConfigPath,
        "QR Code: " + (.qrCode // "N/A")
        ' "$CONFIG_DIR/clients/$name/info.json"
    else
        echo "Name: $name"
        if [[ -f "$CONFIG_DIR/clients/$name/wg0.conf" ]]; then
            echo "IP Address: $(grep "Address" "$CONFIG_DIR/clients/$name/wg0.conf" | cut -d' ' -f3)"
        fi
        echo "Config Path: $CONFIG_DIR/clients/$name/wg0.conf"
    fi
    
    echo ""
    echo "📁 Available Files:"
    ls -la "$CONFIG_DIR/clients/$name/" | grep -v "^total"
    
    echo ""
    echo "🔗 Download Commands:"
    echo "  Standard config: cat '$CONFIG_DIR/clients/$name/wg0.conf'"
    echo "  WebSocket config: cat '$CONFIG_DIR/clients/$name/wg0-websocket.conf'"
    if [[ -f "$CONFIG_DIR/clients/$name/qr.txt" ]]; then
        echo "  QR Code (text): cat '$CONFIG_DIR/clients/$name/qr.txt'"
    fi
}

# Generate client download package
package_client() {
    local name="$1"
    
    if [[ -z "$name" ]]; then
        log_error "Usage: $0 package <client-name>"
        exit 1
    fi
    
    if [[ ! -d "$CONFIG_DIR/clients/$name" ]]; then
        log_error "Client '$name' not found!"
        exit 1
    fi
    
    local package_dir="/tmp/vpn-client-$name"
    local package_file="/tmp/vpn-client-$name.tar.gz"
    
    log "Creating client package for: $name"
    
    # Create package directory
    rm -rf "$package_dir"
    mkdir -p "$package_dir"
    
    # Copy client files
    cp "$CONFIG_DIR/clients/$name"/* "$package_dir/" 2>/dev/null || true
    
    # Copy client helper script
    cp "$INSTALL_DIR/scripts/client-helper.sh" "$package_dir/"
    
    # Create README for client
    cat > "$package_dir/README.txt" << EOF
VPN Client Package for: $name
==============================

This package contains everything needed to connect to the VPN server.

Files included:
- wg0.conf: Standard WireGuard configuration
- wg0-websocket.conf: WebSocket tunnel configuration (for restricted networks)
- qr.png: QR code for mobile devices
- client-helper.sh: Helper script for easy connection
- info.json: Client information

Quick Setup (Linux/macOS):
1. Install WireGuard: ./client-helper.sh install
2. Setup client: ./client-helper.sh setup wg0.conf
3. Connect: ./client-helper.sh start
4. Disconnect: ./client-helper.sh stop

For restricted networks (using WebSocket tunnel):
1. Setup: ./client-helper.sh setup wg0-websocket.conf
2. Connect: ./client-helper.sh start

Mobile devices:
- Scan the QR code with your WireGuard app
- Or manually import the wg0.conf file

For support, contact your system administrator.
EOF
    
    # Create package
    cd /tmp
    tar -czf "$package_file" "vpn-client-$name"
    rm -rf "$package_dir"
    
    log "Client package created: $package_file"
    echo ""
    echo "📦 Package Details:"
    echo "   File: $package_file"
    echo "   Size: $(du -h "$package_file" | cut -f1)"
    echo ""
    echo "Transfer this file to the client and extract it:"
    echo "   tar -xzf vpn-client-$name.tar.gz"
}

# Main function
main() {
    check_root
    
    case "${1:-}" in
        add)
            add_client "${2:-}" "${3:-}"
            ;;
        remove)
            remove_client "${2:-}"
            ;;
        list)
            list_clients
            ;;
        show)
            show_client "${2:-}"
            ;;
        package)
            package_client "${2:-}"
            ;;
        *)
            echo "WireGuard Client Management Script"
            echo "=================================="
            echo ""
            echo "Usage: $0 {add|remove|list|show|package} [arguments...]"
            echo ""
            echo "Commands:"
            echo "  add <name> [email]     Add a new client"
            echo "  remove <name>          Remove a client"
            echo "  list                   List all clients"
            echo "  show <name>            Show client details"
            echo "  package <name>         Create client package"
            echo ""
            echo "Examples:"
            echo "  $0 add \"John Laptop\" john@example.com"
            echo "  $0 remove \"John Laptop\""
            echo "  $0 list"
            echo "  $0 show \"John Laptop\""
            echo "  $0 package \"John Laptop\""
            exit 1
            ;;
    esac
}

# Run main function
main "$@"