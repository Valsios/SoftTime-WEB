using System.Collections.Concurrent;

namespace SoftTime.Application.Security;

/// <summary>Cache mémoire des droits effectifs, 30 secondes par compte. Une seule instance de l'application est supposée.</summary>
public sealed class EffectiveRightsCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<decimal, (DateTimeOffset At, EffectiveRights Rights)> _items = new();
    private readonly TimeProvider _time;

    public EffectiveRightsCache(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public bool TryGet(decimal userId, out EffectiveRights rights)
    {
        if (_items.TryGetValue(userId, out var entry))
        {
            if (_time.GetUtcNow() - entry.At < Lifetime)
            {
                rights = entry.Rights;
                return true;
            }
            _items.TryRemove(userId, out _);
        }
        rights = null!;
        return false;
    }

    public void Set(decimal userId, EffectiveRights rights) => _items[userId] = (_time.GetUtcNow(), rights);

    public void Invalidate(decimal userId) => _items.TryRemove(userId, out _);

    public void InvalidateAll() => _items.Clear();
}
