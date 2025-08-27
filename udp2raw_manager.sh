#!/bin/bash

#=============================================================================
# UDP2RAW Installation and Setup Script
# For bypassing NAT/CGNAT/ISP blocking in restrictive environments
#=============================================================================

set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly UDP2RAW_VERSION="20200818.0"
readonly UDP2RAW_URL="https://github.com/wangyu-/udp2raw-tunnel/releases/download/${UDP2RAW_VERSION}/udp2raw_binaries.tar.gz"
readonly UDP2RAW_DIR="/opt/udp2raw"
readonly UDP2RAW_SERVICE="/etc/systemd/system/udp2raw.service"

# Colors
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m'

print_success() {
    echo -e "${GREEN}✓ $*${NC}"
}

print_error() {
    echo -e "${RED}✗ $*${NC}"
}

print_warn() {
    echo -e "${YELLOW}⚠ $*${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ $*${NC}"
}

check_root() {
    if [[ $EUID -ne 0 ]]; then
        print_error "This script must be run as root or with sudo privileges"
        exit 1
    fi
}

install_udp2raw() {
    print_info "Installing UDP2RAW..."
    
    # Create directory
    mkdir -p "$UDP2RAW_DIR"
    cd "$UDP2RAW_DIR"
    
    # Download UDP2RAW
    print_info "Downloading UDP2RAW ${UDP2RAW_VERSION}..."
    wget -q "$UDP2RAW_URL" -O udp2raw_binaries.tar.gz
    
    # Extract and install
    tar -xzf udp2raw_binaries.tar.gz
    chmod +x udp2raw_amd64
    ln -sf "$UDP2RAW_DIR/udp2raw_amd64" /usr/local/bin/udp2raw
    
    # Cleanup
    rm -f udp2raw_binaries.tar.gz
    
    print_success "UDP2RAW installed successfully"
}

create_udp2raw_service() {
    print_info "Creating UDP2RAW systemd service..."
    
    # Generate a random password
    local password=$(openssl rand -base64 32)
    
    cat > "$UDP2RAW_SERVICE" << EOF
[Unit]
Description=UDP2RAW Tunnel Service
After=network.target
Wants=network.target

[Service]
Type=simple
User=root
ExecStart=/usr/local/bin/udp2raw -s -l 0.0.0.0:4096 -r 127.0.0.1:51820 -k "$password" --raw-mode faketcp --log-level 2
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

    # Save password for later use
    echo "$password" > "$UDP2RAW_DIR/password.txt"
    chmod 600 "$UDP2RAW_DIR/password.txt"
    
    # Enable service
    systemctl daemon-reload
    systemctl enable udp2raw
    
    print_success "UDP2RAW service created"
    print_info "Password saved to: $UDP2RAW_DIR/password.txt"
}

start_udp2raw() {
    print_info "Starting UDP2RAW service..."
    systemctl start udp2raw
    
    if systemctl is-active --quiet udp2raw; then
        print_success "UDP2RAW service started successfully"
    else
        print_error "Failed to start UDP2RAW service"
        return 1
    fi
}

show_client_instructions() {
    local password=$(cat "$UDP2RAW_DIR/password.txt")
    local server_ip=$(curl -s ifconfig.me || echo "YOUR_SERVER_IP")
    
    echo
    print_info "UDP2RAW Server Setup Complete!"
    echo
    print_info "Client Setup Instructions:"
    echo
    echo "1. Download UDP2RAW for your platform:"
    echo "   https://github.com/wangyu-/udp2raw-tunnel/releases"
    echo
    echo "2. Run UDP2RAW client (replace YOUR_SERVER_IP with actual IP):"
    echo "   Linux/Mac:"
    echo "   sudo udp2raw -c -l 127.0.0.1:51820 -r ${server_ip}:4096 -k \"$password\" --raw-mode faketcp"
    echo
    echo "   Windows:"
    echo "   udp2raw.exe -c -l 127.0.0.1:51820 -r ${server_ip}:4096 -k \"$password\" --raw-mode faketcp"
    echo
    echo "3. Configure WireGuard client to connect to localhost:51820"
    echo "   (instead of the server IP directly)"
    echo
    print_warn "Keep the password secure: $password"
    echo "Password is also saved in: $UDP2RAW_DIR/password.txt"
}

status_udp2raw() {
    print_info "UDP2RAW Service Status:"
    echo
    
    if systemctl is-active --quiet udp2raw; then
        print_success "Service Status: Active"
        
        # Show process info
        local pid=$(pgrep udp2raw || echo "")
        if [[ -n "$pid" ]]; then
            print_info "Process ID: $pid"
            print_info "Listening on port 4096 (TCP mode)"
        fi
    else
        print_error "Service Status: Inactive"
    fi
    
    # Port check
    if netstat -tlpn | grep -q ":4096 "; then
        print_success "Port 4096: Listening"
    else
        print_error "Port 4096: Not listening"
    fi
}

main() {
    case "${1:-}" in
        "install")
            check_root
            install_udp2raw
            create_udp2raw_service
            start_udp2raw
            show_client_instructions
            ;;
        "start")
            check_root
            systemctl start udp2raw
            print_success "UDP2RAW service started"
            ;;
        "stop")
            check_root
            systemctl stop udp2raw
            print_success "UDP2RAW service stopped"
            ;;
        "restart")
            check_root
            systemctl restart udp2raw
            print_success "UDP2RAW service restarted"
            ;;
        "status")
            status_udp2raw
            ;;
        "password")
            if [[ -f "$UDP2RAW_DIR/password.txt" ]]; then
                echo "UDP2RAW Password: $(cat "$UDP2RAW_DIR/password.txt")"
            else
                print_error "Password file not found. Run 'install' first."
            fi
            ;;
        *)
            echo "UDP2RAW Management Script"
            echo
            echo "Usage: $0 <command>"
            echo
            echo "Commands:"
            echo "  install   - Install and setup UDP2RAW"
            echo "  start     - Start UDP2RAW service"
            echo "  stop      - Stop UDP2RAW service"
            echo "  restart   - Restart UDP2RAW service"
            echo "  status    - Show service status"
            echo "  password  - Show UDP2RAW password"
            echo
            echo "Example:"
            echo "  sudo $0 install"
            echo "  $0 status"
            ;;
    esac
}

main "$@"