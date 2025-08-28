#!/bin/bash

# ==============================================================================
# VPN Client Helper Script
# ==============================================================================
# 
# Description: Client-side helper for connecting to VPN via WebSocket tunnel
# Platform: Linux, macOS, Windows (WSL)
# Usage: ./client-helper.sh {install|setup|start|stop|status|connect}
# 
# This script helps clients connect to the VPN server using either:
# 1. Direct WireGuard connection (standard)
# 2. WebSocket tunnel (for restricted networks)
# ==============================================================================

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="$HOME/.vpn-client.conf"
WS_CONFIG_FILE="$HOME/.vpn-client-ws.conf"
WS_TUNNEL_PORT="8080"
WG_PORT="51820"
SERVER_DOMAIN="vpn.vietnga.info.vn"
TUNNEL_PID_FILE="/tmp/wstunnel.pid"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Logging functions
log() {
    echo -e "${GREEN}[$(date '+%H:%M:%S')]${NC} $1"
}

log_error() {
    echo -e "${RED}[$(date '+%H:%M:%S')] ERROR:${NC} $1" >&2
}

log_warning() {
    echo -e "${YELLOW}[$(date '+%H:%M:%S')] WARNING:${NC} $1"
}

log_info() {
    echo -e "${BLUE}[$(date '+%H:%M:%S')] INFO:${NC} $1"
}

# Detect operating system
detect_os() {
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        echo "linux"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        echo "macos"
    elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]]; then
        echo "windows"
    else
        echo "unknown"
    fi
}

# Check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Install dependencies based on OS
install_dependencies() {
    local os=$(detect_os)
    log "Installing dependencies for $os..."
    
    case $os in
        linux)
            # Detect Linux distribution
            if command_exists apt-get; then
                # Ubuntu/Debian
                log "Detected Ubuntu/Debian system"
                sudo apt-get update
                sudo apt-get install -y wireguard wireguard-tools wget curl jq qrencode
            elif command_exists yum; then
                # CentOS/RHEL
                log "Detected CentOS/RHEL system"
                sudo yum install -y epel-release
                sudo yum install -y wireguard-tools wget curl jq qrencode
            elif command_exists dnf; then
                # Fedora
                log "Detected Fedora system"
                sudo dnf install -y wireguard-tools wget curl jq qrencode
            elif command_exists pacman; then
                # Arch Linux
                log "Detected Arch Linux system"
                sudo pacman -S --noconfirm wireguard-tools wget curl jq qrencode
            else
                log_error "Unsupported Linux distribution. Please install WireGuard manually."
                exit 1
            fi
            ;;
        macos)
            log "Detected macOS system"
            if ! command_exists brew; then
                log_error "Homebrew not found. Please install Homebrew first:"
                log_error "  /bin/bash -c \"\$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\""
                exit 1
            fi
            brew install wireguard-tools wget curl jq qrencode
            ;;
        windows)
            log "Detected Windows system (WSL)"
            log_warning "Please ensure you're running this in WSL (Windows Subsystem for Linux)"
            sudo apt-get update
            sudo apt-get install -y wireguard wireguard-tools wget curl jq qrencode
            ;;
        *)
            log_error "Unsupported operating system: $OSTYPE"
            log_error "Please install WireGuard tools manually"
            exit 1
            ;;
    esac
    
    # Install wstunnel
    install_wstunnel
    
    log "Dependencies installed successfully!"
}

# Install wstunnel
install_wstunnel() {
    local os=$(detect_os)
    local arch=$(uname -m)
    local wstunnel_binary="wstunnel"
    
    log "Installing wstunnel..."
    
    # Determine the correct binary URL
    local base_url="https://github.com/erebe/wstunnel/releases/latest/download"
    local binary_url=""
    
    case $os in
        linux)
            if [[ "$arch" == "x86_64" ]]; then
                binary_url="$base_url/wstunnel-linux-x64"
            elif [[ "$arch" == "aarch64" ]] || [[ "$arch" == "arm64" ]]; then
                binary_url="$base_url/wstunnel-linux-arm64"
            else
                log_error "Unsupported architecture for Linux: $arch"
                exit 1
            fi
            ;;
        macos)
            if [[ "$arch" == "x86_64" ]]; then
                binary_url="$base_url/wstunnel-macos-x64"
            elif [[ "$arch" == "arm64" ]]; then
                binary_url="$base_url/wstunnel-macos-arm64"
            else
                log_error "Unsupported architecture for macOS: $arch"
                exit 1
            fi
            ;;
        windows)
            binary_url="$base_url/wstunnel-windows-x64.exe"
            wstunnel_binary="wstunnel.exe"
            ;;
        *)
            log_error "Unsupported OS for wstunnel: $os"
            exit 1
            ;;
    esac
    
    # Download and install wstunnel
    local temp_file="/tmp/$wstunnel_binary"
    log_info "Downloading wstunnel from: $binary_url"
    
    if wget -q "$binary_url" -O "$temp_file"; then
        chmod +x "$temp_file"
        sudo mv "$temp_file" "/usr/local/bin/wstunnel"
        log "wstunnel installed successfully to /usr/local/bin/wstunnel"
    else
        log_error "Failed to download wstunnel"
        exit 1
    fi
    
    # Verify installation
    if command_exists wstunnel; then
        log_info "wstunnel version: $(wstunnel --version 2>&1 || echo 'Version info not available')"
    else
        log_error "wstunnel installation verification failed"
        exit 1
    fi
}

# Setup client configuration
setup_client() {
    local config_path="$1"
    local use_websocket="${2:-false}"
    
    if [[ -z "$config_path" ]]; then
        log_error "Usage: $0 setup <config-file> [websocket]"
        log_error "  config-file: Path to WireGuard configuration file"
        log_error "  websocket: Optional, set to 'true' to use WebSocket tunnel"
        exit 1
    fi
    
    if [[ ! -f "$config_path" ]]; then
        log_error "Config file not found: $config_path"
        exit 1
    fi
    
    log "Setting up VPN client configuration..."
    
    if [[ "$use_websocket" == "true" ]]; then
        # Setup for WebSocket tunnel
        cp "$config_path" "$WS_CONFIG_FILE"
        
        # Modify config for WebSocket tunnel
        sed -i "s/Endpoint = .*/Endpoint = 127.0.0.1:$WG_PORT/" "$WS_CONFIG_FILE"
        
        log "WebSocket tunnel configuration created: $WS_CONFIG_FILE"
        log_info "This configuration will use WebSocket tunneling for restricted networks"
    else
        # Standard configuration
        cp "$config_path" "$CONFIG_FILE"
        log "Standard configuration created: $CONFIG_FILE"
    fi
    
    # Set proper permissions
    chmod 600 "$CONFIG_FILE" 2>/dev/null || true
    chmod 600 "$WS_CONFIG_FILE" 2>/dev/null || true
    
    log "Client configured successfully!"
    echo ""
    echo "📋 Configuration Summary:"
    echo "   Config file: $config_path"
    echo "   Standard config: $CONFIG_FILE"
    if [[ "$use_websocket" == "true" ]]; then
        echo "   WebSocket config: $WS_CONFIG_FILE"
        echo "   Mode: WebSocket tunnel (for restricted networks)"
    else
        echo "   Mode: Direct connection"
    fi
    echo ""
    echo "Next steps:"
    echo "   1. Connect: $0 start [websocket]"
    echo "   2. Check status: $0 status"
    echo "   3. Disconnect: $0 stop"
}

# Start VPN connection
start_tunnel() {
    local use_websocket="${1:-false}"
    local config_to_use="$CONFIG_FILE"
    
    if [[ "$use_websocket" == "true" ]]; then
        config_to_use="$WS_CONFIG_FILE"
        
        if [[ ! -f "$config_to_use" ]]; then
            log_error "WebSocket configuration not found. Please run setup first:"
            log_error "  $0 setup <config-file> true"
            exit 1
        fi
        
        log "Starting WebSocket tunnel..."
        
        # Check if wstunnel is available
        if ! command_exists wstunnel; then
            log_error "wstunnel not found. Please run: $0 install"
            exit 1
        fi
        
        # Start wstunnel in background
        nohup wstunnel --localToRemote="127.0.0.1:$WG_PORT:127.0.0.1:$WG_PORT" "wss://$SERVER_DOMAIN/wstunnel" >/dev/null 2>&1 &
        local tunnel_pid=$!
        echo $tunnel_pid > "$TUNNEL_PID_FILE"
        
        log_info "WebSocket tunnel started (PID: $tunnel_pid)"
        
        # Wait for tunnel to establish
        sleep 3
        
        # Verify tunnel is running
        if ! kill -0 $tunnel_pid 2>/dev/null; then
            log_error "WebSocket tunnel failed to start"
            rm -f "$TUNNEL_PID_FILE"
            exit 1
        fi
    else
        if [[ ! -f "$config_to_use" ]]; then
            log_error "Configuration not found. Please run setup first:"
            log_error "  $0 setup <config-file>"
            exit 1
        fi
    fi
    
    log "Starting VPN connection..."
    
    # Check if WireGuard is already running
    if sudo wg show 2>/dev/null | grep -q "interface:"; then
        log_warning "WireGuard is already running. Stopping existing connection..."
        sudo wg-quick down "$config_to_use" 2>/dev/null || true
    fi
    
    # Start WireGuard
    if sudo wg-quick up "$config_to_use"; then
        log "VPN connected successfully!"
        
        if [[ "$use_websocket" == "true" ]]; then
            log_info "Connected via WebSocket tunnel"
        else
            log_info "Connected via direct connection"
        fi
        
        # Show connection info
        echo ""
        echo "🔗 Connection Details:"
        sudo wg show
        
        echo ""
        echo "🌐 IP Address Information:"
        echo "   VPN IP: $(ip addr show $(sudo wg show | grep interface | cut -d: -f2 | tr -d ' ') | grep 'inet ' | awk '{print $2}' 2>/dev/null || echo 'Unknown')"
        echo "   Public IP: $(curl -s https://api.ipify.org 2>/dev/null || echo 'Unknown')"
        
    else
        log_error "Failed to start VPN connection"
        
        # Cleanup on failure
        if [[ "$use_websocket" == "true" ]] && [[ -f "$TUNNEL_PID_FILE" ]]; then
            kill $(cat "$TUNNEL_PID_FILE") 2>/dev/null || true
            rm -f "$TUNNEL_PID_FILE"
        fi
        
        exit 1
    fi
}

# Stop VPN connection
stop_tunnel() {
    log "Stopping VPN connection..."
    
    local stopped_something=false
    
    # Stop WireGuard
    if sudo wg show 2>/dev/null | grep -q "interface:"; then
        # Find the config file being used
        local active_interface=$(sudo wg show | grep interface | cut -d: -f2 | tr -d ' ')
        
        # Try both config files
        for config in "$CONFIG_FILE" "$WS_CONFIG_FILE"; do
            if [[ -f "$config" ]]; then
                sudo wg-quick down "$config" 2>/dev/null || true
                stopped_something=true
            fi
        done
        
        if [[ "$stopped_something" == "true" ]]; then
            log "WireGuard connection stopped"
        fi
    fi
    
    # Stop WebSocket tunnel
    if [[ -f "$TUNNEL_PID_FILE" ]]; then
        local tunnel_pid=$(cat "$TUNNEL_PID_FILE")
        if kill -0 $tunnel_pid 2>/dev/null; then
            kill $tunnel_pid 2>/dev/null || true
            rm -f "$TUNNEL_PID_FILE"
            log "WebSocket tunnel stopped"
            stopped_something=true
        else
            rm -f "$TUNNEL_PID_FILE"
        fi
    fi
    
    # Kill any remaining wstunnel processes
    if pgrep -f "wstunnel.*$SERVER_DOMAIN" >/dev/null 2>&1; then
        pkill -f "wstunnel.*$SERVER_DOMAIN" 2>/dev/null || true
        log "Cleaned up remaining wstunnel processes"
        stopped_something=true
    fi
    
    if [[ "$stopped_something" == "true" ]]; then
        log "VPN disconnected successfully!"
    else
        log_info "No active VPN connection found"
    fi
}

# Check VPN status
check_status() {
    echo "🔍 VPN Connection Status"
    echo "========================"
    
    local is_connected=false
    
    # Check WireGuard status
    if sudo wg show 2>/dev/null | grep -q "interface:"; then
        echo -e "${GREEN}✓ WireGuard: Connected${NC}"
        echo ""
        sudo wg show
        is_connected=true
        
        # Get VPN interface info
        local wg_interface=$(sudo wg show | grep interface | cut -d: -f2 | tr -d ' ')
        if [[ -n "$wg_interface" ]]; then
            echo ""
            echo "🌐 Network Information:"
            local vpn_ip=$(ip addr show "$wg_interface" | grep 'inet ' | awk '{print $2}' 2>/dev/null || echo 'Unknown')
            echo "   VPN IP: $vpn_ip"
        fi
    else
        echo -e "${RED}✗ WireGuard: Disconnected${NC}"
    fi
    
    # Check WebSocket tunnel status
    if [[ -f "$TUNNEL_PID_FILE" ]]; then
        local tunnel_pid=$(cat "$TUNNEL_PID_FILE")
        if kill -0 $tunnel_pid 2>/dev/null; then
            echo -e "${GREEN}✓ WebSocket Tunnel: Running${NC} (PID: $tunnel_pid)"
        else
            echo -e "${RED}✗ WebSocket Tunnel: Stopped${NC} (stale PID file)"
            rm -f "$TUNNEL_PID_FILE"
        fi
    else
        if pgrep -f "wstunnel.*$SERVER_DOMAIN" >/dev/null 2>&1; then
            echo -e "${YELLOW}⚠ WebSocket Tunnel: Running${NC} (no PID file)"
        else
            echo -e "${RED}✗ WebSocket Tunnel: Stopped${NC}"
        fi
    fi
    
    # Check public IP
    echo ""
    echo "🌍 Public IP Information:"
    local public_ip=$(curl -s --max-time 5 https://api.ipify.org 2>/dev/null || echo 'Unable to determine')
    echo "   Current Public IP: $public_ip"
    
    # Configuration files status
    echo ""
    echo "📁 Configuration Files:"
    if [[ -f "$CONFIG_FILE" ]]; then
        echo -e "   Standard config: ${GREEN}✓${NC} $CONFIG_FILE"
    else
        echo -e "   Standard config: ${RED}✗${NC} Not found"
    fi
    
    if [[ -f "$WS_CONFIG_FILE" ]]; then
        echo -e "   WebSocket config: ${GREEN}✓${NC} $WS_CONFIG_FILE"
    else
        echo -e "   WebSocket config: ${RED}✗${NC} Not found"
    fi
    
    # DNS check
    echo ""
    echo "🔍 DNS Resolution Test:"
    if nslookup "$SERVER_DOMAIN" >/dev/null 2>&1; then
        local server_ip=$(nslookup "$SERVER_DOMAIN" | grep -A1 "Name:" | grep "Address:" | awk '{print $2}' | head -1)
        echo -e "   Server DNS: ${GREEN}✓${NC} $SERVER_DOMAIN → $server_ip"
    else
        echo -e "   Server DNS: ${RED}✗${NC} Failed to resolve $SERVER_DOMAIN"
    fi
    
    # Overall status
    echo ""
    if [[ "$is_connected" == "true" ]]; then
        echo -e "${GREEN}🎉 VPN is connected and working!${NC}"
    else
        echo -e "${YELLOW}⚠️  VPN is not connected${NC}"
        echo ""
        echo "To connect:"
        echo "   Direct connection: $0 start"
        echo "   WebSocket tunnel:  $0 start websocket"
    fi
}

# Connect with automatic detection
auto_connect() {
    log "Attempting to connect to VPN server..."
    
    # Check if configurations exist
    if [[ ! -f "$CONFIG_FILE" ]] && [[ ! -f "$WS_CONFIG_FILE" ]]; then
        log_error "No configuration files found. Please run setup first:"
        log_error "  $0 setup <config-file>"
        exit 1
    fi
    
    # Try direct connection first
    if [[ -f "$CONFIG_FILE" ]]; then
        log_info "Trying direct connection..."
        if timeout 10 nc -z "$SERVER_DOMAIN" 51820 2>/dev/null; then
            log "Direct connection available, using standard WireGuard"
            start_tunnel false
            return
        else
            log_warning "Direct connection failed, trying WebSocket tunnel..."
        fi
    fi
    
    # Try WebSocket tunnel
    if [[ -f "$WS_CONFIG_FILE" ]]; then
        log_info "Using WebSocket tunnel for restricted networks"
        start_tunnel true
    else
        log_error "WebSocket configuration not found. Please run setup with WebSocket option:"
        log_error "  $0 setup <config-file> true"
        exit 1
    fi
}

# Show help
show_help() {
    echo "VPN Client Helper Script"
    echo "========================"
    echo ""
    echo "This script helps you connect to the VPN server using WireGuard"
    echo "with optional WebSocket tunneling for restricted networks."
    echo ""
    echo "Usage: $0 {install|setup|start|stop|status|connect|help}"
    echo ""
    echo "Commands:"
    echo "  install                    Install required dependencies"
    echo "  setup <config> [websocket] Setup client configuration"
    echo "  start [websocket]          Start VPN connection"
    echo "  stop                       Stop VPN connection"
    echo "  status                     Show connection status"
    echo "  connect                    Auto-connect (tries direct, then WebSocket)"
    echo "  help                       Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0 install                           # Install dependencies"
    echo "  $0 setup wg0.conf                   # Setup standard config"
    echo "  $0 setup wg0.conf true              # Setup WebSocket config"
    echo "  $0 start                             # Connect directly"
    echo "  $0 start websocket                  # Connect via WebSocket"
    echo "  $0 connect                          # Auto-connect"
    echo "  $0 status                           # Check status"
    echo "  $0 stop                             # Disconnect"
    echo ""
    echo "Notes:"
    echo "  - Use WebSocket mode for networks that block VPN traffic"
    echo "  - The 'connect' command automatically chooses the best method"
    echo "  - Run 'install' first on new systems"
    echo "  - Configurations are stored in ~/.vpn-client*.conf"
}

# Main function
main() {
    case "${1:-}" in
        install)
            install_dependencies
            ;;
        setup)
            setup_client "${2:-}" "${3:-false}"
            ;;
        start)
            if [[ "${2:-}" == "websocket" ]] || [[ "${2:-}" == "ws" ]]; then
                start_tunnel true
            else
                start_tunnel false
            fi
            ;;
        stop)
            stop_tunnel
            ;;
        status)
            check_status
            ;;
        connect)
            auto_connect
            ;;
        help|--help|-h)
            show_help
            ;;
        *)
            show_help
            exit 1
            ;;
    esac
}

# Trap for cleanup on exit
trap 'log_info "Script interrupted, cleaning up..."; stop_tunnel >/dev/null 2>&1 || true' INT TERM

# Run main function
main "$@"