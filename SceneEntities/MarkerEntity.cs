using Ape.Core.Scene.Models;
using MessagePack;

namespace Ape.Module.MultiPeerTest;

/// <summary>
/// One replicated object. The author bumps <see cref="Revision"/>; every peer reads the same value.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
public sealed partial class MarkerEntity : Entity
{
    [IgnoreMember]
    private int _revision;

    public MarkerEntity() : base(MarkerCatalog.TypeId)
    {
    }

    [Key(10)]
    public int Revision
    {
        get => _revision;
        set => SetProperty(ref _revision, value);
    }
}
