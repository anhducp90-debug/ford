#!/bin/bash

#=============================================================================
# WireGuard + UDP2RAW VPN Management Script for Frankfurt VPS
# Supports up to 50 clients with automatic key generation and QR codes
# Designed to bypass NAT/CGNAT/ISP blocking for clients in Russia
#=============================================================================

set -euo pipefail

# Configuration
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly WG_INTERFACE="wg0"
readonly WG_PORT="51820"
readonly UDP2RAW_PORT="4096"
readonly VPN_SUBNET="10.66.66.0/24"
readonly SERVER_IP="10.66.66.1"
readonly CLIENT_IP_START="10.66.66.2"
readonly MAX_CLIENTS=50

# Directories
readonly WG_DIR="/etc/wireguard"
readonly CLIENT_DIR="$SCRIPT_DIR/clients"
readonly KEYS_DIR="$SCRIPT_DIR/keys"
readonly LOG_DIR="$SCRIPT_DIR/logs"
readonly TEMPLATES_DIR="$SCRIPT_DIR/templates"

# Files
readonly SERVER_CONFIG="$WG_DIR/wg0.conf"
readonly CLIENT_DB="$CLIENT_DIR/clients.db"
readonly LOG_FILE="$LOG_DIR/vpn_manager.log"

# Colors for output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No Color

#=============================================================================
# Utility Functions
#=============================================================================

log() {
    local level="$1"
    shift
    local message="$*"
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    echo "[$timestamp] [$level] $message" | tee -a "$LOG_FILE"
}

log_info() {
    log "INFO" "$@"
}

log_error() {
    log "ERROR" "$@"
}

log_warn() {
    log "WARN" "$@"
}

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

#=============================================================================
# System Checks and Setup
#=============================================================================

check_root() {
    if [[ $EUID -ne 0 ]]; then
        print_error "This script must be run as root or with sudo privileges"
        log_error "Script executed without root privileges"
        exit 1
    fi
}

check_dependencies() {
    local deps=("wireguard" "wg" "wg-quick" "qrencode" "iptables")
    local missing_deps=()
    
    for dep in "${deps[@]}"; do
        if ! command -v "$dep" &> /dev/null; then
            missing_deps+=("$dep")
        fi
    done
    
    if [[ ${#missing_deps[@]} -gt 0 ]]; then
        print_error "Missing dependencies: ${missing_deps[*]}"
        log_error "Missing dependencies: ${missing_deps[*]}"
        print_info "Installing missing dependencies..."
        
        # Update package list
        apt update
        
        # Install WireGuard
        if [[ " ${missing_deps[*]} " =~ " wireguard " ]] || [[ " ${missing_deps[*]} " =~ " wg " ]]; then
            apt install -y wireguard wireguard-tools
        fi
        
        # Install QR code generator
        if [[ " ${missing_deps[*]} " =~ " qrencode " ]]; then
            apt install -y qrencode
        fi
        
        print_success "Dependencies installed successfully"
    fi
}

setup_directories() {
    local dirs=("$CLIENT_DIR" "$KEYS_DIR" "$LOG_DIR" "$TEMPLATES_DIR")
    
    for dir in "${dirs[@]}"; do
        if [[ ! -d "$dir" ]]; then
            mkdir -p "$dir"
            chmod 700 "$dir"
            log_info "Created directory: $dir"
        fi
    done
    
    # Create client database if it doesn't exist
    if [[ ! -f "$CLIENT_DB" ]]; then
        echo "# Client Database" > "$CLIENT_DB"
        echo "# Format: CLIENT_NAME,IP_ADDRESS,PUBLIC_KEY,PRIVATE_KEY,PRESHARED_KEY,CREATED_DATE" >> "$CLIENT_DB"
        chmod 600 "$CLIENT_DB"
        log_info "Created client database: $CLIENT_DB"
    fi
}

#=============================================================================
# Key Generation Functions
#=============================================================================

generate_keys() {
    local key_name="$1"
    local private_key_file="$KEYS_DIR/${key_name}_private.key"
    local public_key_file="$KEYS_DIR/${key_name}_public.key"
    
    # Generate private key
    wg genkey > "$private_key_file"
    chmod 600 "$private_key_file"
    
    # Generate public key from private key
    wg pubkey < "$private_key_file" > "$public_key_file"
    chmod 600 "$public_key_file"
    
    log_info "Generated key pair for: $key_name"
}

generate_preshared_key() {
    local psk_file="$1"
    wg genpsk > "$psk_file"
    chmod 600 "$psk_file"
    log_info "Generated preshared key: $psk_file"
}

#=============================================================================
# IP Management Functions
#=============================================================================

get_next_client_ip() {
    local base_ip="10.66.66"
    local start_host=2
    local max_host=$((start_host + MAX_CLIENTS - 1))
    
    # Read existing IPs from client database
    local used_ips=()
    if [[ -f "$CLIENT_DB" ]]; then
        while IFS=',' read -r name ip _ _ _ _; do
            [[ $name =~ ^# ]] && continue  # Skip comments
            [[ -n "$ip" ]] && used_ips+=("$ip")
        done < "$CLIENT_DB"
    fi
    
    # Find next available IP
    for ((host=start_host; host<=max_host; host++)); do
        local test_ip="$base_ip.$host"
        local ip_used=false
        
        for used_ip in "${used_ips[@]}"; do
            if [[ "$used_ip" == "$test_ip" ]]; then
                ip_used=true
                break
            fi
        done
        
        if [[ "$ip_used" == false ]]; then
            echo "$test_ip"
            return 0
        fi
    done
    
    # No available IP found
    return 1
}

#=============================================================================
# Server Configuration Functions
#=============================================================================

setup_server() {
    print_info "Setting up WireGuard server..."
    
    # Generate server keys if they don't exist
    if [[ ! -f "$KEYS_DIR/server_private.key" ]]; then
        generate_keys "server"
        print_success "Generated server keys"
    fi
    
    local server_private_key=$(cat "$KEYS_DIR/server_private.key")
    
    # Create server configuration
    cat > "$SERVER_CONFIG" << EOF
[Interface]
PrivateKey = $server_private_key
Address = $SERVER_IP/24
ListenPort = $WG_PORT
PostUp = iptables -A FORWARD -i %i -j ACCEPT; iptables -A FORWARD -o %i -j ACCEPT; iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE
PostDown = iptables -D FORWARD -i %i -j ACCEPT; iptables -D FORWARD -o %i -j ACCEPT; iptables -t nat -D POSTROUTING -o eth0 -j MASQUERADE

EOF
    
    chmod 600 "$SERVER_CONFIG"
    
    # Enable IP forwarding
    echo 'net.ipv4.ip_forward=1' | tee -a /etc/sysctl.conf > /dev/null
    sysctl -p > /dev/null
    
    # Enable and start WireGuard service
    systemctl enable wg-quick@wg0
    
    print_success "Server configuration created"
    log_info "WireGuard server configured successfully"
}

reload_server() {
    if systemctl is-active --quiet wg-quick@wg0; then
        systemctl restart wg-quick@wg0
        log_info "WireGuard server reloaded"
    else
        systemctl start wg-quick@wg0
        log_info "WireGuard server started"
    fi
}

#=============================================================================
# Client Management Functions
#=============================================================================

add_client() {
    local client_name="$1"
    
    # Validate client name
    if [[ -z "$client_name" ]]; then
        print_error "Client name cannot be empty"
        return 1
    fi
    
    if [[ "$client_name" =~ [^a-zA-Z0-9_-] ]]; then
        print_error "Client name can only contain letters, numbers, hyphens, and underscores"
        return 1
    fi
    
    # Check if client already exists
    if grep -q "^$client_name," "$CLIENT_DB" 2>/dev/null; then
        print_error "Client '$client_name' already exists"
        return 1
    fi
    
    # Get next available IP
    local client_ip
    if ! client_ip=$(get_next_client_ip); then
        print_error "No available IP addresses (maximum $MAX_CLIENTS clients)"
        return 1
    fi
    
    print_info "Adding client: $client_name (IP: $client_ip)"
    
    # Generate client keys
    generate_keys "$client_name"
    
    # Generate preshared key
    local psk_file="$KEYS_DIR/${client_name}_preshared.key"
    generate_preshared_key "$psk_file"
    
    # Read keys
    local client_private_key=$(cat "$KEYS_DIR/${client_name}_private.key")
    local client_public_key=$(cat "$KEYS_DIR/${client_name}_public.key")
    local server_public_key=$(cat "$KEYS_DIR/server_public.key")
    local preshared_key=$(cat "$psk_file")
    
    # Add client to database
    local created_date=$(date '+%Y-%m-%d %H:%M:%S')
    echo "$client_name,$client_ip,$client_public_key,$client_private_key,$preshared_key,$created_date" >> "$CLIENT_DB"
    
    # Add client to server configuration
    cat >> "$SERVER_CONFIG" << EOF

[Peer]
# Client: $client_name
PublicKey = $client_public_key
PresharedKey = $preshared_key
AllowedIPs = $client_ip/32

EOF
    
    # Create client configuration
    local client_config_file="$CLIENT_DIR/${client_name}.conf"
    cat > "$client_config_file" << EOF
[Interface]
PrivateKey = $client_private_key
Address = $client_ip/32
DNS = 8.8.8.8, 1.1.1.1

[Peer]
PublicKey = $server_public_key
PresharedKey = $preshared_key
Endpoint = YOUR_SERVER_IP:$WG_PORT
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25

EOF
    
    chmod 600 "$client_config_file"
    
    # Generate QR code
    local qr_file="$CLIENT_DIR/${client_name}_qr.png"
    qrencode -t png -o "$qr_file" < "$client_config_file"
    
    # Reload server
    reload_server
    
    print_success "Client '$client_name' added successfully"
    print_info "Configuration file: $client_config_file"
    print_info "QR code: $qr_file"
    
    log_info "Added client: $client_name (IP: $client_ip)"
    
    return 0
}

del_client() {
    local client_name="$1"
    
    if [[ -z "$client_name" ]]; then
        print_error "Client name cannot be empty"
        return 1
    fi
    
    # Check if client exists
    if ! grep -q "^$client_name," "$CLIENT_DB" 2>/dev/null; then
        print_error "Client '$client_name' does not exist"
        return 1
    fi
    
    print_info "Removing client: $client_name"
    
    # Remove client from database
    grep -v "^$client_name," "$CLIENT_DB" > "${CLIENT_DB}.tmp" && mv "${CLIENT_DB}.tmp" "$CLIENT_DB"
    
    # Remove client files
    rm -f "$KEYS_DIR/${client_name}_private.key"
    rm -f "$KEYS_DIR/${client_name}_public.key"
    rm -f "$KEYS_DIR/${client_name}_preshared.key"
    rm -f "$CLIENT_DIR/${client_name}.conf"
    rm -f "$CLIENT_DIR/${client_name}_qr.png"
    
    # Rebuild server configuration
    rebuild_server_config
    
    # Reload server
    reload_server
    
    print_success "Client '$client_name' removed successfully"
    log_info "Removed client: $client_name"
    
    return 0
}

list_clients() {
    print_info "Client List:"
    echo
    printf "%-20s %-15s %-10s %-20s\n" "Name" "IP Address" "Status" "Created"
    printf "%-20s %-15s %-10s %-20s\n" "----" "----------" "------" "-------"
    
    local client_count=0
    
    if [[ -f "$CLIENT_DB" ]]; then
        while IFS=',' read -r name ip public_key private_key preshared_key created_date; do
            [[ $name =~ ^# ]] && continue  # Skip comments
            [[ -z "$name" ]] && continue   # Skip empty lines
            
            local status="Active"
            if ! systemctl is-active --quiet wg-quick@wg0; then
                status="Inactive"
            fi
            
            printf "%-20s %-15s %-10s %-20s\n" "$name" "$ip" "$status" "$created_date"
            ((client_count++))
        done < "$CLIENT_DB"
    fi
    
    echo
    print_info "Total clients: $client_count/$MAX_CLIENTS"
}

show_client_qr() {
    local client_name="$1"
    
    if [[ -z "$client_name" ]]; then
        print_error "Client name cannot be empty"
        return 1
    fi
    
    # Check if client exists
    if ! grep -q "^$client_name," "$CLIENT_DB" 2>/dev/null; then
        print_error "Client '$client_name' does not exist"
        return 1
    fi
    
    local qr_file="$CLIENT_DIR/${client_name}_qr.png"
    local config_file="$CLIENT_DIR/${client_name}.conf"
    
    # Regenerate QR code if it doesn't exist
    if [[ ! -f "$qr_file" ]] && [[ -f "$config_file" ]]; then
        qrencode -t png -o "$qr_file" < "$config_file"
        print_info "Regenerated QR code for client: $client_name"
    fi
    
    if [[ -f "$qr_file" ]]; then
        print_success "QR code for client '$client_name': $qr_file"
        print_info "Configuration file: $config_file"
        
        # Display QR code in terminal if possible
        if command -v qrencode &> /dev/null; then
            echo
            print_info "QR Code (scan with your mobile device):"
            qrencode -t ansiutf8 < "$config_file"
        fi
    else
        print_error "QR code file not found for client: $client_name"
        return 1
    fi
}

rebuild_server_config() {
    print_info "Rebuilding server configuration..."
    
    local server_private_key=$(cat "$KEYS_DIR/server_private.key")
    
    # Create base server configuration
    cat > "$SERVER_CONFIG" << EOF
[Interface]
PrivateKey = $server_private_key
Address = $SERVER_IP/24
ListenPort = $WG_PORT
PostUp = iptables -A FORWARD -i %i -j ACCEPT; iptables -A FORWARD -o %i -j ACCEPT; iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE
PostDown = iptables -D FORWARD -i %i -j ACCEPT; iptables -D FORWARD -o %i -j ACCEPT; iptables -t nat -D POSTROUTING -o eth0 -j MASQUERADE

EOF
    
    # Add all active clients
    if [[ -f "$CLIENT_DB" ]]; then
        while IFS=',' read -r name ip public_key private_key preshared_key created_date; do
            [[ $name =~ ^# ]] && continue  # Skip comments
            [[ -z "$name" ]] && continue   # Skip empty lines
            
            cat >> "$SERVER_CONFIG" << EOF

[Peer]
# Client: $name
PublicKey = $public_key
PresharedKey = $preshared_key
AllowedIPs = $ip/32

EOF
        done < "$CLIENT_DB"
    fi
    
    chmod 600 "$SERVER_CONFIG"
    print_success "Server configuration rebuilt"
}

#=============================================================================
# Server Status Functions
#=============================================================================

server_status() {
    print_info "WireGuard Server Status:"
    echo
    
    # Service status
    if systemctl is-active --quiet wg-quick@wg0; then
        print_success "Service Status: Active"
    else
        print_error "Service Status: Inactive"
    fi
    
    # Interface status
    if ip link show "$WG_INTERFACE" &> /dev/null; then
        print_success "Interface Status: Up"
        
        # Show interface details
        echo
        print_info "Interface Details:"
        wg show "$WG_INTERFACE" 2>/dev/null || print_warn "No WireGuard information available"
    else
        print_error "Interface Status: Down"
    fi
    
    # Port status
    echo
    print_info "Port Status:"
    if netstat -ulpn | grep -q ":$WG_PORT "; then
        print_success "WireGuard port $WG_PORT: Listening"
    else
        print_error "WireGuard port $WG_PORT: Not listening"
    fi
    
    # Client count
    echo
    local client_count=0
    if [[ -f "$CLIENT_DB" ]]; then
        client_count=$(grep -c -v '^#' "$CLIENT_DB" 2>/dev/null || echo 0)
    fi
    print_info "Connected Clients: $client_count/$MAX_CLIENTS"
    
    # System resources
    echo
    print_info "System Resources:"
    echo "CPU Usage: $(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)%"
    echo "Memory Usage: $(free | grep Mem | awk '{printf("%.1f%%", $3/$2 * 100.0)}')"
    echo "Disk Usage: $(df -h / | awk 'NR==2{printf "%s", $5}')"
}

#=============================================================================
# Main Script Logic
#=============================================================================

show_usage() {
    echo "WireGuard VPN Management Script"
    echo
    echo "Usage: $0 <command> [arguments]"
    echo
    echo "Commands:"
    echo "  setup                     - Initial server setup"
    echo "  add-client <name>         - Add a new client"
    echo "  del-client <name>         - Remove a client"
    echo "  list-clients              - List all clients"
    echo "  show-client-qr <name>     - Show QR code for client"
    echo "  server-status             - Show server status"
    echo "  help                      - Show this help message"
    echo
    echo "Examples:"
    echo "  $0 setup"
    echo "  $0 add-client john-phone"
    echo "  $0 del-client john-phone"
    echo "  $0 list-clients"
    echo "  $0 show-client-qr john-phone"
    echo "  $0 server-status"
}

main() {
    # Check for command line arguments
    if [[ $# -eq 0 ]]; then
        show_usage
        exit 1
    fi
    
    local command="$1"
    shift
    
    # Create log directory if it doesn't exist
    mkdir -p "$LOG_DIR"
    chmod 700 "$LOG_DIR"
    
    case "$command" in
        "setup")
            check_root
            check_dependencies
            setup_directories
            setup_server
            print_success "WireGuard server setup completed successfully"
            ;;
        "add-client")
            check_root
            if [[ $# -eq 0 ]]; then
                print_error "Client name is required"
                echo "Usage: $0 add-client <client-name>"
                exit 1
            fi
            add_client "$1"
            ;;
        "del-client")
            check_root
            if [[ $# -eq 0 ]]; then
                print_error "Client name is required"
                echo "Usage: $0 del-client <client-name>"
                exit 1
            fi
            del_client "$1"
            ;;
        "list-clients")
            list_clients
            ;;
        "show-client-qr")
            if [[ $# -eq 0 ]]; then
                print_error "Client name is required"
                echo "Usage: $0 show-client-qr <client-name>"
                exit 1
            fi
            show_client_qr "$1"
            ;;
        "server-status")
            server_status
            ;;
        "help"|"--help"|"-h")
            show_usage
            ;;
        *)
            print_error "Unknown command: $command"
            echo
            show_usage
            exit 1
            ;;
    esac
}

# Run main function with all arguments
main "$@"