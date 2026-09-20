# Testing

The game is verified by running the real build headlessly or windowed, taking screenshots and reading
logs — not by eyeballing the editor. Every fix in this project was confirmed that way before being
called done.

## Compiling and building

```bash
./build.ps1 -method Builder.Setup        # regenerate the scene and assets
./build.ps1 -method Builder.Build        # Windows player
./build.ps1 -method NetTests.Run         # hit-geometry unit tests, prints PASSED / FAILED
```

`build.ps1` opens Unity in batch mode, runs the method and prints every `error CS…`, `[Builder]` and
compile-failure line, so a broken script is visible in the terminal without opening the editor.

## Screenshots

```bash
./shot.ps1 -name mg -extra "-mg -quality 2"
```

`shot.ps1` launches `Build/ShaChang.exe` windowed, waits for it to take an automated screenshot into
`shots/<name>.png`, then prints the stats line and any exception in the player log. Useful flags built
into the game for this purpose:

| Flag | Effect |
|---|---|
| `-shot <path> -delay <s>` | Take a screenshot after N seconds and quit |
| `-mode endless\|extract\|conquest` | Start that mode immediately |
| `-pose`, `-weapon <n>`, `-allatt` | Pose the player, select a weapon, fit every attachment |
| `-heli`, `-mg` | Put the player in the helicopter or behind the pickup's gun |
| `-storm`, `-dayt <0..1>`, `-quality <0..2>` | Force a sandstorm, a time of day, a quality preset |
| `-showmap`, `-bag`, `-attdbg` | Open the map, the backpack, the attachment debug overlay |
| `-nopp`, `-noshadow`, `-noworld`, `-noenemies` | Isolate a rendering or gameplay layer |

## Network tests

`Assets/Editor/NetTests.cs` exercises the rewound hit geometry directly — the maths that replaced
Unity colliders for player hits, where a mistake would silently break every shot online:

```bash
./build.ps1 -method NetTests.Run
[NetTest] ok   chest, level shot   hit=True head=False dist=19.64
[NetTest] ok   head shot           hit=True head=True  dist=19.78
...
[Builder] net tests PASSED
```

For the live path, run a server and two clients on one machine:

```bash
ShaChang.exe -server -port 7801 -duel -netdebug -batchmode -nographics -logFile srv.log
ShaChang.exe -connect 127.0.0.1 -port 7801 -netdebug -autofire  -shot a.png -delay 45 -logFile a.log
ShaChang.exe -connect 127.0.0.1 -port 7801 -netdebug -autoaim   -shot b.png -delay 45 -logFile b.log
```

| Flag | Effect |
|---|---|
| `-duel` | Server: both teams spawn in the same corner, so a two-client test can actually meet |
| `-netdebug` | Log shots with the rewind used, seat changes, vehicle poses and a periodic "what do I see" probe |
| `-autofire` | Client: aim at the nearest hostile player and keep firing |
| `-autoaim` | Client: aim at them without shooting, for looking at remote avatars |
| `-drivetest` | Client: get into the armed pickup and drive it in a straight line |
| `-watchveh` | Client: stand beside that pickup and watch it, for checking vehicle sync |

The server log then shows exactly what the authority decided:

```
[Net] shot by 24346 rewind=100ms rtt=0 -> HEAD 32120 at 10.2m
[Net] vehicle 6 pose from 0 at (-88, 20, -119)
```

and each client's probe line shows whether it can still see the other player:

```
[Net] t=12 others=1 dist=59 me=(-106, 20, -116) veh=True
```

A remote player whose distance never changes while the server sees them moving means updates are being
dropped — that is how the rpc-link problem in [multiplayer.md](multiplayer.md) was found.

## Housekeeping

Leftover server processes keep holding their UDP port and make the next run fail to start:

```powershell
Get-Process ShaChang -ErrorAction SilentlyContinue | Stop-Process -Force
```
