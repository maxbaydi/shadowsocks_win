using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using VibeShadowsocks.Core.Abstractions;
using VibeShadowsocks.Core.Models;
using VibeShadowsocks.Core.Orchestration;
using VibeShadowsocks.Core.Pac;
using VibeShadowsocks.Core.Validation;

namespace VibeShadowsocks.Tests.Core;

public sealed class ConnectionOrchestratorTests
{
    [Fact]
    public async Task Connect_Then_Disconnect_TransitionsAndRollsBackProxy()
    {
        var settings = BuildSettings(routingMode: RoutingMode.Global);
        var settingsStore = new FakeSettingsStore(settings);
        var secureStorage = new FakeSecureStorage();
        await secureStorage.SaveSecretAsync(settings.ServerProfiles[0].PasswordSecretId, "password123");

        var runner = new FakeSsLocalRunner();
        var proxyManager = new FakeSystemProxyManager();
        var pacManager = new FakePacManager();
        var orchestrator = new ConnectionOrchestrator(
            NullLogger<ConnectionOrchestrator>.Instance,
            settingsStore,
            new SettingsValidator(),
            secureStorage,
            runner,
            proxyManager,
            pacManager,
            new FakePortProbe(),
            new FakeSsLocalProvisioner());

        var connect = await orchestrator.ConnectAsync();

        Assert.True(connect.Success);
        Assert.Equal(ConnectionState.Connected, orchestrator.Snapshot.State);
        Assert.Equal(1, runner.StartCount);
        Assert.Equal(1, proxyManager.BeginSessionCount);
        Assert.Equal(1, proxyManager.ApplyCount);

        var disconnect = await orchestrator.DisconnectAsync();

        Assert.True(disconnect.Success);
        Assert.Equal(ConnectionState.Disconnected, orchestrator.Snapshot.State);
        Assert.Equal(1, runner.StopCount);
        Assert.Equal(1, proxyManager.RollbackCount);

        orchestrator.Dispose();
    }

    [Fact]
    public async Task Coalesced_UpdatePac_SecondCommandBecomesNoOp()
    {
        var settings = BuildSettings(routingMode: RoutingMode.Pac);
        var settingsStore = new FakeSettingsStore(settings);
        var secureStorage = new FakeSecureStorage();
        await secureStorage.SaveSecretAsync(settings.ServerProfiles[0].PasswordSecretId, "password123");

        var runner = new FakeSsLocalRunner();
        var proxyManager = new FakeSystemProxyManager();
        var pacManager = new FakePacManager(delayUpdate: TimeSpan.FromMilliseconds(300));

        var orchestrator = new ConnectionOrchestrator(
            NullLogger<ConnectionOrchestrator>.Instance,
            settingsStore,
            new SettingsValidator(),
            secureStorage,
            runner,
            proxyManager,
            pacManager,
            new FakePortProbe(),
            new FakeSsLocalProvisioner());

        await orchestrator.ConnectAsync();

        var first = orchestrator.UpdatePacAsync();
        var second = await orchestrator.UpdatePacAsync();
        var firstResult = await first;

        Assert.True(firstResult.Success);
        Assert.True(second.IsNoOp);

        orchestrator.Dispose();
    }

    [Fact]
    public void PacParser_And_Evaluator_WorkForSimpleRules()
    {
        const string rules = "||example.com\n@@||intranet.local\nPROXY *.blocked.net\nDIRECT /.*\\.corp$/";
        var parsed = PacRulesParser.Parse(rules);

        var direct = PacRuleEvaluator.Evaluate(parsed, "http://intranet.local", 1080, PacDefaultAction.Proxy);
        var proxy = PacRuleEvaluator.Evaluate(parsed, "https://api.example.com", 1080, PacDefaultAction.Proxy);
        var globProxy = PacRuleEvaluator.Evaluate(parsed, "https://foo.blocked.net", 1080, PacDefaultAction.Direct);

        Assert.Equal("DIRECT", direct.Decision);
        Assert.Contains("SOCKS5", proxy.Decision);
        Assert.Contains("SOCKS5", globProxy.Decision);
    }

    private static AppSettings BuildSettings(RoutingMode routingMode)
    {
        var profile = new ServerProfile
        {
            Name = "Test",
            Host = "127.0.0.1",
            Port = 8388,
            Method = "aes-256-gcm",
            PasswordSecretId = "secret-1",
        };

        var pacProfile = new PacProfile
        {
            Name = "Managed",
            Type = PacProfileType.Managed,
            InlineRules = "||example.com",
        };

        return new AppSettings
        {
            SsLocalExecutablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            ServerProfiles = [profile],
            ActiveServerProfileId = profile.Id,
            PacProfiles = [pacProfile],
            ActivePacProfileId = pacProfile.Id,
            RoutingMode = routingMode,
        };
    }

    private sealed class FakeSettingsStore(AppSettings initial) : ISettingsStore
    {
        private AppSettings _settings = initial;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
        {
            _settings = update(_settings);
            return Task.FromResult(_settings);
        }
    }

    private sealed class FakeSecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public Task SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
        {
            _secrets[secretId] = secretValue;
            return Task.CompletedTask;
        }

        public Task<string?> ReadSecretAsync(string secretId, CancellationToken cancellationToken = default)
            => Task.FromResult(_secrets.TryGetValue(secretId, out var value) ? value : null);

        public Task DeleteSecretAsync(string secretId, CancellationToken cancellationToken = default)
        {
            _secrets.Remove(secretId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSsLocalRunner : ISsLocalRunner
    {
        public event EventHandler<SsLocalExitedEventArgs>? UnexpectedExit;

        public bool IsRunning { get; private set; }

        public int? ProcessId => IsRunning ? 12345 : null;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task<SsLocalStartResult> StartAsync(SsLocalStartRequest request, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            StartCount++;
            return Task.FromResult(new SsLocalStartResult(12345, "config.json", DateTimeOffset.UtcNow));
        }

        public Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<bool> WaitForLocalPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void EmitUnexpectedExit() => UnexpectedExit?.Invoke(this, new SsLocalExitedEventArgs(1, "boom"));
    }

    private sealed class FakeSystemProxyManager : ISystemProxyManager
    {
        public int BeginSessionCount { get; private set; }

        public int ApplyCount { get; private set; }

        public int RollbackCount { get; private set; }

        public Task CrashRecoverIfNeededAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BeginSessionAsync(CancellationToken cancellationToken = default)
        {
            BeginSessionCount++;
            return Task.CompletedTask;
        }

        public Task ApplyRoutingModeAsync(RoutingMode routingMode, int socksPort, int httpPort, Uri? pacUri, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePacManager(TimeSpan? delayUpdate = null) : IPacManager
    {
        private readonly TimeSpan _delayUpdate = delayUpdate ?? TimeSpan.Zero;

        public Task StartAsync(int port, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<PacUpdateResult> UpdateManagedPacAsync(PacProfile profile, int socksPort, CancellationToken cancellationToken = default)
        {
            if (_delayUpdate > TimeSpan.Zero)
            {
                await Task.Delay(_delayUpdate, cancellationToken);
            }

            return new PacUpdateResult(true, "updated", PacRulesParser.Parse("||example.com"));
        }

        public Task<Uri> ResolvePacUriAsync(PacProfile profile, int socksPort, int pacPort, CancellationToken cancellationToken = default)
            => Task.FromResult(new Uri($"http://127.0.0.1:{pacPort}/proxy.pac"));

        public string GetPacPreview() => "function FindProxyForURL(url, host) { return 'DIRECT'; }";

        public PacEvaluationResult TestRule(PacProfile profile, string urlOrHost, int socksPort)
            => new(urlOrHost, "example.com", "DIRECT", "stub");

        public Task<IReadOnlyList<PacPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PacPreset>>([]);
    }

    private sealed class FakePortProbe : IPortAvailabilityProbe
    {
        public Task<bool> IsPortAvailableAsync(int port, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<int> FindAvailablePortAsync(int preferredPort, CancellationToken cancellationToken = default)
            => Task.FromResult(preferredPort + 1);
    }

    private sealed class FakeSsLocalProvisioner : ISsLocalProvisioner
    {
        public Task<string> EnsureAvailableAsync(string? configuredPath, CancellationToken ct = default)
            => Task.FromResult(configuredPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
    }
}


