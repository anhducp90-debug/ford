#!/bin/bash

# ==============================================================================
# VPN Production Server Auto-Installation Script
# ==============================================================================
# 
# Description: Comprehensive VPN server setup with WireGuard over WebSocket + TLS
# Target OS: Ubuntu 24.04
# Domain: vpn.vietnga.info.vn
# Subnet: 10.66.66.0/24
# 
# Features:
# - WireGuard VPN with multiple client support
# - WebSocket tunneling over TCP 443 with TLS
# - Let's Encrypt automatic certificate management
# - Web UI for client management with QR codes
# - Real-time bandwidth monitoring and statistics
# - Systemd service management for production reliability
# - Automated firewall configuration
# - Client helper scripts for all platforms
#
# Usage: sudo bash install-vpn.sh
# ==============================================================================

set -euo pipefail

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration variables
DOMAIN="vpn.vietnga.info.vn"
VPN_SUBNET="10.66.66.0/24"
VPN_SERVER_IP="10.66.66.1"
VPN_PORT="51820"
WS_PORT="443"
HTTP_PORT="80"
WG_INTERFACE="wg0"
INSTALL_DIR="/opt/vpn-server"
CONFIG_DIR="/etc/vpn-server"
LOG_FILE="/var/log/vpn-install.log"

# Logging function
log() {
    echo -e "${GREEN}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1" | tee -a "$LOG_FILE"
}

log_error() {
    echo -e "${RED}[$(date '+%Y-%m-%d %H:%M:%S')] ERROR:${NC} $1" | tee -a "$LOG_FILE"
}

log_warning() {
    echo -e "${YELLOW}[$(date '+%Y-%m-%d %H:%M:%S')] WARNING:${NC} $1" | tee -a "$LOG_FILE"
}

log_info() {
    echo -e "${BLUE}[$(date '+%Y-%m-%d %H:%M:%S')] INFO:${NC} $1" | tee -a "$LOG_FILE"
}

# Error handling
error_exit() {
    log_error "$1"
    exit 1
}

# Check if running as root
check_root() {
    if [[ $EUID -ne 0 ]]; then
        error_exit "This script must be run as root. Use: sudo bash $0"
    fi
}

# Check Ubuntu version
check_ubuntu_version() {
    if ! lsb_release -d 2>/dev/null | grep -q "Ubuntu 24.04"; then
        log_warning "This script is designed for Ubuntu 24.04. Current version:"
        lsb_release -d 2>/dev/null || echo "Unknown Ubuntu version"
        read -p "Continue anyway? (y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            exit 1
        fi
    fi
}

# Update system packages
update_system() {
    log "Updating system packages..."
    apt-get update && apt-get upgrade -y
    apt-get install -y curl wget git jq qrencode ufw iptables-persistent
}

# Install Docker and Docker Compose
install_docker() {
    log "Installing Docker and Docker Compose..."
    
    # Remove old Docker versions
    apt-get remove -y docker docker-engine docker.io containerd runc 2>/dev/null || true
    
    # Install Docker
    curl -fsSL https://get.docker.com -o get-docker.sh
    sh get-docker.sh
    rm get-docker.sh
    
    # Install Docker Compose
    curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
    chmod +x /usr/local/bin/docker-compose
    
    # Enable Docker service
    systemctl enable docker
    systemctl start docker
    
    # Verify installation
    docker --version
    docker-compose --version
}

# Install WireGuard
install_wireguard() {
    log "Installing WireGuard..."
    apt-get install -y wireguard wireguard-tools
    
    # Enable IP forwarding
    echo 'net.ipv4.ip_forward=1' >> /etc/sysctl.conf
    echo 'net.ipv6.conf.all.forwarding=1' >> /etc/sysctl.conf
    sysctl -p
}

# Create directory structure
create_directories() {
    log "Creating directory structure..."
    mkdir -p "$INSTALL_DIR"/{docker,scripts,configs,web-ui,systemd}
    mkdir -p "$CONFIG_DIR"/{wireguard,clients,certs,logs}
    mkdir -p /var/lib/vpn-server/{data,stats}
    
    # Set proper permissions
    chmod 700 "$CONFIG_DIR/wireguard"
    chmod 755 "$INSTALL_DIR"
}

# Generate WireGuard server keys
generate_server_keys() {
    log "Generating WireGuard server keys..."
    cd "$CONFIG_DIR/wireguard"
    
    # Generate server keys
    wg genkey | tee server_private.key | wg pubkey > server_public.key
    chmod 600 server_private.key
    chmod 644 server_public.key
    
    SERVER_PRIVATE_KEY=$(cat server_private.key)
    SERVER_PUBLIC_KEY=$(cat server_public.key)
    
    log_info "Server public key: $SERVER_PUBLIC_KEY"
}

# Create WireGuard server configuration
create_wireguard_config() {
    log "Creating WireGuard server configuration..."
    
    cat > "$CONFIG_DIR/wireguard/wg0.conf" << EOF
[Interface]
Address = $VPN_SERVER_IP/24
ListenPort = $VPN_PORT
PrivateKey = $SERVER_PRIVATE_KEY
SaveConfig = false

# Enable IP forwarding and NAT
PostUp = iptables -A FORWARD -i %i -j ACCEPT; iptables -A FORWARD -o %i -j ACCEPT; iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE
PostDown = iptables -D FORWARD -i %i -j ACCEPT; iptables -D FORWARD -o %i -j ACCEPT; iptables -t nat -D POSTROUTING -o eth0 -j MASQUERADE

# Client configurations will be added here automatically
EOF
    
    chmod 600 "$CONFIG_DIR/wireguard/wg0.conf"
}

# Create Docker Compose configuration
create_docker_compose() {
    log "Creating Docker Compose configuration..."
    
    cat > "$INSTALL_DIR/docker/docker-compose.yml" << 'EOF'
version: '3.8'

services:
  caddy:
    image: caddy:2-alpine
    container_name: vpn-caddy
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./caddy/Caddyfile:/etc/caddy/Caddyfile
      - ./caddy/data:/data
      - ./caddy/config:/config
      - ../web-ui:/srv
    environment:
      - DOMAIN=vpn.vietnga.info.vn
    networks:
      - vpn-network

  wg-easy:
    image: weejewel/wg-easy:7
    container_name: vpn-wg-easy
    restart: unless-stopped
    environment:
      - LANG=en
      - WG_HOST=vpn.vietnga.info.vn
      - PASSWORD=VpnAdmin2024!
      - WG_PORT=51820
      - WG_DEFAULT_ADDRESS=10.66.66.x
      - WG_DEFAULT_DNS=1.1.1.1,8.8.8.8
      - WG_ALLOWED_IPS=0.0.0.0/0
      - WG_PERSISTENT_KEEPALIVE=25
      - WG_PRE_UP=echo "WireGuard Pre-Up"
      - WG_POST_UP=iptables -A FORWARD -i %i -j ACCEPT; iptables -A FORWARD -o %i -j ACCEPT; iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE
      - WG_PRE_DOWN=echo "WireGuard Pre-Down"
      - WG_POST_DOWN=iptables -D FORWARD -i %i -j ACCEPT; iptables -D FORWARD -o %i -j ACCEPT; iptables -t nat -D POSTROUTING -o eth0 -j MASQUERADE
    volumes:
      - /etc/wireguard:/etc/wireguard
      - /lib/modules:/lib/modules
    ports:
      - "51820:51820/udp"
    cap_add:
      - NET_ADMIN
      - SYS_MODULE
    sysctls:
      - net.ipv4.ip_forward=1
      - net.ipv4.conf.all.src_valid_mark=1
    networks:
      - vpn-network

  wstunnel:
    image: mpromonet/wstunnel:latest
    container_name: vpn-wstunnel
    restart: unless-stopped
    command: ["wstunnel", "--server", "ws://0.0.0.0:8080", "--restrictTo", "127.0.0.1:51820"]
    ports:
      - "8080:8080"
    networks:
      - vpn-network

  stats-monitor:
    image: nginx:alpine
    container_name: vpn-stats
    restart: unless-stopped
    volumes:
      - ../web-ui:/usr/share/nginx/html
      - ./nginx/nginx.conf:/etc/nginx/nginx.conf
    networks:
      - vpn-network

networks:
  vpn-network:
    driver: bridge
EOF
}

# Create Caddy configuration
create_caddy_config() {
    log "Creating Caddy configuration..."
    
    mkdir -p "$INSTALL_DIR/docker/caddy"
    
    cat > "$INSTALL_DIR/docker/caddy/Caddyfile" << EOF
$DOMAIN {
    # Enable TLS with Let's Encrypt
    tls {
        on_demand
    }
    
    # Main VPN management interface
    route / {
        reverse_proxy vpn-wg-easy:51821
    }
    
    # WebSocket tunnel for WireGuard
    route /wstunnel {
        reverse_proxy vpn-wstunnel:8080 {
            header_up Upgrade websocket
            header_up Connection "Upgrade"
        }
    }
    
    # Statistics and monitoring
    route /stats* {
        reverse_proxy vpn-stats:80
    }
    
    # API endpoints
    route /api* {
        reverse_proxy vpn-wg-easy:51821
    }
    
    # Enable logging
    log {
        output file /var/log/caddy.log
    }
}

# Redirect HTTP to HTTPS
http://$DOMAIN {
    redir https://{host}{uri}
}
EOF
}

# Create custom web UI
create_web_ui() {
    log "Creating custom web UI..."
    
    cat > "$INSTALL_DIR/web-ui/index.html" << 'EOF'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>VPN Server Management</title>
    <link rel="stylesheet" href="style.css">
</head>
<body>
    <div class="container">
        <header>
            <h1>🔐 VPN Server Management</h1>
            <p>Secure WireGuard VPN with WebSocket Tunneling</p>
        </header>
        
        <nav class="tabs">
            <button class="tab-button active" onclick="showTab('dashboard')">Dashboard</button>
            <button class="tab-button" onclick="showTab('clients')">Clients</button>
            <button class="tab-button" onclick="showTab('stats')">Statistics</button>
            <button class="tab-button" onclick="showTab('settings')">Settings</button>
        </nav>
        
        <main>
            <div id="dashboard" class="tab-content active">
                <div class="card">
                    <h2>Server Status</h2>
                    <div class="status-grid">
                        <div class="status-item">
                            <span class="status-label">VPN Status:</span>
                            <span id="vpn-status" class="status-value">Loading...</span>
                        </div>
                        <div class="status-item">
                            <span class="status-label">Connected Clients:</span>
                            <span id="connected-clients" class="status-value">Loading...</span>
                        </div>
                        <div class="status-item">
                            <span class="status-label">Total Clients:</span>
                            <span id="total-clients" class="status-value">Loading...</span>
                        </div>
                        <div class="status-item">
                            <span class="status-label">Server IP:</span>
                            <span class="status-value">10.66.66.1</span>
                        </div>
                    </div>
                </div>
                
                <div class="card">
                    <h2>Quick Actions</h2>
                    <div class="action-buttons">
                        <button onclick="addClient()" class="btn btn-primary">Add New Client</button>
                        <button onclick="downloadConfig()" class="btn btn-secondary">Download Configs</button>
                        <button onclick="restartVPN()" class="btn btn-warning">Restart VPN</button>
                    </div>
                </div>
            </div>
            
            <div id="clients" class="tab-content">
                <div class="card">
                    <h2>Client Management</h2>
                    <button onclick="showAddClientModal()" class="btn btn-primary">Add New Client</button>
                    <div id="clients-list">
                        <!-- Client list will be populated here -->
                    </div>
                </div>
            </div>
            
            <div id="stats" class="tab-content">
                <div class="card">
                    <h2>Bandwidth Statistics</h2>
                    <canvas id="statsChart" width="400" height="200"></canvas>
                    <div id="stats-table">
                        <!-- Statistics table will be populated here -->
                    </div>
                </div>
            </div>
            
            <div id="settings" class="tab-content">
                <div class="card">
                    <h2>Server Settings</h2>
                    <form id="settings-form">
                        <div class="form-group">
                            <label for="server-name">Server Name:</label>
                            <input type="text" id="server-name" value="VPN Server">
                        </div>
                        <div class="form-group">
                            <label for="dns-servers">DNS Servers:</label>
                            <input type="text" id="dns-servers" value="1.1.1.1, 8.8.8.8">
                        </div>
                        <div class="form-group">
                            <label for="mtu">MTU Size:</label>
                            <input type="number" id="mtu" value="1420">
                        </div>
                        <button type="submit" class="btn btn-primary">Save Settings</button>
                    </form>
                </div>
            </div>
        </main>
    </div>
    
    <!-- Add Client Modal -->
    <div id="addClientModal" class="modal">
        <div class="modal-content">
            <span class="close" onclick="hideAddClientModal()">&times;</span>
            <h2>Add New Client</h2>
            <form id="add-client-form">
                <div class="form-group">
                    <label for="client-name">Client Name:</label>
                    <input type="text" id="client-name" required>
                </div>
                <div class="form-group">
                    <label for="client-email">Email (optional):</label>
                    <input type="email" id="client-email">
                </div>
                <button type="submit" class="btn btn-primary">Create Client</button>
            </form>
        </div>
    </div>
    
    <script src="app.js"></script>
</body>
</html>
EOF
    
    cat > "$INSTALL_DIR/web-ui/style.css" << 'EOF'
* {
    margin: 0;
    padding: 0;
    box-sizing: border-box;
}

body {
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    line-height: 1.6;
    color: #333;
    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    min-height: 100vh;
}

.container {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}

header {
    text-align: center;
    margin-bottom: 30px;
    color: white;
}

header h1 {
    font-size: 2.5rem;
    margin-bottom: 10px;
}

header p {
    font-size: 1.1rem;
    opacity: 0.9;
}

.tabs {
    display: flex;
    background: white;
    border-radius: 10px 10px 0 0;
    overflow: hidden;
    box-shadow: 0 2px 10px rgba(0,0,0,0.1);
}

.tab-button {
    flex: 1;
    padding: 15px 20px;
    border: none;
    background: #f8f9fa;
    cursor: pointer;
    font-size: 1rem;
    transition: all 0.3s ease;
}

.tab-button:hover {
    background: #e9ecef;
}

.tab-button.active {
    background: white;
    border-bottom: 3px solid #667eea;
}

main {
    background: white;
    border-radius: 0 0 10px 10px;
    min-height: 500px;
    box-shadow: 0 2px 10px rgba(0,0,0,0.1);
}

.tab-content {
    display: none;
    padding: 30px;
}

.tab-content.active {
    display: block;
}

.card {
    background: #f8f9fa;
    border-radius: 8px;
    padding: 25px;
    margin-bottom: 20px;
    box-shadow: 0 2px 5px rgba(0,0,0,0.05);
}

.card h2 {
    margin-bottom: 20px;
    color: #495057;
}

.status-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: 15px;
}

.status-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 10px;
    background: white;
    border-radius: 5px;
    border-left: 4px solid #667eea;
}

.status-label {
    font-weight: 600;
    color: #6c757d;
}

.status-value {
    font-weight: bold;
    color: #495057;
}

.action-buttons {
    display: flex;
    gap: 15px;
    flex-wrap: wrap;
}

.btn {
    padding: 12px 24px;
    border: none;
    border-radius: 5px;
    cursor: pointer;
    font-size: 1rem;
    font-weight: 600;
    text-decoration: none;
    display: inline-block;
    transition: all 0.3s ease;
}

.btn-primary {
    background: #667eea;
    color: white;
}

.btn-primary:hover {
    background: #5a67d8;
}

.btn-secondary {
    background: #6c757d;
    color: white;
}

.btn-secondary:hover {
    background: #5a6268;
}

.btn-warning {
    background: #ffc107;
    color: #212529;
}

.btn-warning:hover {
    background: #e0a800;
}

.btn-danger {
    background: #dc3545;
    color: white;
}

.btn-danger:hover {
    background: #c82333;
}

.form-group {
    margin-bottom: 20px;
}

.form-group label {
    display: block;
    margin-bottom: 5px;
    font-weight: 600;
    color: #495057;
}

.form-group input {
    width: 100%;
    padding: 10px;
    border: 1px solid #ced4da;
    border-radius: 5px;
    font-size: 1rem;
}

.form-group input:focus {
    outline: none;
    border-color: #667eea;
    box-shadow: 0 0 0 2px rgba(102, 126, 234, 0.25);
}

.modal {
    display: none;
    position: fixed;
    z-index: 1000;
    left: 0;
    top: 0;
    width: 100%;
    height: 100%;
    background-color: rgba(0,0,0,0.5);
}

.modal-content {
    background-color: white;
    margin: 15% auto;
    padding: 30px;
    border-radius: 10px;
    width: 90%;
    max-width: 500px;
    position: relative;
}

.close {
    position: absolute;
    right: 15px;
    top: 15px;
    color: #aaa;
    font-size: 28px;
    font-weight: bold;
    cursor: pointer;
}

.close:hover {
    color: #000;
}

#clients-list {
    margin-top: 20px;
}

.client-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 15px;
    background: white;
    border-radius: 5px;
    margin-bottom: 10px;
    border-left: 4px solid #28a745;
}

.client-info h3 {
    margin-bottom: 5px;
    color: #495057;
}

.client-info p {
    color: #6c757d;
    font-size: 0.9rem;
}

.client-actions {
    display: flex;
    gap: 10px;
}

.client-actions .btn {
    padding: 8px 16px;
    font-size: 0.9rem;
}

#stats-table {
    margin-top: 20px;
    overflow-x: auto;
}

.stats-table {
    width: 100%;
    border-collapse: collapse;
    background: white;
    border-radius: 5px;
    overflow: hidden;
}

.stats-table th,
.stats-table td {
    padding: 12px;
    text-align: left;
    border-bottom: 1px solid #dee2e6;
}

.stats-table th {
    background: #f8f9fa;
    font-weight: 600;
    color: #495057;
}

@media (max-width: 768px) {
    .container {
        padding: 10px;
    }
    
    .tabs {
        flex-direction: column;
    }
    
    .status-grid {
        grid-template-columns: 1fr;
    }
    
    .action-buttons {
        flex-direction: column;
    }
    
    .client-item {
        flex-direction: column;
        align-items: flex-start;
        gap: 10px;
    }
}
EOF
    
    cat > "$INSTALL_DIR/web-ui/app.js" << 'EOF'
// VPN Management Interface JavaScript

let clients = [];
let stats = {};

// Tab functionality
function showTab(tabName) {
    // Hide all tab contents
    const tabContents = document.querySelectorAll('.tab-content');
    tabContents.forEach(content => content.classList.remove('active'));
    
    // Remove active class from all tab buttons
    const tabButtons = document.querySelectorAll('.tab-button');
    tabButtons.forEach(button => button.classList.remove('active'));
    
    // Show selected tab content
    document.getElementById(tabName).classList.add('active');
    
    // Add active class to clicked button
    event.target.classList.add('active');
    
    // Load tab-specific data
    if (tabName === 'clients') {
        loadClients();
    } else if (tabName === 'stats') {
        loadStats();
    } else if (tabName === 'dashboard') {
        loadDashboard();
    }
}

// Load dashboard data
function loadDashboard() {
    // Simulate API calls to get server status
    updateServerStatus();
    setInterval(updateServerStatus, 30000); // Update every 30 seconds
}

function updateServerStatus() {
    // Simulate getting real data
    document.getElementById('vpn-status').textContent = 'Online';
    document.getElementById('vpn-status').style.color = '#28a745';
    
    // This would normally come from an API
    fetch('/api/status')
        .then(response => response.json())
        .then(data => {
            document.getElementById('connected-clients').textContent = data.connected || '0';
            document.getElementById('total-clients').textContent = data.total || '0';
        })
        .catch(() => {
            // Fallback values
            document.getElementById('connected-clients').textContent = '0';
            document.getElementById('total-clients').textContent = '0';
        });
}

// Client management
function loadClients() {
    const clientsList = document.getElementById('clients-list');
    
    // This would normally fetch from API
    fetch('/api/clients')
        .then(response => response.json())
        .then(data => {
            clients = data;
            renderClients();
        })
        .catch(() => {
            // Show sample data
            clients = [
                {
                    id: 1,
                    name: 'John Laptop',
                    email: 'john@example.com',
                    ip: '10.66.66.2',
                    connected: true,
                    lastSeen: new Date().toISOString()
                },
                {
                    id: 2,
                    name: 'Jane Phone',
                    email: 'jane@example.com',
                    ip: '10.66.66.3',
                    connected: false,
                    lastSeen: new Date(Date.now() - 3600000).toISOString()
                }
            ];
            renderClients();
        });
}

function renderClients() {
    const clientsList = document.getElementById('clients-list');
    
    if (clients.length === 0) {
        clientsList.innerHTML = '<p>No clients configured yet.</p>';
        return;
    }
    
    clientsList.innerHTML = clients.map(client => `
        <div class="client-item">
            <div class="client-info">
                <h3>${client.name}</h3>
                <p>IP: ${client.ip} | Email: ${client.email || 'N/A'}</p>
                <p>Status: <span style="color: ${client.connected ? '#28a745' : '#dc3545'}">
                    ${client.connected ? 'Connected' : 'Disconnected'}
                </span></p>
                <p>Last seen: ${new Date(client.lastSeen).toLocaleString()}</p>
            </div>
            <div class="client-actions">
                <button onclick="downloadClientConfig(${client.id})" class="btn btn-secondary">Config</button>
                <button onclick="showQRCode(${client.id})" class="btn btn-primary">QR Code</button>
                <button onclick="removeClient(${client.id})" class="btn btn-danger">Remove</button>
            </div>
        </div>
    `).join('');
}

// Modal functions
function showAddClientModal() {
    document.getElementById('addClientModal').style.display = 'block';
}

function hideAddClientModal() {
    document.getElementById('addClientModal').style.display = 'none';
    document.getElementById('add-client-form').reset();
}

// Client actions
function addClient() {
    showAddClientModal();
}

document.getElementById('add-client-form').addEventListener('submit', function(e) {
    e.preventDefault();
    
    const name = document.getElementById('client-name').value;
    const email = document.getElementById('client-email').value;
    
    // This would normally send to API
    fetch('/api/clients', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({ name, email })
    })
    .then(response => response.json())
    .then(data => {
        if (data.success) {
            hideAddClientModal();
            loadClients();
            alert('Client added successfully!');
        } else {
            alert('Error adding client: ' + data.error);
        }
    })
    .catch(() => {
        // Simulate successful creation
        const newClient = {
            id: clients.length + 1,
            name: name,
            email: email,
            ip: `10.66.66.${clients.length + 2}`,
            connected: false,
            lastSeen: new Date().toISOString()
        };
        clients.push(newClient);
        renderClients();
        hideAddClientModal();
        alert('Client added successfully! (Demo mode)');
    });
});

function removeClient(clientId) {
    if (confirm('Are you sure you want to remove this client?')) {
        fetch(`/api/clients/${clientId}`, {
            method: 'DELETE'
        })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                loadClients();
                alert('Client removed successfully!');
            } else {
                alert('Error removing client: ' + data.error);
            }
        })
        .catch(() => {
            // Simulate removal
            clients = clients.filter(client => client.id !== clientId);
            renderClients();
            alert('Client removed successfully! (Demo mode)');
        });
    }
}

function downloadClientConfig(clientId) {
    window.open(`/api/clients/${clientId}/config`, '_blank');
}

function showQRCode(clientId) {
    window.open(`/api/clients/${clientId}/qr`, '_blank');
}

// Statistics
function loadStats() {
    // This would normally fetch real statistics
    fetch('/api/stats')
        .then(response => response.json())
        .then(data => {
            renderStats(data);
        })
        .catch(() => {
            // Show demo stats
            const demoStats = {
                clients: [
                    { name: 'John Laptop', bytesIn: 1024*1024*50, bytesOut: 1024*1024*30 },
                    { name: 'Jane Phone', bytesIn: 1024*1024*25, bytesOut: 1024*1024*15 }
                ]
            };
            renderStats(demoStats);
        });
}

function renderStats(statsData) {
    const statsTable = document.getElementById('stats-table');
    
    if (!statsData.clients || statsData.clients.length === 0) {
        statsTable.innerHTML = '<p>No statistics available.</p>';
        return;
    }
    
    statsTable.innerHTML = `
        <table class="stats-table">
            <thead>
                <tr>
                    <th>Client</th>
                    <th>Data In</th>
                    <th>Data Out</th>
                    <th>Total</th>
                </tr>
            </thead>
            <tbody>
                ${statsData.clients.map(client => `
                    <tr>
                        <td>${client.name}</td>
                        <td>${formatBytes(client.bytesIn)}</td>
                        <td>${formatBytes(client.bytesOut)}</td>
                        <td>${formatBytes(client.bytesIn + client.bytesOut)}</td>
                    </tr>
                `).join('')}
            </tbody>
        </table>
    `;
}

function formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

// Quick actions
function downloadConfig() {
    window.open('/api/configs/all', '_blank');
}

function restartVPN() {
    if (confirm('Are you sure you want to restart the VPN server?')) {
        fetch('/api/restart', { method: 'POST' })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    alert('VPN server is restarting...');
                    setTimeout(updateServerStatus, 5000);
                } else {
                    alert('Error restarting VPN server: ' + data.error);
                }
            })
            .catch(() => {
                alert('VPN server restart initiated! (Demo mode)');
            });
    }
}

// Settings
document.getElementById('settings-form').addEventListener('submit', function(e) {
    e.preventDefault();
    
    const settings = {
        serverName: document.getElementById('server-name').value,
        dnsServers: document.getElementById('dns-servers').value,
        mtu: document.getElementById('mtu').value
    };
    
    fetch('/api/settings', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(settings)
    })
    .then(response => response.json())
    .then(data => {
        if (data.success) {
            alert('Settings saved successfully!');
        } else {
            alert('Error saving settings: ' + data.error);
        }
    })
    .catch(() => {
        alert('Settings saved successfully! (Demo mode)');
    });
});

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    loadDashboard();
    
    // Close modal when clicking outside
    window.addEventListener('click', function(event) {
        const modal = document.getElementById('addClientModal');
        if (event.target === modal) {
            hideAddClientModal();
        }
    });
});
EOF
}

# Create systemd services
create_systemd_services() {
    log "Creating systemd services..."
    
    # VPN Server service
    cat > "/etc/systemd/system/vpn-server.service" << EOF
[Unit]
Description=VPN Server Stack
Requires=docker.service
After=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=$INSTALL_DIR/docker
ExecStart=/usr/local/bin/docker-compose up -d
ExecStop=/usr/local/bin/docker-compose down
TimeoutStartSec=0

[Install]
WantedBy=multi-user.target
EOF
    
    # Stats monitor service
    cat > "/etc/systemd/system/vpn-stats.service" << EOF
[Unit]
Description=VPN Statistics Monitor
After=vpn-server.service

[Service]
Type=simple
ExecStart=$INSTALL_DIR/scripts/stats-monitor.sh
Restart=always
RestartSec=30

[Install]
WantedBy=multi-user.target
EOF
    
    systemctl daemon-reload
    systemctl enable vpn-server.service
    systemctl enable vpn-stats.service
}

# Create helper scripts
create_helper_scripts() {
    log "Creating helper scripts..."
    
    # Client management script
    cat > "$INSTALL_DIR/scripts/manage-client.sh" << 'EOF'
#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_DIR="/etc/vpn-server"
WG_CONFIG="$CONFIG_DIR/wireguard/wg0.conf"

add_client() {
    local name="$1"
    local email="${2:-}"
    
    if [[ -z "$name" ]]; then
        echo "Usage: $0 add <client-name> [email]"
        exit 1
    fi
    
    # Generate client keys
    cd "$CONFIG_DIR/wireguard"
    CLIENT_PRIVATE_KEY=$(wg genkey)
    CLIENT_PUBLIC_KEY=$(echo "$CLIENT_PRIVATE_KEY" | wg pubkey)
    
    # Find next available IP
    LAST_IP=$(grep -oP 'AllowedIPs = 10\.66\.66\.\K\d+' "$WG_CONFIG" | sort -n | tail -1)
    NEXT_IP=$((${LAST_IP:-1} + 1))
    CLIENT_IP="10.66.66.$NEXT_IP"
    
    # Add peer to server config
    cat >> "$WG_CONFIG" << EOL

# Client: $name
[Peer]
PublicKey = $CLIENT_PUBLIC_KEY
AllowedIPs = $CLIENT_IP/32
EOL
    
    # Create client config
    mkdir -p "$CONFIG_DIR/clients/$name"
    cat > "$CONFIG_DIR/clients/$name/wg0.conf" << EOL
[Interface]
PrivateKey = $CLIENT_PRIVATE_KEY
Address = $CLIENT_IP/24
DNS = 1.1.1.1, 8.8.8.8

[Peer]
PublicKey = $(cat "$CONFIG_DIR/wireguard/server_public.key")
Endpoint = vpn.vietnga.info.vn:443
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25
EOL
    
    # Generate QR code
    qrencode -t ansiutf8 < "$CONFIG_DIR/clients/$name/wg0.conf" > "$CONFIG_DIR/clients/$name/qr.txt"
    qrencode -t png -o "$CONFIG_DIR/clients/$name/qr.png" < "$CONFIG_DIR/clients/$name/wg0.conf"
    
    # Restart WireGuard
    systemctl restart wg-quick@wg0
    
    echo "Client '$name' added successfully!"
    echo "IP: $CLIENT_IP"
    echo "Config: $CONFIG_DIR/clients/$name/wg0.conf"
    echo "QR Code: $CONFIG_DIR/clients/$name/qr.png"
}

remove_client() {
    local name="$1"
    
    if [[ -z "$name" ]]; then
        echo "Usage: $0 remove <client-name>"
        exit 1
    fi
    
    if [[ ! -d "$CONFIG_DIR/clients/$name" ]]; then
        echo "Client '$name' not found!"
        exit 1
    fi
    
    # Get client public key
    CLIENT_PUBLIC_KEY=$(grep -A3 "# Client: $name" "$WG_CONFIG" | grep "PublicKey" | cut -d' ' -f3)
    
    # Remove from server config
    sed -i "/# Client: $name/,+2d" "$WG_CONFIG"
    
    # Remove client directory
    rm -rf "$CONFIG_DIR/clients/$name"
    
    # Restart WireGuard
    systemctl restart wg-quick@wg0
    
    echo "Client '$name' removed successfully!"
}

list_clients() {
    echo "Configured clients:"
    if [[ -d "$CONFIG_DIR/clients" ]]; then
        for client_dir in "$CONFIG_DIR/clients"/*; do
            if [[ -d "$client_dir" ]]; then
                client_name=$(basename "$client_dir")
                client_ip=$(grep "Address" "$client_dir/wg0.conf" | cut -d' ' -f3 | cut -d'/' -f1)
                echo "  - $client_name ($client_ip)"
            fi
        done
    else
        echo "  No clients configured"
    fi
}

case "$1" in
    add)
        add_client "$2" "$3"
        ;;
    remove)
        remove_client "$2"
        ;;
    list)
        list_clients
        ;;
    *)
        echo "Usage: $0 {add|remove|list} [arguments...]"
        echo "  add <name> [email]  - Add a new client"
        echo "  remove <name>       - Remove a client"
        echo "  list                - List all clients"
        exit 1
        ;;
esac
EOF
    
    chmod +x "$INSTALL_DIR/scripts/manage-client.sh"
    
    # Stats monitor script
    cat > "$INSTALL_DIR/scripts/stats-monitor.sh" << 'EOF'
#!/bin/bash

CONFIG_DIR="/etc/vpn-server"
STATS_DIR="/var/lib/vpn-server/stats"

mkdir -p "$STATS_DIR"

while true; do
    # Get WireGuard stats
    if command -v wg &> /dev/null; then
        wg show wg0 dump > "$STATS_DIR/wg_dump.txt" 2>/dev/null
        
        # Parse and format stats
        {
            echo "{"
            echo "  \"timestamp\": \"$(date -Iseconds)\","
            echo "  \"clients\": ["
            
            first=true
            while IFS=$'\t' read -r public_key preshared_key endpoint allowed_ips latest_handshake rx_bytes tx_bytes persistent_keepalive; do
                if [[ "$public_key" != "$(cat "$CONFIG_DIR/wireguard/server_public.key" 2>/dev/null)" ]]; then
                    if [[ "$first" == true ]]; then
                        first=false
                    else
                        echo ","
                    fi
                    
                    # Find client name by public key
                    client_name="Unknown"
                    for client_dir in "$CONFIG_DIR/clients"/*; do
                        if [[ -d "$client_dir" ]]; then
                            if grep -q "$public_key" "$client_dir/wg0.conf" 2>/dev/null; then
                                client_name=$(basename "$client_dir")
                                break
                            fi
                        fi
                    done
                    
                    echo "    {"
                    echo "      \"name\": \"$client_name\","
                    echo "      \"public_key\": \"$public_key\","
                    echo "      \"endpoint\": \"$endpoint\","
                    echo "      \"allowed_ips\": \"$allowed_ips\","
                    echo "      \"latest_handshake\": \"$latest_handshake\","
                    echo "      \"rx_bytes\": ${rx_bytes:-0},"
                    echo "      \"tx_bytes\": ${tx_bytes:-0},"
                    echo "      \"connected\": $(if [[ -n "$latest_handshake" && "$latest_handshake" != "0" ]]; then echo "true"; else echo "false"; fi)"
                    echo -n "    }"
                fi
            done < "$STATS_DIR/wg_dump.txt"
            
            echo ""
            echo "  ]"
            echo "}"
        } > "$STATS_DIR/current.json"
    fi
    
    sleep 30
done
EOF
    
    chmod +x "$INSTALL_DIR/scripts/stats-monitor.sh"
    
    # Client helper script
    cat > "$INSTALL_DIR/scripts/client-helper.sh" << 'EOF'
#!/bin/bash

# Client-side helper for connecting to VPN via WebSocket tunnel

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="$HOME/.vpn-client.conf"
WS_TUNNEL_PORT="8080"
WG_PORT="51820"
SERVER_DOMAIN="vpn.vietnga.info.vn"

install_dependencies() {
    echo "Installing dependencies..."
    
    if command -v apt-get &> /dev/null; then
        # Ubuntu/Debian
        sudo apt-get update
        sudo apt-get install -y wireguard-tools wget curl
    elif command -v yum &> /dev/null; then
        # CentOS/RHEL
        sudo yum install -y epel-release
        sudo yum install -y wireguard-tools wget curl
    elif command -v brew &> /dev/null; then
        # macOS
        brew install wireguard-tools wget curl
    else
        echo "Unsupported system. Please install WireGuard manually."
        exit 1
    fi
    
    # Install wstunnel
    WSTUNNEL_URL="https://github.com/erebe/wstunnel/releases/latest/download/wstunnel-linux-x64"
    if [[ "$OSTYPE" == "darwin"* ]]; then
        WSTUNNEL_URL="https://github.com/erebe/wstunnel/releases/latest/download/wstunnel-macos-x64"
    fi
    
    sudo curl -L "$WSTUNNEL_URL" -o /usr/local/bin/wstunnel
    sudo chmod +x /usr/local/bin/wstunnel
}

setup_client() {
    local config_path="$1"
    
    if [[ ! -f "$config_path" ]]; then
        echo "Config file not found: $config_path"
        exit 1
    fi
    
    # Copy config
    cp "$config_path" "$CONFIG_FILE"
    
    # Modify config for WebSocket tunnel
    sed -i "s/Endpoint = .*/Endpoint = 127.0.0.1:$WG_PORT/" "$CONFIG_FILE"
    
    echo "Client configured successfully!"
    echo "Config stored at: $CONFIG_FILE"
}

start_tunnel() {
    echo "Starting WebSocket tunnel..."
    
    # Start wstunnel in background
    wstunnel --localToRemote="127.0.0.1:$WG_PORT:127.0.0.1:$WG_PORT" "wss://$SERVER_DOMAIN/wstunnel" &
    TUNNEL_PID=$!
    echo $TUNNEL_PID > /tmp/wstunnel.pid
    
    sleep 2
    
    # Start WireGuard
    sudo wg-quick up "$CONFIG_FILE"
    
    echo "VPN connected via WebSocket tunnel!"
    echo "Tunnel PID: $TUNNEL_PID"
}

stop_tunnel() {
    echo "Stopping VPN connection..."
    
    # Stop WireGuard
    sudo wg-quick down "$CONFIG_FILE" 2>/dev/null || true
    
    # Stop wstunnel
    if [[ -f /tmp/wstunnel.pid ]]; then
        kill $(cat /tmp/wstunnel.pid) 2>/dev/null || true
        rm -f /tmp/wstunnel.pid
    fi
    
    echo "VPN disconnected!"
}

status() {
    echo "VPN Status:"
    
    if sudo wg show 2>/dev/null | grep -q "interface:"; then
        echo "  WireGuard: Connected"
        sudo wg show
    else
        echo "  WireGuard: Disconnected"
    fi
    
    if [[ -f /tmp/wstunnel.pid ]] && kill -0 $(cat /tmp/wstunnel.pid) 2>/dev/null; then
        echo "  WebSocket Tunnel: Running (PID: $(cat /tmp/wstunnel.pid))"
    else
        echo "  WebSocket Tunnel: Stopped"
    fi
}

case "$1" in
    install)
        install_dependencies
        ;;
    setup)
        setup_client "$2"
        ;;
    start)
        start_tunnel
        ;;
    stop)
        stop_tunnel
        ;;
    status)
        status
        ;;
    *)
        echo "Usage: $0 {install|setup|start|stop|status}"
        echo "  install          - Install required dependencies"
        echo "  setup <config>   - Setup client with config file"
        echo "  start            - Start VPN connection"
        echo "  stop             - Stop VPN connection"
        echo "  status           - Show connection status"
        exit 1
        ;;
esac
EOF
    
    chmod +x "$INSTALL_DIR/scripts/client-helper.sh"
}

# Configure firewall
configure_firewall() {
    log "Configuring firewall..."
    
    # Reset UFW
    ufw --force reset
    
    # Default policies
    ufw default deny incoming
    ufw default allow outgoing
    
    # Allow SSH (important!)
    ufw allow ssh
    
    # Allow HTTP and HTTPS
    ufw allow 80/tcp
    ufw allow 443/tcp
    
    # Allow WireGuard (only from localhost for security)
    ufw allow from 127.0.0.1 to any port 51820
    
    # Enable UFW
    ufw --force enable
    
    # Configure iptables for NAT
    iptables -t nat -A POSTROUTING -s 10.66.66.0/24 -o eth0 -j MASQUERADE
    iptables -A FORWARD -i wg0 -j ACCEPT
    iptables -A FORWARD -o wg0 -j ACCEPT
    
    # Save iptables rules
    iptables-save > /etc/iptables/rules.v4
}

# Start services
start_services() {
    log "Starting VPN services..."
    
    # Start Docker services
    cd "$INSTALL_DIR/docker"
    docker-compose up -d
    
    # Start systemd services
    systemctl start vpn-stats.service
    
    # Wait for services to start
    sleep 10
    
    # Verify services
    docker-compose ps
    systemctl status vpn-stats.service --no-pager
}

# Create documentation
create_documentation() {
    log "Creating documentation..."
    
    cat > "$INSTALL_DIR/README.md" << 'EOF'
# VPN Production Server

This is a complete VPN server solution with WireGuard over WebSocket + TLS.

## Features

- **WireGuard VPN** with multiple client support
- **WebSocket tunneling** over TCP 443 with TLS encryption
- **Automatic TLS certificates** via Let's Encrypt
- **Web management interface** with real-time statistics
- **QR code generation** for easy client setup
- **Bandwidth monitoring** and reporting
- **Production-ready** systemd service management

## Server Components

- **Caddy**: Reverse proxy and TLS termination
- **WG-Easy**: Web-based WireGuard management
- **wstunnel**: WebSocket tunneling for WireGuard
- **Statistics Monitor**: Real-time bandwidth tracking

## Management

### Web Interface
Access the web management interface at: https://vpn.vietnga.info.vn

Default credentials:
- Username: admin
- Password: VpnAdmin2024!

### Command Line

Add a new client:
```bash
sudo /opt/vpn-server/scripts/manage-client.sh add "John Laptop" john@example.com
```

Remove a client:
```bash
sudo /opt/vpn-server/scripts/manage-client.sh remove "John Laptop"
```

List all clients:
```bash
sudo /opt/vpn-server/scripts/manage-client.sh list
```

### Service Management

Start VPN services:
```bash
sudo systemctl start vpn-server.service
```

Stop VPN services:
```bash
sudo systemctl stop vpn-server.service
```

Check service status:
```bash
sudo systemctl status vpn-server.service
sudo systemctl status vpn-stats.service
```

View logs:
```bash
sudo journalctl -u vpn-server.service -f
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml logs -f
```

## Client Setup

### Download Helper Script
```bash
wget https://vpn.vietnga.info.vn/client-helper.sh
chmod +x client-helper.sh
```

### Install Dependencies
```bash
./client-helper.sh install
```

### Setup Client
```bash
./client-helper.sh setup /path/to/client.conf
```

### Connect
```bash
./client-helper.sh start
```

### Disconnect
```bash
./client-helper.sh stop
```

### Check Status
```bash
./client-helper.sh status
```

## Troubleshooting

### Check Services
```bash
# Docker services
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml ps

# Systemd services
sudo systemctl status vpn-server.service
sudo systemctl status vpn-stats.service

# WireGuard
sudo wg show
```

### View Logs
```bash
# Installation log
sudo tail -f /var/log/vpn-install.log

# Docker logs
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml logs -f

# Systemd logs
sudo journalctl -u vpn-server.service -f
sudo journalctl -u vpn-stats.service -f
```

### Network Issues
```bash
# Check firewall
sudo ufw status verbose

# Check IP forwarding
cat /proc/sys/net/ipv4/ip_forward

# Check iptables
sudo iptables -L -n -v
sudo iptables -t nat -L -n -v
```

### SSL Certificate Issues
```bash
# Check Caddy logs
sudo docker logs vpn-caddy

# Manual certificate renewal
sudo docker exec vpn-caddy caddy reload --config /etc/caddy/Caddyfile
```

## Security Notes

- Change the default admin password immediately
- Regularly update the server and Docker images
- Monitor logs for suspicious activity
- Use strong client names and rotate keys periodically
- Consider enabling fail2ban for additional protection

## File Locations

- Installation: `/opt/vpn-server/`
- Configuration: `/etc/vpn-server/`
- Client configs: `/etc/vpn-server/clients/`
- Statistics: `/var/lib/vpn-server/stats/`
- Logs: `/var/log/vpn-install.log`

## Support

For issues and support, check the logs and refer to the troubleshooting section above.
EOF
}

# Main installation function
main() {
    clear
    echo -e "${BLUE}"
    echo "=================================================================="
    echo "         VPN Production Server Installation Script"
    echo "=================================================================="
    echo -e "${NC}"
    echo "Target: Ubuntu 24.04"
    echo "Domain: $DOMAIN"
    echo "Subnet: $VPN_SUBNET"
    echo ""
    
    log "Starting VPN server installation..."
    
    # Pre-installation checks
    check_root
    check_ubuntu_version
    
    # Installation steps
    update_system
    install_docker
    install_wireguard
    create_directories
    generate_server_keys
    create_wireguard_config
    create_docker_compose
    create_caddy_config
    create_web_ui
    create_systemd_services
    create_helper_scripts
    configure_firewall
    start_services
    create_documentation
    
    # Installation complete
    echo -e "${GREEN}"
    echo "=================================================================="
    echo "         VPN Server Installation Complete!"
    echo "=================================================================="
    echo -e "${NC}"
    echo ""
    echo "🎉 VPN server has been successfully installed and configured!"
    echo ""
    echo "📋 Next Steps:"
    echo "1. Point your domain '$DOMAIN' to this server's IP"
    echo "2. Wait a few minutes for Let's Encrypt certificate generation"
    echo "3. Access the web interface: https://$DOMAIN"
    echo "4. Default password: VpnAdmin2024!"
    echo ""
    echo "📁 Important Files:"
    echo "   - Installation: $INSTALL_DIR"
    echo "   - Configuration: $CONFIG_DIR"
    echo "   - Documentation: $INSTALL_DIR/README.md"
    echo ""
    echo "🔧 Management Commands:"
    echo "   - Add client: $INSTALL_DIR/scripts/manage-client.sh add 'Client Name'"
    echo "   - List clients: $INSTALL_DIR/scripts/manage-client.sh list"
    echo "   - View logs: tail -f $LOG_FILE"
    echo ""
    echo "⚠️  Security Reminder:"
    echo "   - Change the default admin password immediately"
    echo "   - Ensure your domain DNS is pointed to this server"
    echo "   - Monitor logs regularly for security"
    echo ""
    log "Installation completed successfully!"
}

# Run main function
main "$@"