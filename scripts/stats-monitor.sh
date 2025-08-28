#!/bin/bash

# ==============================================================================
# VPN Statistics Monitor Script
# ==============================================================================
# 
# Description: Monitor VPN client statistics and generate reports
# Runs as a systemd service to continuously monitor bandwidth usage
# Output: JSON files for web interface consumption
# ==============================================================================

set -euo pipefail

# Configuration
CONFIG_DIR="/etc/vpn-server"
STATS_DIR="/var/lib/vpn-server/stats"
WG_INTERFACE="wg0"
LOG_FILE="/var/log/vpn-stats.log"
UPDATE_INTERVAL=30

# Colors for logging
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Logging functions
log() {
    echo -e "${GREEN}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1" | tee -a "$LOG_FILE"
}

log_error() {
    echo -e "${RED}[$(date '+%Y-%m-%d %H:%M:%S')] ERROR:${NC} $1" | tee -a "$LOG_FILE" >&2
}

log_warning() {
    echo -e "${YELLOW}[$(date '+%Y-%m-%d %H:%M:%S')] WARNING:${NC} $1" | tee -a "$LOG_FILE"
}

log_info() {
    echo -e "${BLUE}[$(date '+%Y-%m-%d %H:%M:%S')] INFO:${NC} $1" | tee -a "$LOG_FILE"
}

# Initialize directories
init_dirs() {
    mkdir -p "$STATS_DIR"
    mkdir -p "$(dirname "$LOG_FILE")"
    
    # Create initial files if they don't exist
    if [[ ! -f "$STATS_DIR/current.json" ]]; then
        echo '{"timestamp":"","clients":[],"server":{"status":"initializing"}}' > "$STATS_DIR/current.json"
    fi
    
    if [[ ! -f "$STATS_DIR/daily.json" ]]; then
        echo '{"date":"","clients":[],"totals":{"bytes_in":0,"bytes_out":0}}' > "$STATS_DIR/daily.json"
    fi
}

# Get client name from public key
get_client_name() {
    local public_key="$1"
    local client_name="Unknown"
    
    if [[ -d "$CONFIG_DIR/clients" ]]; then
        for client_dir in "$CONFIG_DIR/clients"/*; do
            if [[ -d "$client_dir" ]]; then
                local info_file="$client_dir/info.json"
                if [[ -f "$info_file" ]]; then
                    local client_key=$(jq -r '.publicKey' "$info_file" 2>/dev/null || echo "")
                    if [[ "$client_key" == "$public_key" ]]; then
                        client_name=$(jq -r '.name' "$info_file" 2>/dev/null || basename "$client_dir")
                        break
                    fi
                fi
            fi
        done
    fi
    
    echo "$client_name"
}

# Get server public key
get_server_public_key() {
    if [[ -f "$CONFIG_DIR/wireguard/server_public.key" ]]; then
        cat "$CONFIG_DIR/wireguard/server_public.key"
    else
        echo ""
    fi
}

# Parse WireGuard statistics
parse_wg_stats() {
    local stats_file="$STATS_DIR/wg_dump.txt"
    
    # Get WireGuard dump
    if command -v wg &> /dev/null && wg show "$WG_INTERFACE" &> /dev/null; then
        wg show "$WG_INTERFACE" dump > "$stats_file" 2>/dev/null || {
            log_warning "Failed to get WireGuard statistics"
            return 1
        }
    else
        log_warning "WireGuard interface $WG_INTERFACE not found or wg command not available"
        return 1
    fi
    
    local server_public_key=$(get_server_public_key)
    local current_time=$(date -Iseconds)
    
    # Start building JSON
    {
        echo "{"
        echo "  \"timestamp\": \"$current_time\","
        echo "  \"server\": {"
        echo "    \"status\": \"online\","
        echo "    \"interface\": \"$WG_INTERFACE\","
        echo "    \"public_key\": \"$server_public_key\","
        echo "    \"clients_connected\": 0"
        echo "  },"
        echo "  \"clients\": ["
        
        local first_client=true
        local connected_count=0
        
        # Parse each line (skip header if present)
        while IFS=$'\t' read -r public_key preshared_key endpoint allowed_ips latest_handshake rx_bytes tx_bytes persistent_keepalive; do
            # Skip server's own entry and empty lines
            if [[ -z "$public_key" ]] || [[ "$public_key" == "$server_public_key" ]]; then
                continue
            fi
            
            # Add comma if not first client
            if [[ "$first_client" == true ]]; then
                first_client=false
            else
                echo ","
            fi
            
            # Get client information
            local client_name=$(get_client_name "$public_key")
            local client_ip=$(echo "$allowed_ips" | cut -d'/' -f1)
            
            # Determine connection status
            local is_connected=false
            local last_seen="never"
            
            if [[ -n "$latest_handshake" ]] && [[ "$latest_handshake" != "0" ]]; then
                # Check if handshake is recent (within 3 minutes)
                local handshake_age=$(($(date +%s) - latest_handshake))
                if [[ $handshake_age -lt 180 ]]; then
                    is_connected=true
                    connected_count=$((connected_count + 1))
                fi
                last_seen=$(date -d "@$latest_handshake" -Iseconds 2>/dev/null || echo "$latest_handshake")
            fi
            
            # Format bytes
            local rx_mb=$((${rx_bytes:-0} / 1024 / 1024))
            local tx_mb=$((${tx_bytes:-0} / 1024 / 1024))
            
            # Client JSON entry
            echo "    {"
            echo "      \"name\": \"$client_name\","
            echo "      \"ip\": \"$client_ip\","
            echo "      \"public_key\": \"$public_key\","
            echo "      \"endpoint\": \"${endpoint:-unknown}\","
            echo "      \"allowed_ips\": \"$allowed_ips\","
            echo "      \"connected\": $is_connected,"
            echo "      \"last_seen\": \"$last_seen\","
            echo "      \"latest_handshake\": \"${latest_handshake:-0}\","
            echo "      \"bytes_received\": ${rx_bytes:-0},"
            echo "      \"bytes_sent\": ${tx_bytes:-0},"
            echo "      \"mb_received\": $rx_mb,"
            echo "      \"mb_sent\": $tx_mb,"
            echo "      \"total_mb\": $((rx_mb + tx_mb)),"
            echo "      \"persistent_keepalive\": \"${persistent_keepalive:-0}\""
            echo -n "    }"
            
        done < "$stats_file"
        
        echo ""
        echo "  ]"
        echo "}"
        
        # Update server connected count
    } > "$STATS_DIR/current.json.tmp"
    
    # Update connected count in the JSON
    jq ".server.clients_connected = $connected_count" "$STATS_DIR/current.json.tmp" > "$STATS_DIR/current.json"
    rm -f "$STATS_DIR/current.json.tmp"
}

# Generate daily statistics
generate_daily_stats() {
    local today=$(date +%Y-%m-%d)
    local daily_file="$STATS_DIR/daily_$(date +%Y_%m_%d).json"
    
    # Initialize daily stats if new day
    if [[ ! -f "$daily_file" ]]; then
        echo "{\"date\":\"$today\",\"clients\":[],\"totals\":{\"bytes_in\":0,\"bytes_out\":0}}" > "$daily_file"
    fi
    
    # Copy current to daily (this is a simple approach; in production you'd accumulate deltas)
    if [[ -f "$STATS_DIR/current.json" ]]; then
        cp "$STATS_DIR/current.json" "$STATS_DIR/daily.json"
        cp "$STATS_DIR/current.json" "$daily_file"
    fi
}

# Generate historical statistics
generate_history() {
    local history_file="$STATS_DIR/history.json"
    local current_time=$(date -Iseconds)
    
    # Read current stats
    if [[ -f "$STATS_DIR/current.json" ]]; then
        local total_clients=$(jq '.clients | length' "$STATS_DIR/current.json" 2>/dev/null || echo 0)
        local connected_clients=$(jq '.server.clients_connected' "$STATS_DIR/current.json" 2>/dev/null || echo 0)
        local total_bytes_in=$(jq '[.clients[].bytes_received] | add // 0' "$STATS_DIR/current.json" 2>/dev/null || echo 0)
        local total_bytes_out=$(jq '[.clients[].bytes_sent] | add // 0' "$STATS_DIR/current.json" 2>/dev/null || echo 0)
        
        # Create history entry
        local history_entry=$(cat << EOF
{
  "timestamp": "$current_time",
  "total_clients": $total_clients,
  "connected_clients": $connected_clients,
  "total_bytes_in": $total_bytes_in,
  "total_bytes_out": $total_bytes_out
}
EOF
)
        
        # Initialize history file if it doesn't exist
        if [[ ! -f "$history_file" ]]; then
            echo '{"entries":[]}' > "$history_file"
        fi
        
        # Add entry to history (keep last 1000 entries)
        jq ".entries += [$history_entry] | .entries = .entries[-1000:]" "$history_file" > "$history_file.tmp"
        mv "$history_file.tmp" "$history_file"
    fi
}

# Health check
health_check() {
    local health_file="$STATS_DIR/health.json"
    local current_time=$(date -Iseconds)
    local status="healthy"
    local issues=()
    
    # Check WireGuard interface
    if ! ip link show "$WG_INTERFACE" &> /dev/null; then
        status="unhealthy"
        issues+=("WireGuard interface $WG_INTERFACE not found")
    fi
    
    # Check if wg command works
    if ! command -v wg &> /dev/null; then
        status="warning"
        issues+=("WireGuard tools not available")
    fi
    
    # Check disk space
    local disk_usage=$(df "$STATS_DIR" | tail -1 | awk '{print $5}' | sed 's/%//')
    if [[ $disk_usage -gt 90 ]]; then
        status="warning"
        issues+=("Disk usage high: ${disk_usage}%")
    fi
    
    # Check if stats directory is writable
    if [[ ! -w "$STATS_DIR" ]]; then
        status="unhealthy"
        issues+=("Stats directory not writable")
    fi
    
    # Generate health JSON
    {
        echo "{"
        echo "  \"timestamp\": \"$current_time\","
        echo "  \"status\": \"$status\","
        echo "  \"disk_usage\": \"${disk_usage}%\","
        echo "  \"wireguard_interface\": \"$WG_INTERFACE\","
        echo "  \"issues\": ["
        
        local first=true
        for issue in "${issues[@]}"; do
            if [[ "$first" == true ]]; then
                first=false
            else
                echo ","
            fi
            echo "    \"$issue\""
        done
        
        echo "  ]"
        echo "}"
    } > "$health_file"
}

# Cleanup old files
cleanup_old_files() {
    # Remove daily files older than 30 days
    find "$STATS_DIR" -name "daily_*.json" -mtime +30 -delete 2>/dev/null || true
    
    # Compress old log files
    if [[ -f "$LOG_FILE" ]] && [[ $(stat -f%z "$LOG_FILE" 2>/dev/null || stat -c%s "$LOG_FILE" 2>/dev/null || echo 0) -gt 10485760 ]]; then
        # Rotate log if it's larger than 10MB
        mv "$LOG_FILE" "$LOG_FILE.$(date +%Y%m%d)"
        gzip "$LOG_FILE.$(date +%Y%m%d)" 2>/dev/null || true
        touch "$LOG_FILE"
    fi
    
    # Remove old compressed logs (keep 7 days)
    find "$(dirname "$LOG_FILE")" -name "$(basename "$LOG_FILE").*.gz" -mtime +7 -delete 2>/dev/null || true
}

# Signal handler for graceful shutdown
shutdown_handler() {
    log "Received shutdown signal, stopping stats monitor..."
    exit 0
}

# Main monitoring loop
main_loop() {
    log "Starting VPN statistics monitor"
    log_info "Update interval: $UPDATE_INTERVAL seconds"
    log_info "Stats directory: $STATS_DIR"
    
    # Initialize
    init_dirs
    
    # Set up signal handlers
    trap shutdown_handler SIGTERM SIGINT
    
    local iteration=0
    
    while true; do
        iteration=$((iteration + 1))
        
        # Parse WireGuard statistics
        if parse_wg_stats; then
            log_info "Statistics updated (iteration $iteration)"
        else
            log_warning "Failed to parse WireGuard statistics (iteration $iteration)"
        fi
        
        # Generate additional statistics every 10 iterations (5 minutes)
        if [[ $((iteration % 10)) -eq 0 ]]; then
            generate_daily_stats
            generate_history
            health_check
            
            # Cleanup every 100 iterations (50 minutes)
            if [[ $((iteration % 100)) -eq 0 ]]; then
                cleanup_old_files
                log_info "Performed cleanup (iteration $iteration)"
            fi
        fi
        
        # Sleep
        sleep "$UPDATE_INTERVAL"
    done
}

# One-time statistics generation
generate_once() {
    log "Generating one-time statistics"
    init_dirs
    parse_wg_stats
    generate_daily_stats
    generate_history
    health_check
    log "One-time statistics generation complete"
}

# Show current statistics
show_stats() {
    echo "Current VPN Statistics"
    echo "======================"
    
    if [[ -f "$STATS_DIR/current.json" ]]; then
        echo ""
        echo "📊 Server Status:"
        jq -r '.server | "  Status: " + .status + "\n  Interface: " + .interface + "\n  Connected Clients: " + (.clients_connected | tostring)' "$STATS_DIR/current.json" 2>/dev/null || echo "  Unable to parse server status"
        
        echo ""
        echo "👥 Client Statistics:"
        jq -r '.clients[] | "  " + .name + " (" + .ip + "): " + (if .connected then "🟢 Connected" else "🔴 Disconnected" end) + " | " + (.total_mb | tostring) + " MB total"' "$STATS_DIR/current.json" 2>/dev/null || echo "  No client data available"
        
        echo ""
        echo "📈 Total Usage:"
        local total_in=$(jq '[.clients[].bytes_received] | add // 0' "$STATS_DIR/current.json" 2>/dev/null || echo 0)
        local total_out=$(jq '[.clients[].bytes_sent] | add // 0' "$STATS_DIR/current.json" 2>/dev/null || echo 0)
        local total_mb=$(( (total_in + total_out) / 1024 / 1024 ))
        echo "  Total In: $(( total_in / 1024 / 1024 )) MB"
        echo "  Total Out: $(( total_out / 1024 / 1024 )) MB"
        echo "  Combined: $total_mb MB"
    else
        echo "No statistics available. Run with 'monitor' or 'once' first."
    fi
    
    echo ""
    echo "📁 Files:"
    echo "  Current: $STATS_DIR/current.json"
    echo "  Daily: $STATS_DIR/daily.json"
    echo "  History: $STATS_DIR/history.json"
    echo "  Health: $STATS_DIR/health.json"
}

# Main function
case "${1:-monitor}" in
    monitor)
        main_loop
        ;;
    once)
        generate_once
        ;;
    show)
        show_stats
        ;;
    health)
        health_check
        if [[ -f "$STATS_DIR/health.json" ]]; then
            jq . "$STATS_DIR/health.json"
        fi
        ;;
    *)
        echo "VPN Statistics Monitor"
        echo "====================="
        echo ""
        echo "Usage: $0 {monitor|once|show|health}"
        echo ""
        echo "Commands:"
        echo "  monitor    Start continuous monitoring (default)"
        echo "  once       Generate statistics once and exit"
        echo "  show       Display current statistics"
        echo "  health     Check and display health status"
        echo ""
        echo "Files generated:"
        echo "  $STATS_DIR/current.json  - Current statistics"
        echo "  $STATS_DIR/daily.json    - Daily statistics"
        echo "  $STATS_DIR/history.json  - Historical data"
        echo "  $STATS_DIR/health.json   - Health check results"
        exit 1
        ;;
esac