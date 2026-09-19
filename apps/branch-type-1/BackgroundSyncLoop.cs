using SugarERP.Sync.Client;

namespace SugarERP.Branch1;

internal sealed class BackgroundSyncLoop(BranchSyncService sync, TimeSpan interval) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _running;

    public void Start()
    {
        _running ??= Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await sync.SynchronizeAsync(_stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // An unexpected failure must not stop later retries or interrupt POS work.
                    // Durable outbox entries remain available for the next cycle/manual sync.
                }
            } while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null) await _running;
        _stop.Dispose();
    }
}
