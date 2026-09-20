# Multiplayer

6v6 team deathmatch on a dedicated Linux server, joined from an in-game room list. Built on
FishNet 4.7.3 with the Tugboat UDP transport.

## Why it is cheap to run

Every machine generates the same world from the same seed, so nothing about the level is ever sent
over the wire. A match server holds terrain, colliders and a navmesh — about 250 MB of RSS — and the
traffic is only players, shots and vehicle seats. A $5 VPS with 1 GB of RAM runs a full 12-player
match.

## Objects

| Object | Where it lives | Purpose |
|---|---|---|
| `NetworkManager` + `Tugboat` + `NetBoot` | Scene, created by `Builder` | Starts a server or a client from the command line |
| `Match` prefab (`TdmMatch`) | `Resources/Net/Match.prefab`, spawned by the server | Scores, clock, teams, spawns, respawn timers |
| `NetPlayer` prefab | `Resources/Net/NetPlayer.prefab`, one per connection | Pose streaming, authoritative shooting, vehicle relay |
| `NetVehicles` | Plain static state, no network object | Seat ownership and interpolation for the deterministic vehicle list |

Both prefabs are registered in `Resources/Net/SpawnablePrefabs.asset`. Nothing networked is placed in
the scene by hand — see the note on scene ids at the end.

## Player state

The owning client keeps playing with the ordinary first-person controller; its `NetPlayer` mirrors
that pose and streams it to the server 20 times a second. The server relays it to everyone else, who
keep a short ring buffer of poses and **render remote players 100 ms in the past**. That fixed delay
is what makes other players glide instead of teleport, and it is also the reason lag compensation has
to rewind by the same amount.

Remote avatars are the same soldier model as the AI, tinted per team on top of its own texture (not
painted over it), with the rifle mounted between the hands, hips pinned under the collider, the torso
leaning with the aim, and clip selection — idle, walk, run, strafe, backpedal, crouch, death — driven
by the velocity read back out of the pose buffer. A player sitting in a vehicle is hidden, because the
vehicle draws them.

## Shooting: server authoritative and lag compensated

Clients never claim a kill. `LocalFire` sends the shot's origin, direction, damage and range plus the
client's own round-trip estimate; the server decides everything.

```
rewind  = clamp(rtt / 2 + 100 ms, 0, 400 ms)
history = each player's positions, 20 Hz, 1.2 s deep
```

The server rewinds every candidate victim to `now - rewind`, which is the world the shooter actually
saw, then tests the shot against a body capsule and a head sphere sized from that player's stance
(crouched players are shorter). Level geometry is not rewound — it does not move — so a normal
raycast decides where the bullet stops and only players nearer than that wall can be hit.

The hit geometry is not Unity colliders but explicit maths, so it can be rewound cheaply and tested
without touching the physics scene. `Assets/Editor/NetTests.cs` covers it (`Build > Net tests`): chest,
head, legs, over the head, wide misses, grazing shots, shots from behind, crouched stances and steep
angles. The capsule cap overlaps the skull, so a hit that passes through the head sphere at roughly
the same range counts as a head shot — without that rule a clean head shot scored as a chest hit.

Head shots do 2.2× damage. The shooter gets a hit marker from a `TargetRpc`, the victim gets the
direction indicator, and health is authoritative.

## Vehicles

The vehicle list is identical on every machine, spawned by `Game.SpawnMatchVehicles()`, and indexed by
kind plus rounded position so the numbering can never depend on spawn timing. From there:

- A player entering a car asks the server for the seat. The server keeps the seat table and either
  grants it or bounces the player back out with "this one is taken".
- The driver streams the vehicle transform 20 times a second through their own `NetPlayer`. The server
  applies it (it needs the car in the right place for shooting) and relays it; everyone else glides
  their local copy toward the relayed pose.
- Vehicle damage stays client side, but the machine that destroys a car tells the server, so a wreck
  never survives on one screen and drives on another.

## Match rules

`TdmMatch`: 75 kills or 10 minutes, whichever comes first, 8-second respawns, teams balanced on join,
and two opposite spawn corners. Everything runs on the server; clients only read the synced numbers.

## Room list

The server posts a heartbeat every 5 seconds to a tiny Python room directory
(`deploy/lobby.py`, standard library only, ~60 MB of RAM). The client's menu reads
`GET /rooms` and lists the name, mode, player count and state, so players never type an IP.

## Pitfalls worth remembering

These each cost a full debugging cycle and are easy to hit again.

1. **Scene network objects created by a script have no scene id.** FishNet then logs "expected to be
   initialized but was not", the object half-spawns, `IsSpawned` stays false and nobody gets a team.
   Register such objects as ordinary spawnable prefabs and instantiate them at runtime instead.
2. **RPC links silently drop rpcs.** FishNet compresses rpc headers into two-byte links after the
   first sends; when the two sides disagree about the table, the receiver skips the packet *without a
   warning*. Remote players froze at their spawn while the server saw them moving. `NetBoot` now sets
   `DebugManager.DisableObserversRpcLinks` (and the target and server equivalents) before starting.
3. **`ObserversRpc` attribute options were unreliable here.** `BufferLast` and `ExcludeOwner` produced
   rpcs that only ever executed on the owner. Plain `[ObserversRpc]` with an `if (IsOwner) return;`
   inside the method works everywhere.
4. **Network properties throw before the behaviour is initialised.** A scene object's `Update` runs
   long before `OnStartNetwork`, and touching `IsClientInitialized` there throws every frame. Track a
   `live` flag set in `OnStartNetwork` / `OnStopNetwork`.
5. **The transport can report `Started` twice**, which spawned the match object twice. Spawning is now
   idempotent, and a failed spawn is discarded and retried on the next event.

## Network conditions

UDP is the right transport for a shooter, but some networks make it hard. A carrier-grade NAT that
hands out a pool of public addresses can change the source address between packets, so the server's
replies go to an endpoint the client never sees and the handshake never completes — the same network
also blocked UDP 7777 outright while UDP 8443 passed, which is why the reference server listens on
8443. On such a connection, play through a tunnel or from another network.

## Roadmap

1. Team deathmatch — working end to end, in playtest.
2. Conquest online, reusing the existing capture-point logic.
3. PvPvE extraction, which needs AI running on the server as well.
