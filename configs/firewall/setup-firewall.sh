#!/bin/bash

# ==============================================================================
# VPN Server Firewall Configuration Script
# ==============================================================================
# 
# Description: Configure UFW and iptables for VPN server security
# This script sets up firewall rules for the VPN server
# ==============================================================================

set -euo pipefail

# Configuration
VPN_SUBNET="10.66.66.0/24"
VPN_INTERFACE="wg0"
WAN_INTERFACE="eth0"  # Main network interface, adjust if needed

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() {
    echo -e "${GREEN}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1"
}

log_error() {
    echo -e "${RED}[$(date '+%Y-%m-%d %H:%M:%S')] ERROR:${NC} $1" >&2
}

log_warning() {
    echo -e "${YELLOW}[$(date '+%Y-%m-%d %H:%M:%S')] WARNING:${NC} $1"
}

# Check if running as root
if [[ $EUID -ne 0 ]]; then
    log_error "This script must be run as root"
    exit 1
fi

# Detect primary network interface
detect_wan_interface() {
    local interface=$(ip route | grep default | awk '{print $5}' | head -1)
    if [[ -n "$interface" ]]; then
        WAN_INTERFACE="$interface"
        log "Detected WAN interface: $WAN_INTERFACE"
    else
        log_warning "Could not detect WAN interface, using default: $WAN_INTERFACE"
    fi
}

# Configure UFW (Uncomplicated Firewall)
configure_ufw() {
    log "Configuring UFW firewall rules..."
    
    # Reset UFW to default state
    ufw --force reset
    
    # Set default policies
    ufw default deny incoming
    ufw default allow outgoing
    ufw default deny forward
    
    # Allow SSH (CRITICAL - don't lock yourself out!)
    ufw allow ssh
    ufw allow 22/tcp
    
    # Allow HTTP and HTTPS for Let's Encrypt and web interface
    ufw allow 80/tcp comment "HTTP for Let's Encrypt"
    ufw allow 443/tcp comment "HTTPS for VPN web interface"
    
    # Allow WireGuard UDP (only from VPN subnet for security)
    ufw allow from $VPN_SUBNET to any port 51820 proto udp comment "WireGuard VPN"
    
    # Allow VPN traffic between clients
    ufw allow from $VPN_SUBNET to $VPN_SUBNET comment "VPN client to client"
    
    # Allow established and related connections
    ufw allow out 53 comment "DNS queries"
    ufw allow out 123 comment "NTP time sync"
    
    # Rate limiting for SSH (prevent brute force)
    ufw limit ssh/tcp
    
    # Enable UFW
    ufw --force enable
    
    log "UFW configuration completed"
}

# Configure iptables for NAT and forwarding
configure_iptables() {
    log "Configuring iptables for NAT and forwarding..."
    
    # Flush existing rules
    iptables -F
    iptables -t nat -F
    iptables -t mangle -F
    
    # Set default policies
    iptables -P INPUT ACCEPT
    iptables -P FORWARD DROP
    iptables -P OUTPUT ACCEPT
    
    # Allow loopback traffic
    iptables -A INPUT -i lo -j ACCEPT
    iptables -A OUTPUT -o lo -j ACCEPT
    
    # Allow established and related connections
    iptables -A INPUT -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
    iptables -A FORWARD -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
    
    # Allow VPN traffic forwarding
    iptables -A FORWARD -i $VPN_INTERFACE -j ACCEPT
    iptables -A FORWARD -o $VPN_INTERFACE -j ACCEPT
    
    # NAT for VPN traffic
    iptables -t nat -A POSTROUTING -s $VPN_SUBNET -o $WAN_INTERFACE -j MASQUERADE
    
    # Allow VPN to access internet
    iptables -A FORWARD -i $VPN_INTERFACE -o $WAN_INTERFACE -j ACCEPT
    iptables -A FORWARD -i $WAN_INTERFACE -o $VPN_INTERFACE -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
    
    # Log dropped packets (optional, for debugging)
    iptables -A INPUT -j LOG --log-prefix "IPT-INPUT-DROP: " --log-level 4
    iptables -A FORWARD -j LOG --log-prefix "IPT-FORWARD-DROP: " --log-level 4
    
    # Save iptables rules
    if command -v iptables-save &> /dev/null; then
        iptables-save > /etc/iptables/rules.v4
        log "iptables rules saved to /etc/iptables/rules.v4"
    else
        log_warning "iptables-persistent not found, rules may not persist after reboot"
    fi
    
    log "iptables configuration completed"
}

# Configure IPv6 (if needed)
configure_ipv6() {
    log "Configuring IPv6 firewall rules..."
    
    # Check if IPv6 is enabled
    if [[ -f /proc/net/if_inet6 ]]; then
        # Similar rules for IPv6
        ip6tables -F
        ip6tables -P INPUT DROP
        ip6tables -P FORWARD DROP
        ip6tables -P OUTPUT ACCEPT
        
        # Allow loopback
        ip6tables -A INPUT -i lo -j ACCEPT
        ip6tables -A OUTPUT -o lo -j ACCEPT
        
        # Allow established connections
        ip6tables -A INPUT -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
        
        # Allow SSH
        ip6tables -A INPUT -p tcp --dport 22 -j ACCEPT
        
        # Allow HTTP/HTTPS
        ip6tables -A INPUT -p tcp --dport 80 -j ACCEPT
        ip6tables -A INPUT -p tcp --dport 443 -j ACCEPT
        
        # Allow ICMPv6
        ip6tables -A INPUT -p icmpv6 -j ACCEPT
        
        # Save IPv6 rules
        if command -v ip6tables-save &> /dev/null; then
            ip6tables-save > /etc/iptables/rules.v6
            log "IPv6 iptables rules saved"
        fi
    else
        log "IPv6 not available, skipping IPv6 configuration"
    fi
}

# Create firewall management script
create_firewall_script() {
    log "Creating firewall management script..."
    
    cat > /opt/vpn-server/scripts/firewall-manager.sh << 'EOF'
#!/bin/bash

# VPN Firewall Management Script

case "$1" in
    status)
        echo "=== UFW Status ==="
        ufw status verbose
        echo ""
        echo "=== iptables Rules ==="
        iptables -L -n -v
        echo ""
        echo "=== NAT Rules ==="
        iptables -t nat -L -n -v
        ;;
    reset)
        echo "Resetting firewall rules..."
        ufw --force reset
        iptables -F
        iptables -t nat -F
        iptables -t mangle -F
        echo "Firewall reset completed"
        ;;
    reload)
        echo "Reloading firewall rules..."
        if [[ -f /etc/iptables/rules.v4 ]]; then
            iptables-restore < /etc/iptables/rules.v4
        fi
        if [[ -f /etc/iptables/rules.v6 ]]; then
            ip6tables-restore < /etc/iptables/rules.v6
        fi
        ufw reload
        echo "Firewall rules reloaded"
        ;;
    block-ip)
        if [[ -n "$2" ]]; then
            ufw deny from "$2"
            echo "Blocked IP: $2"
        else
            echo "Usage: $0 block-ip <ip-address>"
        fi
        ;;
    unblock-ip)
        if [[ -n "$2" ]]; then
            ufw delete deny from "$2"
            echo "Unblocked IP: $2"
        else
            echo "Usage: $0 unblock-ip <ip-address>"
        fi
        ;;
    *)
        echo "VPN Firewall Manager"
        echo "==================="
        echo ""
        echo "Usage: $0 {status|reset|reload|block-ip|unblock-ip}"
        echo ""
        echo "Commands:"
        echo "  status           Show current firewall status"
        echo "  reset            Reset all firewall rules"
        echo "  reload           Reload saved firewall rules"
        echo "  block-ip <ip>    Block specific IP address"
        echo "  unblock-ip <ip>  Unblock specific IP address"
        exit 1
        ;;
esac
EOF
    
    chmod +x /opt/vpn-server/scripts/firewall-manager.sh
    log "Firewall management script created"
}

# Enable IP forwarding permanently
enable_ip_forwarding() {
    log "Enabling IP forwarding..."
    
    # Enable for current session
    sysctl -w net.ipv4.ip_forward=1
    sysctl -w net.ipv6.conf.all.forwarding=1
    
    # Make permanent
    cat >> /etc/sysctl.conf << EOF

# VPN Server IP Forwarding
net.ipv4.ip_forward=1
net.ipv6.conf.all.forwarding=1
net.ipv4.conf.all.src_valid_mark=1
EOF
    
    # Apply settings
    sysctl -p
    
    log "IP forwarding enabled"
}

# Install fail2ban for additional security
install_fail2ban() {
    log "Installing and configuring fail2ban..."
    
    # Install fail2ban
    if command -v apt-get &> /dev/null; then
        apt-get update
        apt-get install -y fail2ban
    elif command -v yum &> /dev/null; then
        yum install -y fail2ban
    else
        log_warning "Could not install fail2ban automatically"
        return
    fi
    
    # Configure fail2ban for SSH
    cat > /etc/fail2ban/jail.local << EOF
[DEFAULT]
bantime = 3600
findtime = 600
maxretry = 5
backend = systemd

[sshd]
enabled = true
port = ssh
filter = sshd
logpath = /var/log/auth.log
maxretry = 3
bantime = 7200

[nginx-http-auth]
enabled = true
filter = nginx-http-auth
logpath = /var/log/nginx/error.log
maxretry = 5
bantime = 3600

[caddy-limit]
enabled = true
filter = caddy-limit
logpath = /var/log/caddy.log
maxretry = 10
bantime = 1800
EOF
    
    # Create custom filter for Caddy
    cat > /etc/fail2ban/filter.d/caddy-limit.conf << EOF
[Definition]
failregex = ^.*"remote_ip":"<HOST>".*"status":429.*$
            ^.*"remote_ip":"<HOST>".*"status":403.*$
ignoreregex =
EOF
    
    # Enable and start fail2ban
    systemctl enable fail2ban
    systemctl start fail2ban
    
    log "fail2ban configured and started"
}

# Main execution
main() {
    log "Starting VPN server firewall configuration..."
    
    # Detect network interface
    detect_wan_interface
    
    # Configure firewall components
    enable_ip_forwarding
    configure_ufw
    configure_iptables
    configure_ipv6
    create_firewall_script
    install_fail2ban
    
    log "Firewall configuration completed successfully!"
    echo ""
    echo "📋 Firewall Summary:"
    echo "   UFW: Enabled with VPN-specific rules"
    echo "   iptables: Configured for NAT and forwarding"
    echo "   IP Forwarding: Enabled"
    echo "   fail2ban: Installed and configured"
    echo ""
    echo "🔧 Management Commands:"
    echo "   Check status: /opt/vpn-server/scripts/firewall-manager.sh status"
    echo "   Reset rules: /opt/vpn-server/scripts/firewall-manager.sh reset"
    echo "   Block IP: /opt/vpn-server/scripts/firewall-manager.sh block-ip <ip>"
    echo ""
    echo "⚠️  Security Note:"
    echo "   SSH access is allowed from anywhere for administration"
    echo "   VPN ports are restricted to the VPN subnet only"
    echo "   Consider restricting SSH to specific IPs for production use"
}

# Run main function
main "$@"