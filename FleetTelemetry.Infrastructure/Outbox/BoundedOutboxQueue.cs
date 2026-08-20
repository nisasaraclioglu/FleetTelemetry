using System.Collections.Concurrent;
using FleetTelemetry.Application.Abstractions;
using FleetTelemetry.Application.Options;
using FleetTelemetry.Domain;
using Microsoft.Extensions.Options;

namespace FleetTelemetry.Infrastructure.Outbox;

public sealed class BoundedOutboxQueue : IOutboxQueue
{
    private readonly ConcurrentDictionary<string, DeviceQueue> _queues =
        new(StringComparer.Ordinal);

    private readonly int _capacity;
    private long _totalEvicted;

    public BoundedOutboxQueue(IOptions<OutboxOptions> options)
    {
        _capacity = options.Value.MaxItemsPerDevice;
    }

    public IReadOnlyCollection<string> DeviceCodes => _queues.Keys.ToArray();

    public long TotalEvicted => Interlocked.Read(ref _totalEvicted);

    public OutboxEnqueueResult Enqueue(string deviceCode, DeviceTelemetry telemetry)
    {
        var queue = _queues.GetOrAdd(deviceCode, static _ => new DeviceQueue());

        if (!queue.Enqueue(telemetry, _capacity))
        {
            return OutboxEnqueueResult.Added;
        }

        Interlocked.Increment(ref _totalEvicted);
        return OutboxEnqueueResult.AddedWithEviction;
    }

    public bool TryDequeue(string deviceCode, out DeviceTelemetry telemetry)
    {
        if (_queues.TryGetValue(deviceCode, out var queue))
        {
            return queue.TryDequeue(out telemetry);
        }

        telemetry = default!;
        return false;
    }

    public void Requeue(string deviceCode, DeviceTelemetry telemetry)
    {
        var queue = _queues.GetOrAdd(deviceCode, static _ => new DeviceQueue());
        queue.Requeue(telemetry, _capacity);
    }

    public void Load(string deviceCode, IEnumerable<DeviceTelemetry> items)
    {
        var queue = _queues.GetOrAdd(deviceCode, static _ => new DeviceQueue());
        queue.Load(items, _capacity);
    }

    public IReadOnlyList<DeviceTelemetry> Snapshot(string deviceCode) =>
        _queues.TryGetValue(deviceCode, out var queue)
            ? queue.Snapshot()
            : [];

    public int Count(string deviceCode) =>
        _queues.TryGetValue(deviceCode, out var queue) ? queue.Count : 0;

    private sealed class DeviceQueue
    {
        private readonly LinkedList<DeviceTelemetry> _items = new();
        private readonly object _gate = new();

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count;
                }
            }
        }

        public bool Enqueue(DeviceTelemetry telemetry, int capacity)
        {
            lock (_gate)
            {
                var evicted = false;

                while (_items.Count >= capacity)
                {
                    _items.RemoveFirst();
                    evicted = true;
                }

                _items.AddLast(telemetry);
                return evicted;
            }
        }

        public bool TryDequeue(out DeviceTelemetry telemetry)
        {
            lock (_gate)
            {
                if (_items.First is null)
                {
                    telemetry = default!;
                    return false;
                }

                telemetry = _items.First.Value;
                _items.RemoveFirst();
                return true;
            }
        }

        public void Requeue(DeviceTelemetry telemetry, int capacity)
        {
            lock (_gate)
            {
                if (_items.Count >= capacity)
                {
                    return;
                }

                _items.AddFirst(telemetry);
            }
        }

        public void Load(IEnumerable<DeviceTelemetry> items, int capacity)
        {
            lock (_gate)
            {
                _items.Clear();

                foreach (var item in items.OrderBy(x => x.DataDate).TakeLast(capacity))
                {
                    _items.AddLast(item);
                }
            }
        }

        public IReadOnlyList<DeviceTelemetry> Snapshot()
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }
}