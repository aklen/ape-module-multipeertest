using Ape.Core.Config.Models;

namespace Ape.Module.MultiPeerTest;

public sealed class MultiPeerOptions
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    public bool Author { get; init; }

    public TimeSpan Interval { get; init; } = DefaultInterval;

    /// <summary>Zero keeps bumping. A positive value stops after that revision.</summary>
    public int MaxRevision { get; init; }

    public string? Receipt { get; init; }

    public static MultiPeerOptions Parse(IConfigNode? module)
    {
        if (module == null)
            return new MultiPeerOptions();

        var millis = module.GetInt("intervalMilliseconds", (int)DefaultInterval.TotalMilliseconds);
        if (millis < 0)
            millis = (int)DefaultInterval.TotalMilliseconds;

        var max = module.GetInt("maxRevision");
        if (max < 0)
            max = 0;

        var receipt = module.GetString("receipt").Trim();
        return new MultiPeerOptions
        {
            Author = module.GetBool("author"),
            Interval = TimeSpan.FromMilliseconds(millis),
            MaxRevision = max,
            Receipt = receipt.Length == 0 ? null : receipt
        };
    }
}
