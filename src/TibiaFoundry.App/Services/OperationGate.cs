using CommunityToolkit.Mvvm.ComponentModel;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Limits how many long operations run at the same time (Settings → Performance, and for scripts
/// Settings → Scripts). Further operations wait in order until a slot is free; waiting can be
/// cancelled. The limit is read each time, so a changed setting applies to the next operation.
/// </summary>
public sealed partial class OperationGate : ObservableObject
{
    private readonly Func<int> _limit;
    private readonly object _sync = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _queue = new();

    public OperationGate(Func<int> limit)
    {
        _limit = limit;
    }

    [ObservableProperty] private int _running;
    [ObservableProperty] private int _waiting;

    public int Limit => Math.Max(1, _limit());

    /// <summary>True when an operation started now would have to wait.</summary>
    public bool IsFull
    {
        get
        {
            lock (_sync) return Running >= Limit;
        }
    }

    /// <summary>Waits for a free slot. Dispose the result when the operation ends.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        TaskCompletionSource<bool> ticket;
        LinkedListNode<TaskCompletionSource<bool>> node;
        lock (_sync)
        {
            if (Running < Limit && _queue.Count == 0)
            {
                Running++;
                return new Slot(this);
            }
            ticket = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            node = _queue.AddLast(ticket);
            Waiting = _queue.Count;
        }
        using (ct.Register(() =>
        {
            lock (_sync)
            {
                if (node.List == null) return;
                _queue.Remove(node);
                Waiting = _queue.Count;
            }
            ticket.TrySetCanceled(ct);
        }))
        {
            await ticket.Task.ConfigureAwait(true);
        }
        return new Slot(this);
    }

    private void Release()
    {
        lock (_sync)
        {
            Running--;
            // Hand free slots to waiting operations (the limit may have been raised meanwhile).
            while (_queue.Count > 0 && Running < Limit)
            {
                var next = _queue.First!.Value;
                _queue.RemoveFirst();
                Running++;
                next.TrySetResult(true);
            }
            Waiting = _queue.Count;
        }
    }

    private sealed class Slot(OperationGate owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release();
        }
    }
}
