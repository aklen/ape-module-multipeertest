using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Ape.Module.MultiPeerTest;

namespace Ape.Example.MultiPeerTest.Tests;

/// <summary>
/// Starts one author, then adds peers one at a time. Each new peer dials every peer already running.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
[Collection("mesh")]
public sealed class GrowingMeshTests
{
    [Fact]
    public void Two_runs_write_the_scene_and_the_peers_in_the_same_order()
    {
        var first = CaptureOrder();
        var second = CaptureOrder();

        Assert.Equal(first.Commits, second.Commits);
        Assert.True(Registers(first.Replica).SequenceEqual(FirstSends(first.AuthorReplica)),
            "Author send order and joiner register order diverged."
            + Environment.NewLine + "send:" + Environment.NewLine + string.Join(Environment.NewLine, first.AuthorReplica)
            + Environment.NewLine + "register:" + Environment.NewLine + string.Join(Environment.NewLine, first.Replica));
        Assert.Equal(first.Replica, second.Replica);
        Assert.Equal(CommitKeys(first.Commits, "create-entity"), Registers(first.Replica));
        Assert.Equal(RevisionCommits(first.Commits), RevisionUpdates(first.Replica));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void Joiners_see_the_authors_markers(int peers)
    {
        var hostDir = FindHostOutput();
        var hostDll = Path.Combine(hostDir, "Ape.Example.MultiPeerTest.dll");
        Assert.True(File.Exists(hostDll), hostDll);
        Assert.True(File.Exists(Path.Combine(hostDir, "Ape.Module.MultiPeerTest.dll")));

        var root = Path.Combine(Path.GetTempPath(), "ape-multipeer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ports = FreePorts(peers);
        var running = new List<Running>();
        try
        {
            var authorReceipt = Path.Combine(root, "peer-0.txt");
            running.Add(Start(hostDir, hostDll, WriteConfig(root, 0, ports, author: true, authorReceipt)));
            if (!WaitFor(running[0], "MultiPeer markers ready", TimeSpan.FromSeconds(20)))
                Assert.Fail("The author did not publish its markers." + Environment.NewLine + running[0].Output);

            for (var index = 1; index < peers; index++)
            {
                var receipt = Path.Combine(root, $"peer-{index}.txt");
                running.Add(Start(hostDir, hostDll, WriteConfig(root, index, ports, author: false, receipt)));
                if (!WaitUntilSynced(receipt, TimeSpan.FromSeconds(20), running))
                {
                    Assert.Fail(
                        $"Peer {index} did not match the author's scene."
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, running.Select((peer, i) => $"peer {i}:{Environment.NewLine}{peer.Output}")));
                }
            }
        }
        finally
        {
            foreach (var peer in running)
                Stop(peer);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static bool WaitUntilSynced(string receipt, TimeSpan timeout, IReadOnlyList<Running> running)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (running.Any(peer => peer.HasExited))
                return false;
            if (Synced(receipt))
                return true;
            Thread.Sleep(100);
        }

        return false;
    }

    private static bool Synced(string receipt)
    {
        if (!File.Exists(receipt))
            return false;

        Dictionary<string, int> seen;
        try
        {
            seen = ReadReceipt(receipt);
        }
        catch (IOException)
        {
            return false;
        }

        return MarkerCatalog.Keys.All(key => seen.TryGetValue(key, out var revision) && revision >= 1);
    }

    private static Dictionary<string, int> ReadReceipt(string path)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision))
                seen[parts[0]] = revision;
        }

        return seen;
    }

    private (string[] Commits, string[] Replica, string[] AuthorReplica) CaptureOrder()
    {
        var hostDir = FindHostOutput();
        var hostDll = Path.Combine(hostDir, "Ape.Example.MultiPeerTest.dll");
        var root = Path.Combine(Path.GetTempPath(), "ape-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ports = FreePorts(2);
        var commits = Path.Combine(root, "author-commits.log");
        var authorReplica = Path.Combine(root, "author-replica.log");
        var replica = Path.Combine(root, "joiner-replica.log");
        var running = new List<Running>();
        try
        {
            var authorReceipt = Path.Combine(root, "peer-0.txt");
            running.Add(Start(hostDir, hostDll, WriteConfig(root, 0, ports, author: true, authorReceipt, commits, authorReplica, 3000)));
            if (!WaitFor(running[0], "MultiPeer markers ready", TimeSpan.FromSeconds(20)))
                Assert.Fail("The author did not publish its markers." + Environment.NewLine + running[0].Output);

            var joinerReceipt = Path.Combine(root, "peer-1.txt");
            running.Add(Start(hostDir, hostDll, WriteConfig(root, 1, ports, author: false, joinerReceipt, null, replica, 3000)));
            if (!WaitUntilSynced(joinerReceipt, TimeSpan.FromSeconds(20), running))
                Assert.Fail("The joiner did not match the author's scene." + Environment.NewLine + running[1].Output);
            if (!WaitForFile(commits, lines => lines.Any(line => line.Contains("\tset-entity\t", StringComparison.Ordinal)), TimeSpan.FromSeconds(5)))
                Assert.Fail("The author commit journal did not record a revision." + Environment.NewLine + running[0].Output);
            if (!WaitForFile(replica, lines => RevisionUpdates(lines).Length == MarkerCatalog.Keys.Length, TimeSpan.FromSeconds(10)))
                Assert.Fail("The joiner replica journal did not record every revision." + Environment.NewLine + string.Join(Environment.NewLine, SafeRead(replica)));

            return (File.ReadAllLines(commits), File.ReadAllLines(replica), File.ReadAllLines(authorReplica));
        }
        finally
        {
            foreach (var peer in running)
                Stop(peer);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static bool WaitForFile(string path, Func<string[], bool> ready, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ready(SafeRead(path)))
                return true;
            Thread.Sleep(50);
        }

        return ready(SafeRead(path));
    }

    private static string[] SafeRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static string[] CommitKeys(IEnumerable<string> lines, string kind)
    {
        var keyIndex = kind == "set-entity" ? 2 : 3;
        var names = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length > keyIndex && parts[1] == kind)
                names.Add(parts[keyIndex]);
        }

        return names.ToArray();
    }

    private static string[] FirstSends(IEnumerable<string> lines)
    {
        var seen = new List<string>();
        foreach (var id in Sends(lines))
        {
            if (!seen.Contains(id))
                seen.Add(id);
        }

        return seen.ToArray();
    }

    private static string[] Sends(IEnumerable<string> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && parts[0] == "send")
                names.Add(parts[1]);
        }

        return names.ToArray();
    }

    private static string[] Registers(IEnumerable<string> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && parts[0] == "register")
                names.Add(parts[1]);
        }

        return names.ToArray();
    }

    private static string[] RevisionCommits(IEnumerable<string> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 4 && parts[1] == "set-entity" && parts[3] == "Revision")
                names.Add(parts[2]);
        }

        return names.ToArray();
    }

    private static string[] RevisionUpdates(IEnumerable<string> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3 && parts[0] == "update" && parts[2] == "Revision")
                names.Add(parts[1]);
        }

        return names.ToArray();
    }

    private static string WriteConfig(
        string root,
        int index,
        IReadOnlyList<int> ports,
        bool author,
        string receipt,
        string? commitJournal = null,
        string? replicaJournal = null,
        int intervalMilliseconds = 200)
    {
        var directory = Path.Combine(root, $"peer-{index}");
        Directory.CreateDirectory(directory);
        var peers = new StringBuilder();
        for (var earlier = 0; earlier < index; earlier++)
        {
            if (peers.Length > 0)
                peers.AppendLine(",");
            peers.Append("        { \"host\": \"127.0.0.1\", \"port\": ").Append(ports[earlier].ToString(CultureInfo.InvariantCulture)).Append(" }");
        }

        var scene = new StringBuilder("\"logCommits\": true");
        if (commitJournal != null)
            scene.Append(", \"commitJournalPath\": ").Append(JsonString(commitJournal));
        if (replicaJournal != null)
            scene.Append(", \"replicaJournalPath\": ").Append(JsonString(replicaJournal));

        var json = $$"""
        {
          "modules": {
            "Ape.Core.Logging": { "enabled": true, "level": "info", "transport": "console" },
            "Ape.Core.Network": {
              "enabled": true,
              "transport": "quic",
              "role": "peer",
              "peerName": "peer-{{index}}",
              "port": {{ports[index]}},
              "keepAliveSeconds": 15,
              "reconnect": { "initialSeconds": 1, "maxSeconds": 8 },
              "peers": [
        {{peers}}
              ]
            },
            "Ape.Core.Scene": { {{scene}} },
            "Ape.Module.MultiPeerTest": {
              "author": {{(author ? "true" : "false")}},
              "intervalMilliseconds": {{intervalMilliseconds}},
              "maxRevision": 1,
              "receipt": {{JsonString(receipt)}}
            }
          }
        }
        """;
        var path = Path.Combine(directory, "peer.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string JsonString(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";

    private static int[] FreePorts(int count)
    {
        var sockets = new Socket[count];
        var ports = new int[count];
        try
        {
            for (var i = 0; i < count; i++)
            {
                sockets[i] = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                sockets[i].Bind(new IPEndPoint(IPAddress.Loopback, 0));
                ports[i] = ((IPEndPoint)sockets[i].LocalEndPoint!).Port;
            }
        }
        finally
        {
            foreach (var socket in sockets)
                socket?.Dispose();
        }

        return ports;
    }

    private static string FindHostOutput()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var configuration in new[] { "", "Debug", "Release" })
            {
                var candidate = string.IsNullOrEmpty(configuration)
                    ? Path.Combine(dir.FullName, "bin", "Ape.Example.MultiPeerTest", "net10.0")
                    : Path.Combine(dir.FullName, "bin", "Ape.Example.MultiPeerTest", configuration, "net10.0");
                if (File.Exists(Path.Combine(candidate, "Ape.Example.MultiPeerTest.dll")))
                    return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Host output was not found next to the test build.");
    }

    private static bool WaitFor(Running running, string line, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (running.Output.Contains(line, StringComparison.Ordinal))
                return true;
            if (running.HasExited)
                return false;
            Thread.Sleep(100);
        }

        return false;
    }

    private static Running Start(string hostDir, string hostDll, string config)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = hostDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(hostDll);
        start.ArgumentList.Add(config);
        var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet exec did not start.");
        return new Running(process);
    }

    private static void Stop(Running running)
    {
        try
        {
            if (!running.Process.HasExited)
                running.Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        running.Process.WaitForExit(5_000);
        running.Process.Dispose();
    }

    private sealed class Running : IDisposable
    {
        public Running(Process process)
        {
            Process = process;
            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public Process Process { get; }

        public string Output
        {
            get
            {
                lock (_output)
                    return _output.ToString();
            }
        }

        public bool HasExited => Process.HasExited;

        public void Dispose() => Process.Dispose();

        private readonly StringBuilder _output = new();

        private void Append(string? line)
        {
            if (line is null)
                return;
            lock (_output)
            {
                if (_output.Length < 64_000)
                    _output.AppendLine(line);
            }
        }
    }
}

[CollectionDefinition("mesh", DisableParallelization = true)]
public sealed class MeshCollection;
