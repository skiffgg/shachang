#!/usr/bin/env bash
# Install the room directory (python3, stdlib only) and re-point the match server at it.
set -euo pipefail

DEST=/opt/shachang
LOBBY_PORT="${LOBBY_PORT:-8080}"
GAME_PORT="${GAME_PORT:-7777}"
ROOM_NAME="${ROOM_NAME:-沙场 1 号房}"
PUBLIC_IP="${PUBLIC_IP:-$(curl -s --max-time 5 ifconfig.me || hostname -I | awk '{print $1}')}"

install -m 755 ./deploy/lobby.py "$DEST/lobby.py"
chown shachang:shachang "$DEST/lobby.py"

cat > /etc/systemd/system/shachang-lobby.service <<UNIT
[Unit]
Description=ShaChang room directory
After=network-online.target

[Service]
Type=simple
User=shachang
Environment=PORT=$LOBBY_PORT
Environment=HOST=$PUBLIC_IP
ExecStart=/usr/bin/python3 $DEST/lobby.py
Restart=always
RestartSec=5
MemoryMax=64M

[Install]
WantedBy=multi-user.target
UNIT

# the match server needs to know where to report
mkdir -p /etc/systemd/system/shachang@.service.d
cat > /etc/systemd/system/shachang@.service.d/lobby.conf <<CONF
[Service]
ExecStart=
ExecStart=$DEST/shachang-server.x86_64 -server -port %i -lobby http://127.0.0.1:$LOBBY_PORT -name "$ROOM_NAME" -batchmode -nographics -logFile $DEST/server-%i.log
CONF

systemctl daemon-reload
systemctl enable --now shachang-lobby
systemctl restart "shachang@$GAME_PORT"

command -v ufw >/dev/null 2>&1 && ufw allow "$LOBBY_PORT"/tcp || true

sleep 20
echo "== lobby =="; systemctl is-active shachang-lobby
echo "== rooms =="; curl -s "http://127.0.0.1:$LOBBY_PORT/rooms"; echo
echo "public: http://$PUBLIC_IP:$LOBBY_PORT"
