using System.Runtime.Versioning;
using Ape.Core;

namespace Ape.Example.MultiPeerTest;

/// <summary>
/// One Ape peer. The JSON decides whether it authors the scene or only receives it.
/// </summary>
public static class Program
{
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    public static void Main(string[] args)
    {
        var configPath = args.FirstOrDefault(arg => !arg.StartsWith("--"));
        ApeSystem.Start(configPath, blocking: true);
    }
}
