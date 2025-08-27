# WireGuard + UDP2RAW VPN Management System

A comprehensive VPN management solution for Frankfurt VPS that combines WireGuard with UDP2RAW to bypass NAT/CGNAT/ISP blocking, specifically designed for clients in Russia.

## Features

- **Automated Setup**: Complete server setup with one command
- **Client Management**: Add, remove, list clients with ease
- **QR Code Generation**: Automatic QR code generation for mobile devices
- **Security**: Strong key generation, PreSharedKey support, secure file permissions
- **Logging**: Comprehensive logging of all operations
- **Multi-Platform**: Support for Android, iOS, Windows, Linux clients
- **Scalable**: Support for up to 50 clients
- **Bypass Restrictions**: UDP2RAW integration for bypassing NAT/CGNAT/ISP blocking

## System Requirements

- Ubuntu 20.04 or later
- Root or sudo privileges
- Internet connection
- Public IP address

## Quick Start

### 1. Server Setup

```bash
# Make the script executable
chmod +x vpn_manager.sh

# Run initial setup (requires root privileges)
sudo ./vpn_manager.sh setup
```

This will:
- Install required dependencies (WireGuard, qrencode)
- Generate server keys
- Configure WireGuard interface
- Set up firewall rules
- Enable IP forwarding
- Start WireGuard service

### 2. Add Your First Client

```bash
# Add a client for an Android phone
sudo ./vpn_manager.sh add-client john-android

# Add a client for an iPhone
sudo ./vpn_manager.sh add-client mary-iphone

# Add a client for a Windows laptop
sudo ./vpn_manager.sh add-client laptop-home
```

### 3. Get Client Configuration

After adding a client, you'll find the configuration files in the `clients/` directory:

- `clients/john-android.conf` - Configuration file
- `clients/john-android_qr.png` - QR code for mobile setup

## Commands Reference

### Server Management

```bash
# Initial server setup
sudo ./vpn_manager.sh setup

# Check server status
./vpn_manager.sh server-status
```

### Client Management

```bash
# Add a new client
sudo ./vpn_manager.sh add-client <client-name>

# Remove a client
sudo ./vpn_manager.sh del-client <client-name>

# List all clients
./vpn_manager.sh list-clients

# Show QR code for a client
./vpn_manager.sh show-client-qr <client-name>

# Show help
./vpn_manager.sh help
```

### Example Commands

```bash
# Add clients for different devices
sudo ./vpn_manager.sh add-client john-phone
sudo ./vpn_manager.sh add-client mary-laptop
sudo ./vpn_manager.sh add-client office-tablet

# List all clients
./vpn_manager.sh list-clients

# Show QR code for mobile setup
./vpn_manager.sh show-client-qr john-phone

# Remove a client
sudo ./vpn_manager.sh del-client old-device

# Check server status
./vpn_manager.sh server-status
```

## Client Setup Instructions

### Android/iOS Setup

1. Install WireGuard app from Google Play Store or App Store
2. Add a client using the management script:
   ```bash
   sudo ./vpn_manager.sh add-client your-phone
   ```
3. Show the QR code:
   ```bash
   ./vpn_manager.sh show-client-qr your-phone
   ```
4. Open WireGuard app and tap "+" → "Create from QR code"
5. Scan the displayed QR code
6. Tap "Save" and toggle the connection on

### Windows Setup

1. Download and install WireGuard for Windows from [wireguard.com](https://www.wireguard.com/install/)
2. Add a client using the management script:
   ```bash
   sudo ./vpn_manager.sh add-client windows-laptop
   ```
3. Copy the configuration file `clients/windows-laptop.conf` to your Windows machine
4. Open WireGuard application
5. Click "Import tunnel(s) from file" and select the configuration file
6. Click "Activate" to connect

### Linux Setup

1. Install WireGuard:
   ```bash
   # Ubuntu/Debian
   sudo apt install wireguard

   # CentOS/RHEL
   sudo yum install wireguard-tools
   ```
2. Add a client using the management script:
   ```bash
   sudo ./vpn_manager.sh add-client linux-desktop
   ```
3. Copy the configuration file to the client machine:
   ```bash
   scp clients/linux-desktop.conf user@client-machine:/etc/wireguard/wg0.conf
   ```
4. Start WireGuard on the client:
   ```bash
   sudo wg-quick up wg0
   ```

## Directory Structure

```
vpn-manager/
├── vpn_manager.sh          # Main management script
├── README.md               # This documentation
├── templates/              # Configuration templates
│   ├── wg-client.conf      # Client configuration template
│   └── wg-server.conf      # Server configuration template
├── clients/                # Client configurations (created on first use)
│   ├── clients.db          # Client database
│   ├── client1.conf        # Client configuration files
│   └── client1_qr.png      # QR codes for mobile setup
├── keys/                   # Cryptographic keys (created on first use)
│   ├── server_private.key  # Server private key
│   ├── server_public.key   # Server public key
│   └── client_*.key        # Client keys
└── logs/                   # Log files (created on first use)
    └── vpn_manager.log     # Management operations log
```

## Security Features

### Key Management
- **Strong Key Generation**: Uses WireGuard's built-in key generation
- **PreShared Keys**: Additional layer of security for each client
- **Secure Storage**: All keys stored with 600 permissions (owner read/write only)
- **No Key Reuse**: Each client gets unique keys

### Access Control
- **IP Allocation**: Automatic IP assignment with no duplicates
- **Subnet Isolation**: Clients can only access their assigned subnet
- **AllowedIPs Restriction**: Fine-grained control over client traffic
- **Root Privilege Check**: Script requires root/sudo for sensitive operations

### Logging and Monitoring
- **Operation Logging**: All client add/remove operations logged
- **Timestamp Tracking**: Full audit trail with timestamps
- **Error Logging**: Comprehensive error tracking and reporting
- **Status Monitoring**: Real-time server and client status

## Network Configuration

### Default Network Settings
- **Server IP**: 10.66.66.1/24
- **Client IP Range**: 10.66.66.2 - 10.66.66.51
- **WireGuard Port**: 51820 (UDP)
- **UDP2RAW Port**: 4096 (for bypass functionality)
- **DNS Servers**: 8.8.8.8, 1.1.1.1

### Firewall Rules
The script automatically configures:
- IP forwarding enabled
- MASQUERADE rule for NAT
- FORWARD rules for WireGuard interface
- Automatic cleanup on service stop

## UDP2RAW Integration (Advanced)

For environments with strict NAT/CGNAT or VPN blocking (like in Russia), you can integrate UDP2RAW:

### Server Side (Frankfurt VPS)
```bash
# Install UDP2RAW
wget https://github.com/wangyu-/udp2raw-tunnel/releases/download/20200818.0/udp2raw_binaries.tar.gz
tar -xf udp2raw_binaries.tar.gz
sudo cp udp2raw_amd64 /usr/local/bin/udp2raw
sudo chmod +x /usr/local/bin/udp2raw

# Run UDP2RAW server (example)
sudo udp2raw -s -l 0.0.0.0:4096 -r 127.0.0.1:51820 -k "your-password" --raw-mode faketcp
```

### Client Side
```bash
# Run UDP2RAW client
sudo udp2raw -c -l 127.0.0.1:51820 -r YOUR_SERVER_IP:4096 -k "your-password" --raw-mode faketcp

# Then connect WireGuard to localhost:51820
```

## Troubleshooting

### Common Issues

1. **"Permission denied" errors**
   - Ensure you're running with sudo/root privileges
   - Check file permissions in keys/ directory

2. **"No available IP addresses"**
   - Maximum 50 clients supported
   - Remove unused clients to free up IPs

3. **WireGuard service not starting**
   - Check server configuration: `sudo wg show`
   - Verify firewall rules: `sudo iptables -L`
   - Check logs: `journalctl -u wg-quick@wg0`

4. **Client can't connect**
   - Verify server public IP in client config
   - Check if port 51820 is open in firewall
   - Ensure client has correct server endpoint

### Log Files

Check the management log:
```bash
tail -f logs/vpn_manager.log
```

Check WireGuard service logs:
```bash
sudo journalctl -u wg-quick@wg0 -f
```

### Useful Commands

```bash
# Show WireGuard status
sudo wg show

# Show network interfaces
ip addr show

# Check if WireGuard is listening
sudo netstat -ulpn | grep 51820

# Test connectivity from server
ping 10.66.66.2  # Replace with client IP

# Restart WireGuard service
sudo systemctl restart wg-quick@wg0
```

## Performance and Limitations

- **Maximum Clients**: 50 (can be modified in script)
- **Bandwidth**: Limited by server's network capacity
- **Latency**: Depends on server location and client connection
- **Memory Usage**: Minimal (WireGuard is lightweight)
- **CPU Usage**: Low impact on modern systems

## Backup and Recovery

### Backup Important Files
```bash
# Backup client database and keys
tar -czf vpn-backup-$(date +%Y%m%d).tar.gz clients/ keys/ logs/

# Backup server configuration
sudo cp /etc/wireguard/wg0.conf /path/to/backup/
```

### Recovery Process
1. Restore the backup files
2. Run the setup command to reconfigure the server
3. Restart WireGuard service

## Contributing

This script is designed to be easily extensible. Key areas for enhancement:
- Additional client platforms
- Advanced security features
- Web-based management interface
- Integration with monitoring systems
- Automated backup solutions

## License

This project is provided as-is for educational and practical VPN management purposes.

## Support

For issues and questions:
1. Check the troubleshooting section
2. Review log files for error details
3. Verify system requirements and dependencies
4. Ensure proper permissions and network configuration

---

**Note**: Always ensure you comply with local laws and regulations when using VPN services. This tool is designed for legitimate privacy and security purposes.