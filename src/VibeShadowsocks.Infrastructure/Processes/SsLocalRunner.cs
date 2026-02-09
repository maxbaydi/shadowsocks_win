using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VibeShadowsocks.Core.Abstractions;

namespace VibeShadowsocks.Infrastructure.Processes;

public sealed class SsLocalRunner(ILogger<SsLocalRunner> logger) : ISsLocalRunner
{
    private static readonly Regex PasswordJsonRegex = new("\"password\"\\s*:\\s*\"[^\"]+\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<SsLocalRunner> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private volatile bool _isStopping;
    private string? _configPath;

    public event EventHandler<SsLocalExitedEventArgs>? UnexpectedExit;

    public bool IsRunning => _process is { HasExited: false };

    public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

    public async Task<SsLocalStartResult> StartAsync(SsLocalStartRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("sslocal is already running.");
            }

            if (!File.Exists(request.ExecutablePath))
            {
                throw new FileNotFoundException("sslocal executable not found", request.ExecutablePath);
            }

            Directory.CreateDirectory(request.ConfigDirectory);
            _configPath = Path.Combine(request.ConfigDirectory, $"sslocal.{request.Profile.Id}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json");

            string arguments;
            if (request.UseServerUrlMode)
            {
                arguments = $"--server-url \"{SsLocalConfigBuilder.BuildServerUrl(request.Profile, request.Password)}\" --local-addr 127.0.0.1:{request.SocksPort}";
            }
            else
            {
                var configJson = SsLocalConfigBuilder.BuildConfigJson(request.Profile, request.Password, request.SocksPort);
                await Storage.AtomicFileWriter.WriteTextAsync(_configPath, configJson, cancellationToken: cancellationToken).ConfigureAwait(false);
                arguments = $"-c \"{_configPath}\"";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = request.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            _process.Exited += HandleProcessExited;
            _process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    _logger.LogInformation("sslocal: {Line}", Redact(eventArgs.Data));
                }
            };
            _process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    _logger.LogWarning("sslocal: {Line}", Redact(eventArgs.Data));
                }
            };

            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to launch sslocal process.");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logger.LogInformation("sslocal started. pid={Pid}", _process.Id);
            return new SsLocalStartResult(_process.Id, _configPath, DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is null)
            {
                return;
            }

            _isStopping = true;
            try
            {
                if (!_process.HasExited)
                {
                    try
                    {
                        _process.CloseMainWindow();
                    }
                    catch
                    {
                        // ignored because sslocal is usually console process.
                    }

                    using var gracefulCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    gracefulCts.CancelAfter(gracefulTimeout);

                    try
                    {
                        await _process.WaitForExitAsync(gracefulCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("sslocal graceful stop timeout, killing process tree.");
                    }

                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _isStopping = false;
                _process.Dispose();
                _process = null;
            }

            if (!string.IsNullOrWhiteSpace(_configPath) && File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }

            _logger.LogInformation("sslocal stopped.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(250, cancellationToken)).ConfigureAwait(false);
                if (completed == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch (SocketException)
            {
                // retry
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private void HandleProcessExited(object? sender, EventArgs eventArgs)
    {
        if (_isStopping)
        {
            return;
        }

        var exitCode = _process?.ExitCode ?? -1;
        UnexpectedExit?.Invoke(this, new SsLocalExitedEventArgs(exitCode, "Process exited unexpectedly"));
    }

    private static string Redact(string line)
    {
        var redacted = PasswordJsonRegex.Replace(line, "\"password\":\"***\"");
        return redacted.Replace("ss://", "ss://***");
    }
}
