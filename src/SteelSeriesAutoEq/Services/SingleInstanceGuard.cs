using System.Threading;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Ensures only one Auto EQ process runs. A second launch signals the first to show its window.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\SteelSeriesAutoEq_SingleInstance";
    public const string ActivateEventName = @"Local\SteelSeriesAutoEq_Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;
    private bool _disposed;

    public event Action? ActivateRequested;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activateEvent)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
    }

    /// <summary>
    /// Returns a guard when this process is primary. Returns null after signaling the existing instance.
    /// </summary>
    public static SingleInstanceGuard? TryAcquireOrSignal()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            SignalExistingInstance();
            return null;
        }

        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        return new SingleInstanceGuard(mutex, activateEvent);
    }

    public void StartListening()
    {
        _listenerTask = Task.Run(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (_activateEvent.WaitOne(500))
                    {
                        ActivateRequested?.Invoke();
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        });
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Primary may be shutting down.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // ignored
        }

        _cts.Dispose();
        _activateEvent.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // ignored
        }

        _mutex.Dispose();
    }
}
