# Running a dedicated server

The reference deployment is a $5 Vultr instance (1 vCPU, 1 GB RAM, Debian 13) in the United States,
which comfortably runs one 12-player match plus the room directory.

## What gets installed

| Unit | What it is | Memory |
|---|---|---|
| `shachang@<port>.service` | The match server, one instance per UDP port | ~250 MB (capped at 420 MB) |
| `shachang-lobby.service` | The Python room directory on TCP 8080 | capped at 64 MB |

A 1 GB swap file is created as a safety net, and the firewall is opened for the game port (UDP) and
the lobby port (TCP).

## Install

```bash
# on the build machine
./build.ps1 -method Builder.BuildLinuxServer
tar -czf shachang-server.tar.gz ServerBuild
scp shachang-server.tar.gz root@<host>:/root/

# on the server
tar -xzf shachang-server.tar.gz
cd ServerBuild
./deploy/install.sh          # user, systemd unit, swap, firewall, first start
./deploy/install-lobby.sh    # room directory, and point the match server at it
```

`install-lobby.sh` writes a drop-in that adds `-lobby http://127.0.0.1:8080` and the room name to the
match server's command line, so the room shows up in the in-game browser:

```bash
curl -s http://<host>:8080/rooms
[{"host": "<host>", "port": 8443, "name": "沙场 1 号房", "mode": "tdm", "players": 0, "max": 12, "state": "waiting"}]
```

## Operating

```bash
systemctl status shachang@8443           # is the match up
tail -f /opt/shachang/server-8443.log    # its Unity log
systemctl restart shachang@8443          # restart; the world takes ~60 s to generate
ps -o rss= -C shachang-server.x86_64     # resident memory in KB
```

The log file belongs to the `shachang` user. Truncating or creating it as root makes the service fail
with "Unable to open log file, exiting" — delete it instead and let the service recreate it.

## Choosing a port

The server takes any UDP port (`shachang@<port>`), and the room directory advertises whatever port the
instance reports. Some networks block arbitrary high UDP ports while letting common ones through; the
reference server therefore listens on **UDP 8443**. If players cannot connect, check that traffic
actually reaches the box before suspecting the game:

```bash
tcpdump -n -c 10 udp port 8443
```

No packets means the path is blocked upstream. Packets arriving from a source address that keeps
changing mean a carrier-grade NAT with an address pool, which breaks UDP sessions for any game.

## Adding a second match

```bash
ufw allow 8444/udp
systemctl enable --now shachang@8444
```

Each instance needs roughly 250 MB, so on the 1 GB plan run one at a time; the 2 GB plan holds two
comfortably.
