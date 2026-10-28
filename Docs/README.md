# Multi-peer scene sync

The author owns three scene objects: `markers/lamp`, `markers/crate`, and `markers/orb`. It sets their revision to 0, then increments it on an interval (five seconds in the sample). A joiner does not create objects. Peers find each other over SSDP and read the same entities from their own scene.

Each process needs its own configuration directory. Ape opens one profile lock for the directory that contains the JSON file.

## Layout

| Path | Role |
| --- | --- |
| `Config/author/author.json` | Peer with `port: 0`. Authors the three markers every 5 seconds. |
| `Config/joiner/joiner.json` | Same shape, `author: false`. Start as many copies as you want. |
| `SceneEntities` | The replicated marker and its scene key. |
| `Services` | Loads with the module, before the network starts, and bumps the markers on the author. |
| `Common` | Reads the module section from the host JSON. |
| `Tests` | Grows a localhost mesh through 2, 3, 4, and 8 peers. |

Each process binds an ephemeral QUIC port and advertises it. The greater principal id dials, so a pair keeps one session. A fixed `peers` entry (`host`, `host` plus `port`, or `match`) still dials that address. `accept` limits who may connect inbound. Replication reaches only the peers this process is connected to.

## Run two processes

From the skeleton root, start the author:

```bash
dotnet run --project src/Ape.Examples/Ape.Example.MultiPeerTest -- \
  src/Ape.Examples/Ape.Example.MultiPeerTest/Config/author/author.json
```

Wait until the log contains `MultiPeer markers ready`. Then start the joiner:

```bash
dotnet run --project src/Ape.Examples/Ape.Example.MultiPeerTest -- \
  src/Ape.Examples/Ape.Example.MultiPeerTest/Config/joiner/joiner.json
```

The joiner log shows `MultiPeer joiner`. After the QUIC connection, its scene commit log shows the three markers. The author log shows `MultiPeer revision 1`, then `2`, and so on. The joiner's scene follows those revisions.

Start another joiner with the same file in another directory (each process needs its own profile lock). On one machine the advertisement is the LAN address, so dial `127.0.0.1` only in a fixed `peers` list. `localhost` makes MsQuic try IPv6 first and fail.

## Receipt file

Set `receipt` in `Ape.Module.MultiPeerTest` to a file path. The process rewrites that file with one `key revision` line per marker it can see. The automated test uses a separate file per peer.

## Publication and mounts

The author on port `19170` exports `markers/lamp` and `markers/crate` and holds `markers/orb`. The joiner dials `127.0.0.1:19170` and places the two exported markers under its own principal. The same files work against the Rust host on those ports. Start one author, then the joiner of either runtime.

```bash
dotnet run --project src/Ape.Examples/Ape.Example.MultiPeerTest -- \
  src/Ape.Examples/Ape.Example.MultiPeerTest/Config/publish-author/publish-author.json

dotnet run --project src/Ape.Examples/Ape.Example.MultiPeerTest -- \
  src/Ape.Examples/Ape.Example.MultiPeerTest/Config/mount-joiner/mount-joiner.json
```

The joiner log names the mounted key and the source path, for example `MultiPeer /{principal}/shared/lamp revision 1 (markers/lamp)`. The orb line does not appear.

## Automated test

From the skeleton root:

```bash
dotnet test \
  --tl:off \
  -clp:ErrorsOnly \
  src/Ape.Examples/Ape.Example.MultiPeerTest/Tests/Ape.Example.MultiPeerTest.Tests.csproj
```

The test starts the author, waits for the markers, then adds peers one at a time. Each joiner must observe revision 1 on all three objects before the next peer starts. The test interval is 200 milliseconds and the author stops at revision 1.
