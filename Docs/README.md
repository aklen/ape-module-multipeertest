# Multi-peer scene sync

The author owns three scene objects: `markers/lamp`, `markers/crate`, and `markers/orb`. It sets their revision to 0, then increments it on an interval (five seconds in the sample). A joiner does not create objects. It dials the peers listed in its config and reads the same entities from its own scene.

Each process needs its own configuration directory. Ape opens one profile lock for the directory that contains the JSON file.

## Layout

| Path | Role |
| --- | --- |
| `Config/author/author.json` | Peer on port `19160`. Authors the three markers every 5 seconds. |
| `Config/joiner/joiner.json` | Peer on port `19161`. Dials the author and only receives the scene. |
| `Module` | Marker entity and the service the host loads before the network starts. |
| `Tests` | Grows a localhost mesh through 2, 3, 4, and 8 peers. |

A new peer dials every peer that is already running, so the links are a mesh rather than a single chain. Replication reaches the peers the author is connected to directly.

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

A later machine uses the same joiner shape: `author` is false, `role` is `peer`, and `peers` lists the hosts it should dial. Use `localhost` on one machine. The development certificate includes that name.

## Receipt file

Set `receipt` in `Ape.Module.MultiPeerTest` to a file path. The process rewrites that file with one `key revision` line per marker it can see. The automated test uses a separate file per peer.

## Automated test

From the skeleton root:

```bash
dotnet test \
  --tl:off \
  -clp:ErrorsOnly \
  src/Ape.Examples/Ape.Example.MultiPeerTest/Tests/Ape.Example.MultiPeerTest.Tests.csproj
```

The test starts the author, waits for the markers, then adds peers one at a time. Each joiner must observe revision 1 on all three objects before the next peer starts. The test interval is 200 milliseconds and the author stops at revision 1.
