using System.Globalization;
using System.Text;
using Ape.Core.Config;
using Ape.Core.Config.Models;
using Ape.Core.Determinism;
using Ape.Core.Logging;
using Ape.Core.Runtime.Service;
using Ape.Core.Scene;
using Ape.Core.Scene.Commit;
using Microsoft.Extensions.DependencyInjection;

namespace Ape.Module.MultiPeerTest;

/// <summary>
/// Registers the marker entity, and on the author creates the three objects and bumps their revision.
/// Every peer writes the scene it can see to the optional receipt file.
/// </summary>
public sealed class MultiPeerService : IPluggableService, IDeterministicFrameParticipant
{
    public string ServiceId => "multipeer-markers";
    public string Name => "Multi-peer markers";
    public string ParticipantId => "multipeer-markers";
    public FramePhase Phase => FramePhase.Publish;
    public int Order => 20;

    private ILogger _logger = null!;
    private ISceneRead? _scene;
    private MultiPeerOptions _options = new();
    private bool _created;
    private bool _announced;
    private int _revision;
    private DateTime _nextBumpUtc = DateTime.MaxValue;

    public void Register(IServiceCollection serviceCollection)
    {
    }

    public void Initialize(IServiceProvider services)
    {
        _logger = services.GetRequiredService<ILogger>();
        _scene = services.GetService<ISceneRead>();
        var hostCfg = services.GetService<IStartupConfig>();
        var modules = services.GetService<IModuleTable>();
        _options = MultiPeerOptions.Parse(modules?.GetModuleSection(hostCfg?.Root, "Ape.Module.MultiPeerTest"));

        var registry = services.GetRequiredService<ISceneEntityRegistry>();
        registry.Register(MarkerCatalog.TypeId, (_, id, _) => new MarkerEntity { Id = id });

        var frames = services.GetService<IFrameParticipantRegistry>();
        if (frames == null)
            _logger.LogWarning("MultiPeer markers will not tick: frame registry is missing.");
        else
            frames.Register(this);

        _logger.LogInfo(_options.Author
            ? $"MultiPeer author every {_options.Interval.TotalMilliseconds:0}ms"
            : "MultiPeer joiner");
    }

    public void Start(CancellationToken cancellationToken)
    {
    }

    public void Stop()
    {
    }

    public void OnHostFrame(in FrameContext context, IFrameCommitBatch commits)
    {
        if (_options.Author && !_created)
        {
            foreach (var key in MarkerCatalog.Keys)
                commits.Enqueue(new CreateRegisteredEntityCommitRequest(MarkerCatalog.TypeId, key));
            _created = true;
        }

        var markers = (_scene?.GetEntities<MarkerEntity>() ?? []).ToList();
        if (_options.Author && !_announced && MarkerCatalog.Keys.All(key => markers.Exists(marker => marker.Id == key)))
        {
            _announced = true;
            _nextBumpUtc = DateTime.UtcNow + _options.Interval;
            _logger.LogInfo("MultiPeer markers ready");
        }

        if (_options.Author && _announced && Due())
        {
            _revision++;
            _nextBumpUtc = DateTime.UtcNow + _options.Interval;
            foreach (var key in MarkerCatalog.Keys)
                commits.Enqueue(new SetSceneEntityPropertyCommitRequest(key, nameof(MarkerEntity.Revision), _revision));
            _logger.LogInfo($"MultiPeer revision {_revision}");
        }

        WriteReceipt(markers);
    }

    private bool Due()
    {
        if (_options.MaxRevision > 0 && _revision >= _options.MaxRevision)
            return false;
        return DateTime.UtcNow >= _nextBumpUtc;
    }

    private void WriteReceipt(IReadOnlyList<MarkerEntity> markers)
    {
        if (string.IsNullOrEmpty(_options.Receipt) || markers.Count == 0)
            return;

        var text = new StringBuilder();
        foreach (var marker in markers.OrderBy(marker => marker.Id, StringComparer.Ordinal))
            text.Append(marker.Id).Append(' ').Append(marker.Revision.ToString(CultureInfo.InvariantCulture)).Append('\n');

        var path = _options.Receipt;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text.ToString());
        File.Move(temporary, path, overwrite: true);
    }
}
