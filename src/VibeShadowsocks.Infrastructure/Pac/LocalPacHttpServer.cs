using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VibeShadowsocks.Infrastructure.Pac;

public sealed class LocalPacHttpServer(ILogger<LocalPacHttpServer> logger) : IAsyncDisposable
{
    private readonly ILogger<LocalPacHttpServer> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private string _pacScript = "function FindProxyForURL(url, host) { return 'DIRECT'; }";
    private int _port;
    private long _version;

    public bool IsRunning => _listener is { IsListening: true };

    public Uri CurrentUri => new($"http://127.0.0.1:{_port}/proxy.pac?v={_version}");

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning && _port == port)
            {
                return;
            }

            await StopInternalAsync().ConfigureAwait(false);

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _port = port;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));

            _logger.LogInformation("Local PAC server started on 127.0.0.1:{Port}", port);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdatePacScriptAsync(string pacScript, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pacScript = pacScript;
            Interlocked.Increment(ref _version);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task StopInternalAsync()
    {
        if (_listener is null)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
            _listener.Stop();
            _listener.Close();

            if (_loopTask is not null)
            {
                await _loopTask.ConfigureAwait(false);
            }
        }
        catch (HttpListenerException)
        {
            // listener already closed.
        }
        catch (ObjectDisposedException)
        {
            // listener already disposed.
        }
        finally
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
            _port = 0;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (!path.Equals("/proxy.pac", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            var payload = Encoding.UTF8.GetBytes(_pacScript);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/x-ns-proxy-autoconfig";
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "PAC server request handling failed.");
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch
            {
                // ignored
            }
        }
    }
}
