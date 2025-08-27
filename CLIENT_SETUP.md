# WireGuard Client Setup Guide

This guide provides step-by-step instructions for setting up WireGuard clients on different platforms to connect to your Frankfurt VPS.

## Before You Start

1. Ensure your VPN server is running on the Frankfurt VPS
2. Add a client configuration using the server management script:
   ```bash
   sudo ./vpn_manager.sh add-client your-device-name
   ```
3. Get your client configuration file from the `clients/` directory

## Android Setup

### Standard Setup (No Blocking)

1. **Install WireGuard App**
   - Open Google Play Store
   - Search for "WireGuard" by WireGuard Development Team
   - Install the app

2. **Add VPN Configuration**
   - Generate QR code on server: `./vpn_manager.sh show-client-qr your-android`
   - Open WireGuard app
   - Tap the "+" button
   - Select "Create from QR code"
   - Scan the QR code displayed on your server
   - Give the tunnel a name (e.g., "Frankfurt VPN")
   - Tap "Create Tunnel"

3. **Connect**
   - Toggle the tunnel switch to connect
   - Grant VPN permission when prompted
   - You should see "Connected" status

### Bypass Setup (For Restricted Networks)

If your ISP blocks VPN traffic, use UDP2RAW:

1. **Install Termux** (Android terminal)
   - Install Termux from F-Droid or Google Play Store

2. **Setup UDP2RAW in Termux**
   ```bash
   # Update packages
   pkg update && pkg upgrade
   
   # Install required packages
   pkg install wget
   
   # Download UDP2RAW
   wget https://github.com/wangyu-/udp2raw-tunnel/releases/download/20200818.0/udp2raw_binaries.tar.gz
   tar -xzf udp2raw_binaries.tar.gz
   
   # Run UDP2RAW (replace with your server details)
   ./udp2raw_arm -c -l 127.0.0.1:51820 -r YOUR_SERVER_IP:4096 -k "YOUR_PASSWORD" --raw-mode faketcp
   ```

3. **Modify WireGuard Config**
   - In WireGuard app, edit your tunnel
   - Change the Endpoint from `YOUR_SERVER_IP:51820` to `127.0.0.1:51820`
   - Save the configuration

4. **Connect**
   - Start UDP2RAW in Termux first
   - Then activate the WireGuard tunnel

---

## iOS Setup

### Standard Setup

1. **Install WireGuard App**
   - Open App Store
   - Search for "WireGuard" by WireGuard Development Team
   - Install the app

2. **Add VPN Configuration**
   - Generate QR code: `./vpn_manager.sh show-client-qr your-iphone`
   - Open WireGuard app
   - Tap "+" in top-right corner
   - Select "Create from QR Code"
   - Scan the QR code
   - Give the tunnel a name
   - Tap "Save"

3. **Connect**
   - Tap the toggle switch next to your tunnel
   - Tap "Allow" when prompted to add VPN configuration
   - Enter your device passcode if requested

### Bypass Setup (For Restricted Networks)

iOS bypass setup is more complex due to platform restrictions:

1. **Use Shadowrocket or Similar App**
   - Install Shadowrocket (paid app) or similar proxy app
   - Configure it to forward traffic through your UDP2RAW server

2. **Alternative: VPN Chain**
   - Use a different VPN service first (if available)
   - Then connect WireGuard through that tunnel

---

## Windows Setup

### Standard Setup

1. **Install WireGuard for Windows**
   - Download from [https://www.wireguard.com/install/](https://www.wireguard.com/install/)
   - Run the installer as Administrator
   - Complete the installation

2. **Import Configuration**
   - Copy your client configuration file (e.g., `windows-laptop.conf`) to your Windows machine
   - Open WireGuard application
   - Click "Import tunnel(s) from file"
   - Select your configuration file
   - The tunnel will appear in the list

3. **Connect**
   - Click "Activate" next to your tunnel
   - Wait for the status to show "Active"

### Bypass Setup (UDP2RAW)

1. **Download UDP2RAW for Windows**
   ```powershell
   # Download from GitHub releases
   # https://github.com/wangyu-/udp2raw-tunnel/releases
   # Extract udp2raw.exe to a folder (e.g., C:\udp2raw\)
   ```

2. **Run UDP2RAW Client**
   - Open Command Prompt as Administrator
   - Navigate to UDP2RAW folder
   - Run:
   ```cmd
   udp2raw.exe -c -l 127.0.0.1:51820 -r YOUR_SERVER_IP:4096 -k "YOUR_PASSWORD" --raw-mode faketcp
   ```

3. **Modify WireGuard Configuration**
   - In WireGuard app, edit your tunnel
   - Change Endpoint from `YOUR_SERVER_IP:51820` to `127.0.0.1:51820`
   - Save and activate the tunnel

---

## Linux Desktop Setup

### Standard Setup

1. **Install WireGuard**
   ```bash
   # Ubuntu/Debian
   sudo apt update
   sudo apt install wireguard
   
   # CentOS/RHEL
   sudo yum install epel-release
   sudo yum install wireguard-tools
   
   # Arch Linux
   sudo pacman -S wireguard-tools
   ```

2. **Copy Configuration**
   ```bash
   # Copy your client config to the system
   sudo cp your-client.conf /etc/wireguard/wg0.conf
   
   # Set proper permissions
   sudo chmod 600 /etc/wireguard/wg0.conf
   ```

3. **Connect**
   ```bash
   # Start VPN
   sudo wg-quick up wg0
   
   # Enable auto-start on boot (optional)
   sudo systemctl enable wg-quick@wg0
   
   # Check status
   sudo wg show
   ```

4. **Disconnect**
   ```bash
   sudo wg-quick down wg0
   ```

### Bypass Setup (UDP2RAW)

1. **Install UDP2RAW**
   ```bash
   # Download and install
   wget https://github.com/wangyu-/udp2raw-tunnel/releases/download/20200818.0/udp2raw_binaries.tar.gz
   tar -xzf udp2raw_binaries.tar.gz
   sudo cp udp2raw_amd64 /usr/local/bin/udp2raw
   sudo chmod +x /usr/local/bin/udp2raw
   ```

2. **Create UDP2RAW Service**
   ```bash
   # Create service file
   sudo tee /etc/systemd/system/udp2raw-client.service > /dev/null <<EOF
   [Unit]
   Description=UDP2RAW Client
   After=network.target
   
   [Service]
   Type=simple
   User=root
   ExecStart=/usr/local/bin/udp2raw -c -l 127.0.0.1:51820 -r YOUR_SERVER_IP:4096 -k "YOUR_PASSWORD" --raw-mode faketcp
   Restart=always
   RestartSec=5
   
   [Install]
   WantedBy=multi-user.target
   EOF
   
   # Enable and start
   sudo systemctl daemon-reload
   sudo systemctl enable udp2raw-client
   sudo systemctl start udp2raw-client
   ```

3. **Modify WireGuard Config**
   ```bash
   # Edit your WireGuard config
   sudo nano /etc/wireguard/wg0.conf
   
   # Change Endpoint line to:
   # Endpoint = 127.0.0.1:51820
   ```

4. **Connect**
   ```bash
   # Start WireGuard (UDP2RAW should already be running)
   sudo wg-quick up wg0
   ```

---

## macOS Setup

### Standard Setup

1. **Install WireGuard**
   - Download from Mac App Store or [https://www.wireguard.com/install/](https://www.wireguard.com/install/)
   - Install the application

2. **Import Configuration**
   - Copy your client configuration file to your Mac
   - Open WireGuard application
   - Click "Import tunnel(s) from file"
   - Select your configuration file

3. **Connect**
   - Click the toggle switch next to your tunnel
   - Enter your password if prompted

### Bypass Setup

Similar to Linux setup, you can compile and run UDP2RAW on macOS:

1. **Install Homebrew** (if not already installed)
   ```bash
   /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
   ```

2. **Compile UDP2RAW**
   ```bash
   # Install dependencies
   brew install git make
   
   # Clone and compile
   git clone https://github.com/wangyu-/udp2raw-tunnel.git
   cd udp2raw-tunnel
   make
   
   # Install
   sudo cp udp2raw /usr/local/bin/
   ```

3. **Run UDP2RAW and modify WireGuard config** (same as Linux)

---

## Troubleshooting

### Common Issues

1. **Cannot Connect**
   - Check if server is running: `./vpn_manager.sh server-status`
   - Verify correct server IP in client config
   - Ensure firewall allows port 51820 (UDP) and 4096 (TCP for UDP2RAW)

2. **No Internet After Connecting**
   - Check DNS settings in client config
   - Verify server has internet access
   - Check if AllowedIPs is set correctly (0.0.0.0/0 for full tunnel)

3. **UDP2RAW Issues**
   - Verify password matches between client and server
   - Check if port 4096 is accessible from client
   - Ensure UDP2RAW is running on server

4. **Slow Performance**
   - Try different UDP2RAW modes (faketcp, udp, icmp)
   - Check server bandwidth and CPU usage
   - Consider reducing MTU size in WireGuard config

### Testing Your Connection

1. **Check IP Address**
   ```bash
   # Should show your VPN server's IP
   curl ifconfig.me
   ```

2. **DNS Leak Test**
   - Visit [https://dnsleaktest.com/](https://dnsleaktest.com/)
   - Should show your VPN server's location

3. **Speed Test**
   - Visit [https://speedtest.net/](https://speedtest.net/)
   - Compare with direct connection speed

### Getting Help

1. **Server Logs**
   ```bash
   # Check VPN manager logs
   tail -f logs/vpn_manager.log
   
   # Check WireGuard logs
   sudo journalctl -u wg-quick@wg0 -f
   ```

2. **Client Debugging**
   - Enable debug logging in WireGuard apps
   - Check system logs for VPN-related errors
   - Verify network connectivity without VPN first

---

## Security Best Practices

1. **Keep Apps Updated**
   - Regularly update WireGuard apps
   - Update server software

2. **Use Strong Passwords**
   - For UDP2RAW, use the generated password
   - Don't share passwords via insecure channels

3. **Monitor Usage**
   - Regularly check server logs
   - Monitor for unauthorized access attempts

4. **Backup Configurations**
   - Keep backups of your client configurations
   - Store them securely

---

For more advanced configuration options and server management, see the main [README.md](README.md) file.