# VPN Server Deployment Guide

## Quick Installation

```bash
wget https://raw.githubusercontent.com/anhducp90-debug/ford/main/install-vpn.sh
sudo bash install-vpn.sh
```

## Pre-Installation Checklist

- [ ] Ubuntu 24.04 server with root access
- [ ] Domain name pointing to server IP (vpn.vietnga.info.vn)
- [ ] Ports 80, 443, and 51820 accessible
- [ ] At least 1GB RAM and 10GB disk space
- [ ] Internet connectivity for downloading packages

## Installation Process

The installation script performs these steps:

1. **System Updates**: Updates package lists and installs dependencies
2. **Docker Installation**: Installs Docker and Docker Compose
3. **WireGuard Setup**: Installs WireGuard and generates server keys
4. **Service Configuration**: Creates systemd services for auto-start
5. **Firewall Setup**: Configures UFW and iptables rules
6. **SSL Certificates**: Sets up Let's Encrypt for automatic TLS
7. **Web Interface**: Deploys management dashboard
8. **Client Tools**: Installs client management scripts

## Post-Installation Steps

### 1. DNS Configuration
Point your domain to the server:
```
vpn.vietnga.info.vn    A    YOUR_SERVER_IP
```

### 2. Initial Access
- Web Interface: https://vpn.vietnga.info.vn
- Default Password: `VpnAdmin2024!`
- **Change the password immediately!**

### 3. First Client Setup
```bash
sudo /opt/vpn-server/scripts/manage-client.sh add "Admin Device" admin@example.com
```

### 4. Service Verification
```bash
sudo systemctl status vpn-server.service
sudo systemctl status vpn-stats.service
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml ps
```

## Configuration Files

### Main Configuration
- `/opt/vpn-server/docker/docker-compose.yml` - Main service stack
- `/etc/vpn-server/wireguard/wg0.conf` - WireGuard server config
- `/opt/vpn-server/docker/caddy/Caddyfile` - Reverse proxy config

### Client Configurations
- `/etc/vpn-server/clients/` - Individual client configs
- Generated automatically with QR codes and helper scripts

## Maintenance Tasks

### Daily
- [ ] Check service status
- [ ] Monitor disk space
- [ ] Review access logs

### Weekly
- [ ] Update system packages
- [ ] Review client access
- [ ] Check certificate expiration

### Monthly
- [ ] Update Docker images
- [ ] Rotate client keys if needed
- [ ] Review firewall logs
- [ ] Backup configuration

## Backup Procedures

### Configuration Backup
```bash
sudo tar -czf vpn-backup-$(date +%Y%m%d).tar.gz /opt/vpn-server /etc/vpn-server
```

### Automated Backup Script
```bash
#!/bin/bash
BACKUP_DIR="/backups"
DATE=$(date +%Y%m%d)

mkdir -p $BACKUP_DIR
tar -czf $BACKUP_DIR/vpn-config-$DATE.tar.gz /opt/vpn-server /etc/vpn-server
find $BACKUP_DIR -name "vpn-config-*.tar.gz" -mtime +7 -delete
```

## Security Hardening

### Additional Security Measures
1. **SSH Key Authentication**: Disable password auth
2. **Fail2Ban**: Already installed, monitor with `sudo fail2ban-client status`
3. **Regular Updates**: Set up automatic security updates
4. **Monitoring**: Set up log monitoring and alerts
5. **Access Control**: Limit SSH access to specific IPs

### Firewall Rules
```bash
# View current rules
sudo ufw status verbose
sudo iptables -L -n -v

# Block suspicious IP
sudo /opt/vpn-server/scripts/firewall-manager.sh block-ip 1.2.3.4
```

## Troubleshooting Guide

### Certificate Issues
```bash
# Check certificate status
sudo docker exec vpn-caddy caddy list-certificates

# Force renewal
sudo docker exec vpn-caddy caddy reload
```

### Connection Problems
```bash
# Check WireGuard status
sudo wg show

# Test connectivity
ping 10.66.66.1
curl -I https://vpn.vietnga.info.vn
```

### Service Issues
```bash
# Restart services
sudo systemctl restart vpn-server.service

# Check logs
sudo journalctl -u vpn-server.service -f
sudo docker-compose -f /opt/vpn-server/docker/docker-compose.yml logs -f
```

### Performance Issues
```bash
# Check resource usage
sudo docker stats
sudo systemctl status

# Monitor network
sudo iftop -i wg0
sudo netstat -tuln
```

## Client Connection Methods

### Standard Connection (Direct WireGuard)
Best for networks that don't block VPN traffic:
```bash
./client-helper.sh setup config.conf
./client-helper.sh start
```

### WebSocket Tunnel (For Restricted Networks)
For networks that block VPN traffic:
```bash
./client-helper.sh setup config.conf true
./client-helper.sh start websocket
```

### Mobile Devices
1. Install WireGuard app
2. Scan QR code from web interface
3. Connect and test

## Performance Optimization

### Server Optimization
```bash
# Increase connection limits
echo 'net.core.somaxconn = 65535' >> /etc/sysctl.conf
echo 'net.ipv4.tcp_max_syn_backlog = 65535' >> /etc/sysctl.conf
sysctl -p
```

### Docker Resource Limits
Edit `/opt/vpn-server/docker/docker-compose.yml`:
```yaml
services:
  caddy:
    deploy:
      resources:
        limits:
          memory: 256M
        reservations:
          memory: 128M
```

## Monitoring and Alerts

### Log Monitoring
```bash
# Real-time logs
sudo tail -f /var/log/vpn-install.log
sudo tail -f /var/log/vpn-stats.log

# Caddy logs
sudo docker logs -f vpn-caddy
```

### Statistics API
```bash
# Current stats
curl -s http://localhost/api/stats | jq

# Health check
curl -s http://localhost/api/status
```

## Scaling Considerations

### Multiple Servers
- Use different subnets (10.66.67.0/24, 10.66.68.0/24)
- Share client database
- Load balance with DNS round-robin

### High Availability
- Set up server clustering
- Use external database for client configs
- Implement health checks and failover

## Legal and Compliance

### Important Notes
- Ensure compliance with local laws
- Maintain access logs as required
- Implement data retention policies
- Consider GDPR compliance for EU users

### Log Retention
```bash
# Set log rotation
sudo logrotate -f /etc/logrotate.conf

# Archive old logs
find /var/log -name "*.log" -mtime +30 -exec gzip {} \;
```

## Support and Resources

### Getting Help
1. Check installation logs: `/var/log/vpn-install.log`
2. Review service status: `sudo systemctl status vpn-server.service`
3. Test connectivity: `ping vpn.vietnga.info.vn`
4. Check DNS resolution: `nslookup vpn.vietnga.info.vn`

### Useful Commands
```bash
# Complete service restart
sudo systemctl restart vpn-server.service vpn-stats.service

# Force certificate renewal
sudo docker exec vpn-caddy caddy reload

# Client management
sudo /opt/vpn-server/scripts/manage-client.sh list

# View active connections
sudo wg show

# Check firewall status
sudo ufw status verbose
```

This deployment guide provides comprehensive instructions for setting up, maintaining, and troubleshooting your VPN server.