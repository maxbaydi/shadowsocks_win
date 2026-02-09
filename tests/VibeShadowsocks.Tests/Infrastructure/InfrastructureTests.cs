using VibeShadowsocks.Core.Pac;
using Xunit;
using VibeShadowsocks.Infrastructure.Storage;

namespace VibeShadowsocks.Tests.Infrastructure;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task AtomicFileWriter_ReplacesExistingFileAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vibeshadowsocks-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var filePath = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(filePath, "old-content");

            await AtomicFileWriter.WriteTextAsync(filePath, "new-content");
            var content = await File.ReadAllTextAsync(filePath);

            Assert.Equal("new-content", content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PacRulesParser_ParsesDomainAndDirectRules()
    {
        var rules = "||example.com\n@@||local.example\nPROXY *.blocked.net\nDIRECT /.*\\.corp$/";
        var parsed = PacRulesParser.Parse(rules);

        Assert.Contains("example.com", parsed.ProxyDomains);
        Assert.Contains("local.example", parsed.DirectDomains);
        Assert.Contains("*.blocked.net", parsed.ProxyGlobs);
        Assert.Contains(@".*\.corp$", parsed.DirectRegex);
    }
}


