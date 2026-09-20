#!/usr/bin/env bash
# Install / update the ShaChang match server on a fresh Debian or Ubuntu box.
#   scp shachang-server.tar.gz root@<ip>:/root/
#   ssh root@<ip> 'tar xzf shachang-server.tar.gz && bash deploy/install.sh'
set -euo pipefail

PORT="${PORT:-7777}"
DEST=/opt/shachang
USER_NAME=shachang

echo "==> unpacking into $DEST"
mkdir -p "$DEST"
cp -r ./shachang-server.x86_64 ./shachang-server_Data "$DEST"/ 2>/dev/null || true
[ -d ./UnityPlayer.so ] || cp -f ./UnityPlayer.so "$DEST"/ 2>/dev/null || true
cp -f ./*.so "$DEST"/ 2>/dev/null || true
chmod +x "$DEST/shachang-server.x86_64"

echo "==> service account"
id -u "$USER_NAME" >/dev/null 2>&1 || useradd --system --home "$DEST" --shell /usr/sbin/nologin "$USER_NAME"
chown -R "$USER_NAME:$USER_NAME" "$DEST"

echo "==> swap (a 512 MB box needs a cushion for the world build)"
if [ ! -f /swapfile ]; then
  fallocate -l 1G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=1024
  chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "==> systemd unit on port $PORT"
cat > /etc/systemd/system/shachang@.service <<UNIT
[Unit]
Description=ShaChang match server on port %i
After=network-online.target

[Service]
Type=simple
User=$USER_NAME
WorkingDirectory=$DEST
ExecStart=$DEST/shachang-server.x86_64 -server -port %i -batchmode -nographics -logFile $DEST/server-%i.log
Restart=always
RestartSec=5
MemoryMax=420M
Nice=-5

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now "shachang@$PORT"

echo "==> firewall"
if command -v ufw >/dev/null 2>&1; then ufw allow "$PORT"/udp || true; fi
if command -v nft >/dev/null 2>&1; then nft add rule inet filter input udp dport "$PORT" accept 2>/dev/null || true; fi

sleep 8
systemctl --no-pager status "shachang@$PORT" | head -n 12
echo
echo "log:  tail -f $DEST/server-$PORT.log"
echo "stop: systemctl stop shachang@$PORT"
echo "另一局: PORT=7778 bash deploy/install.sh   (每局一个端口)"
