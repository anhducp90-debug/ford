#!/bin/bash

#=============================================================================
# WireGuard VPN System - Quick Installation Script
# For Frankfurt VPS with UDP2RAW bypass capability
#=============================================================================

set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

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

print_header() {
    echo
    echo "=================================================================="
    echo -e "${BLUE}$*${NC}"
    echo "=================================================================="
    echo
}

check_system() {
    print_info "Checking system requirements..."
    
    # Check if running as root
    if [[ $EUID -ne 0 ]]; then
        print_error "This installation must be run as root or with sudo"
        echo "Please run: sudo $0"
        exit 1
    fi
    
    # Check Ubuntu version
    if [[ -f /etc/lsb-release ]]; then
        source /etc/lsb-release
        if [[ "$DISTRIB_ID" == "Ubuntu" ]]; then
            local version_num=$(echo "$DISTRIB_RELEASE" | cut -d. -f1)
            if [[ $version_num -ge 20 ]]; then
                print_success "Ubuntu $DISTRIB_RELEASE detected (supported)"
            else
                print_warn "Ubuntu $DISTRIB_RELEASE detected (may work but not tested)"
            fi
        else
            print_warn "Non-Ubuntu system detected (may work but not tested)"
        fi
    else
        print_warn "Cannot detect system version"
    fi
    
    # Check internet connection
    if ping -c 1 8.8.8.8 &> /dev/null; then
        print_success "Internet connection available"
    else
        print_error "No internet connection available"
        exit 1
    fi
    
    # Check if running on VPS
    local public_ip=$(curl -s ifconfig.me 2>/dev/null || echo "")
    if [[ -n "$public_ip" ]]; then
        print_success "Public IP detected: $public_ip"
    else
        print_warn "Could not detect public IP address"
    fi
}

install_dependencies() {
    print_info "Installing system dependencies..."
    
    # Update package list
    apt update
    
    # Install required packages
    apt install -y \
        wireguard \
        wireguard-tools \
        qrencode \
        iptables \
        wget \
        curl \
        openssl \
        net-tools
    
    print_success "Dependencies installed successfully"
}

setup_wireguard() {
    print_info "Setting up WireGuard VPN server..."
    
    # Run the VPN manager setup
    "$SCRIPT_DIR/vpn_manager.sh" setup
    
    print_success "WireGuard VPN server configured"
}

setup_udp2raw() {
    print_info "Setting up UDP2RAW for bypass capabilities..."
    
    # Install UDP2RAW
    "$SCRIPT_DIR/udp2raw_manager.sh" install
    
    print_success "UDP2RAW configured for NAT/CGNAT bypass"
}

show_completion_info() {
    local public_ip=$(curl -s ifconfig.me 2>/dev/null || echo "YOUR_SERVER_IP")
    
    print_header "Installation Complete!"
    
    echo "Your WireGuard VPN server is now ready!"
    echo
    print_info "Server Information:"
    echo "  • Public IP: $public_ip"
    echo "  • WireGuard Port: 51820 (UDP)"
    echo "  • UDP2RAW Port: 4096 (TCP for bypass)"
    echo "  • VPN Subnet: 10.66.66.0/24"
    echo "  • Maximum Clients: 50"
    echo
    print_info "Next Steps:"
    echo "  1. Add your first client:"
    echo "     sudo ./vpn_manager.sh add-client your-device-name"
    echo
    echo "  2. Get the QR code for mobile setup:"
    echo "     ./vpn_manager.sh show-client-qr your-device-name"
    echo
    echo "  3. Check server status anytime:"
    echo "     ./vpn_manager.sh server-status"
    echo
    print_info "For Bypass Mode (Russia, restrictive networks):"
    echo "  • Use UDP2RAW on clients connecting to port 4096"
    echo "  • Check UDP2RAW password: ./udp2raw_manager.sh password"
    echo "  • See README.md for detailed client setup instructions"
    echo
    print_info "Important Files:"
    echo "  • Client configs: ./clients/"
    echo "  • Server logs: ./logs/vpn_manager.log"
    echo "  • Documentation: ./README.md"
    echo
    print_warn "Security Notes:"
    echo "  • All keys are stored securely with 600 permissions"
    echo "  • Keep your server updated regularly"
    echo "  • Monitor logs for suspicious activity"
    echo "  • Backup your keys and client database regularly"
    echo
    print_success "Installation completed successfully!"
}

show_usage() {
    echo "WireGuard + UDP2RAW VPN Installation Script"
    echo
    echo "Usage: $0 [options]"
    echo
    echo "Options:"
    echo "  --wireguard-only    Install only WireGuard (no UDP2RAW)"
    echo "  --help, -h          Show this help message"
    echo
    echo "Default: Install both WireGuard and UDP2RAW for maximum compatibility"
}

main() {
    local install_udp2raw=true
    
    # Parse command line arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            --wireguard-only)
                install_udp2raw=false
                shift
                ;;
            --help|-h)
                show_usage
                exit 0
                ;;
            *)
                print_error "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
    
    print_header "WireGuard + UDP2RAW VPN Installation"
    
    # System checks
    check_system
    
    # Install dependencies
    install_dependencies
    
    # Setup WireGuard
    setup_wireguard
    
    # Setup UDP2RAW if requested
    if [[ "$install_udp2raw" == true ]]; then
        setup_udp2raw
    else
        print_info "Skipping UDP2RAW installation (--wireguard-only specified)"
    fi
    
    # Show completion information
    show_completion_info
}

# Check if script exists
if [[ ! -f "$SCRIPT_DIR/vpn_manager.sh" ]]; then
    print_error "vpn_manager.sh not found in $SCRIPT_DIR"
    print_error "Please ensure all files are in the same directory"
    exit 1
fi

if [[ ! -f "$SCRIPT_DIR/udp2raw_manager.sh" ]]; then
    print_warn "udp2raw_manager.sh not found - UDP2RAW installation will be skipped"
    install_udp2raw=false
fi

main "$@"