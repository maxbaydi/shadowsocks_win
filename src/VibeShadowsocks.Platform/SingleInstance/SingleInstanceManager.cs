using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VibeShadowsocks.Platform.SingleInstance;

public sealed class SingleInstanceManager(ILogger<SingleInstanceManager> logger) : ISingleInstanceManager
{
    private readonly ILogger<SingleInstanceManager> _logger = logger;
    private Mutex? _mutex;
    private string _pipeName = string.Empty;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public event EventHandler? ActivationRequested;

    public bool IsPrimaryInstance { get; private set; }

    public async Task<bool> InitializeAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        _pipeName = $"vibeshadowsocks-{instanceId}-activation";
        _mutex = new Mutex(initiallyOwned: true, $"Global\\vibeshadowsocks-{instanceId}", out var createdNew);

        if (createdNew)
        {
            IsPrimaryInstance = true;
            _listenerCts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenForActivationAsync(_listenerCts.Token), _listenerCts.Token);
            return true;
        }

        IsPrimaryInstance = false;
        await SignalPrimaryAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    public void Dispose()
    {
        try
        {
            _listenerCts?.Cancel();
            _listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _listenerCts?.Dispose();

        if (IsPrimaryInstance)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
    }

    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, "ACTIVATE", StringComparison.OrdinalIgnoreCase))
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Activation pipe loop handled an exception.");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SignalPrimaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync("ACTIVATE").ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not signal primary instance.");
        }
    }
}
