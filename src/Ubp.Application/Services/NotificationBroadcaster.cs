using System.Collections.Concurrent;
using Ubp.Core.Models;

namespace Ubp.Application.Services;

/// <summary>
/// بثّ الإشعارات داخل العملية (Singleton): كل دائرة Blazor تشترك لمستخدم واحد،
/// والاشتراك يُرجع <see cref="IDisposable"/> يجب أن ينفّذه صاحب الاشتراك عند التخلّص.
/// </summary>
public sealed class NotificationBroadcaster
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Func<Notification, Task>>>
        _subscribers = new();

    public int SubscriberCount =>
        _subscribers.Values.Sum(handlers => handlers.Count);

    public IDisposable Subscribe(Guid userId, Func<Notification, Task> handler)
    {
        var token = Guid.NewGuid();
        var handlers = _subscribers.GetOrAdd(userId,
            _ => new ConcurrentDictionary<Guid, Func<Notification, Task>>());
        handlers[token] = handler;
        return new Subscription(() =>
        {
            if (_subscribers.TryGetValue(userId, out var existing)) existing.TryRemove(token, out _);
            if (existing is not null && existing.IsEmpty) _subscribers.TryRemove(userId, out _);
        });
    }

    public async Task PublishAsync(Guid userId, Notification notification)
    {
        if (!_subscribers.TryGetValue(userId, out var handlers)) return;
        foreach (var handler in handlers.Values.ToArray())
        {
            try { await handler(notification); }
            catch { /* دائرة مقطوعة: لا نُسقط بقية المشتركين */ }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose();
        }
    }
}
