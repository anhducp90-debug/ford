# Ford - Production VPN Server with WireGuard over WebSocket + TLS

A comprehensive VPN server solution featuring WireGuard tunneled over WebSocket with automatic TLS certificates, web management interface, and production-ready deployment automation.

## 🚀 Quick Start

Deploy a complete VPN server on Ubuntu 24.04 with a single command:

```bash
# Download and run the installation script
wget https://raw.githubusercontent.com/anhducp90-debug/ford/main/install-vpn.sh
sudo bash install-vpn.sh
```

**Prerequisites:**
- Ubuntu 24.04 server with root access
- Domain pointing to your server (vpn.vietnga.info.vn)
- Ports 80, 443, and 51820 available

## 🎯 Features

### Core VPN Capabilities
- **WireGuard VPN** with multiple client support
- **WebSocket tunneling** over TCP 443 for bypassing network restrictions
- **Automatic TLS certificates** via Let's Encrypt
- **Subnet management** (10.66.66.0/24) with automatic IP assignment

### Management & Monitoring
- **Web management interface** with real-time client monitoring
- **CLI tools** for client management and server administration
- **QR code generation** for easy mobile device setup
- **Bandwidth monitoring** and usage statistics
- **Health monitoring** and automated service management

### Production Ready
- **Systemd service management** with auto-restart
- **Docker-based architecture** for reliability and isolation
- **Comprehensive firewall configuration** with fail2ban
- **Automated backup and logging** systems
- **Security hardening** with proper access controls

## 📋 Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   VPN Clients   │    │   Server Stack   │    │   Management    │
├─────────────────┤    ├──────────────────┤    ├─────────────────┤
│ • WireGuard App │───▶│ • Caddy (Proxy) │    │ • Web Interface │
│ • Mobile Apps   │    │ • WG-Easy        │    │ • CLI Tools     │
│ • Client Helper │    │ • wstunnel       │    │ • Stats Monitor │
│ • QR Codes      │    │ • WireGuard      │    │ • Health Check  │
└─────────────────┘    └──────────────────┘    └─────────────────┘
```

## 🛠 Installation

### Automatic Installation

The installation script handles everything automatically:

```bash
# Make sure your domain points to the server first
sudo bash install-vpn.sh
```

### Manual Installation Steps

1. **Clone the repository:**
   ```bash
   git clone https://github.com/anhducp90-debug/ford.git
   cd ford
   sudo bash install-vpn.sh
   ```

2. **Configure DNS:**
   Point `vpn.vietnga.info.vn` to your server's IP address

3. **Access the web interface:**
   Visit `https://vpn.vietnga.info.vn` (default password: `VpnAdmin2024!`)

## 👥 Client Management

### Web Interface
Access the web management at: `https://vpn.vietnga.info.vn`
- Add/remove clients with QR codes
- Monitor real-time connections and bandwidth
- Download client configurations
- View server statistics

### Command Line Interface

```bash
# Add a new client
sudo /opt/vpn-server/scripts/manage-client.sh add "John Laptop" john@example.com

# Remove a client
sudo /opt/vpn-server/scripts/manage-client.sh remove "John Laptop"

# List all clients
sudo /opt/vpn-server/scripts/manage-client.sh list

# Show client details
sudo /opt/vpn-server/scripts/manage-client.sh show "John Laptop"

# Create client package
sudo /opt/vpn-server/scripts/manage-client.sh package "John Laptop"
```

## 📱 Client Setup

### Mobile Devices
1. Scan the QR code from the web interface
2. Import into WireGuard app
3. Connect and enjoy secure browsing

### Desktop/Laptop

#### Using the Client Helper Script
```bash
# Download the client helper
wget https://vpn.vietnga.info.vn/downloads/client-helper.sh
chmod +x client-helper.sh

# Install dependencies
./client-helper.sh install

# Setup client (standard connection)
./client-helper.sh setup client-config.conf

# Setup client (WebSocket for restricted networks)
./client-helper.sh setup client-config.conf true

# Connect
./client-helper.sh start

# For restricted networks, use WebSocket tunnel
./client-helper.sh start websocket

# Check status
./client-helper.sh status

# Disconnect
./client-helper.sh stop
```

## 🔧 Service Management

### System Services
```bash
# VPN server stack (Docker containers)
sudo systemctl start vpn-server.service
sudo systemctl stop vpn-server.service
sudo systemctl status vpn-server.service

# Statistics monitoring
sudo systemctl start vpn-stats.service
sudo systemctl status vpn-stats.service

# WireGuard interface
sudo systemctl start wg-quick@wg0.service
sudo systemctl status wg-quick@wg0.service
```

### Docker Services
```bash
# View running containers
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml ps

# View logs
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml logs -f

# Restart specific service
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml restart caddy
```

## 📊 Monitoring & Statistics

### Web Dashboard
- Real-time client connections
- Bandwidth usage per client
- Server health status
- Historical statistics

### Command Line Monitoring
```bash
# Show current statistics
sudo /opt/vpn-server/scripts/stats-monitor.sh show

# Check health status
sudo /opt/vpn-server/scripts/stats-monitor.sh health

# Generate one-time statistics
sudo /opt/vpn-server/scripts/stats-monitor.sh once
```

### Log Files
```bash
# Installation log
sudo tail -f /var/log/vpn-install.log

# Statistics log
sudo tail -f /var/log/vpn-stats.log

# System logs
sudo journalctl -u vpn-server.service -f
sudo journalctl -u vpn-stats.service -f
```

## 🔒 Security Features

### Firewall Configuration
```bash
# Check firewall status
sudo /opt/vpn-server/scripts/firewall-manager.sh status

# Block malicious IP
sudo /opt/vpn-server/scripts/firewall-manager.sh block-ip 1.2.3.4

# Reload firewall rules
sudo /opt/vpn-server/scripts/firewall-manager.sh reload
```

### Security Best Practices
- **Change default passwords** immediately after installation
- **Regular updates** of server and Docker images
- **Monitor logs** for suspicious activity
- **Use strong client names** and rotate keys periodically
- **Enable fail2ban** for brute force protection
- **Restrict SSH access** to specific IPs when possible

## 🗂 File Structure

```
/opt/vpn-server/           # Main installation directory
├── docker/               # Docker configurations
│   ├── docker-compose.yml
│   ├── caddy/            # Caddy reverse proxy config
│   └── nginx/            # Nginx configuration
├── scripts/              # Management scripts
│   ├── manage-client.sh  # Client management
│   ├── client-helper.sh  # Client setup helper
│   ├── stats-monitor.sh  # Statistics monitoring
│   └── firewall-manager.sh # Firewall management
├── web-ui/               # Web management interface
└── README.md            # Documentation

/etc/vpn-server/          # Configuration directory
├── wireguard/           # WireGuard configurations
├── clients/             # Client configurations
└── certs/              # TLS certificates

/var/lib/vpn-server/     # Data directory
├── stats/              # Statistics files
└── data/               # Application data
```

## 🛠 Troubleshooting

### Common Issues

**Certificate issues:**
```bash
# Check Caddy logs
sudo docker logs vpn-caddy

# Force certificate renewal
sudo docker exec vpn-caddy caddy reload --config /etc/caddy/Caddyfile
```

**Connection problems:**
```bash
# Check WireGuard status
sudo wg show

# Check iptables rules
sudo iptables -L -n -v
sudo iptables -t nat -L -n -v

# Test connectivity
ping 10.66.66.1  # VPN server IP
```

**Service issues:**
```bash
# Restart all services
sudo systemctl restart vpn-server.service
sudo systemctl restart vpn-stats.service

# Check service status
sudo systemctl status vpn-server.service --no-pager -l
```

### Getting Help

1. Check the logs in `/var/log/vpn-install.log`
2. Review service status with `systemctl status`
3. Verify network connectivity and DNS resolution
4. Check firewall rules and port accessibility

## 🤝 Contributing

This project is part of the Ford repository. Contributions are welcome!

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## 📄 License

This project is licensed under the ISC License.

## ⚡ Legacy Ford Application

The original simple greeting application is still available:

```bash
# Run the original Ford greeting app
npm start
# or
node index.js Alice

# Run tests
npm test
```

### Original API

```javascript
const { sayHi } = require('./index.js');

console.log(sayHi());        // "Hi, World!"
console.log(sayHi('Alice')); // "Hi, Alice!"
```