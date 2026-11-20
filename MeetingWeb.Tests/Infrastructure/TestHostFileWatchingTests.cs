using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace MeetingWeb.Tests.Infrastructure;

// Linux limits how many file watchers (inotify instances) one user may open; every test host watched its settings files
// and web root, and a full run kept 140+ open at once, so hosts started failing at random on CI (2026-11-20). Test hosts
// must not use inotify at all: settings are not reloaded and the file providers poll instead.
public class TestHostFileWatchingTests
{
    [Fact]
    public async Task Test_hosts_open_no_file_watchers()
    {
        using var app = new BriefynFactory();
        await app.InitializeAsync();

        var config = (IConfigurationRoot)app.Services.GetRequiredService<IConfiguration>();
        var jsonSources = config.Providers.OfType<JsonConfigurationProvider>().Select(p => p.Source).ToList();
        Assert.NotEmpty(jsonSources);
        Assert.All(jsonSources, source => Assert.False(source.ReloadOnChange));

        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();
        foreach (var provider in new[] { environment.ContentRootFileProvider, environment.WebRootFileProvider })
            Assert.True(Assert.IsType<PhysicalFileProvider>(provider).UsePollingFileWatcher);
    }
}
