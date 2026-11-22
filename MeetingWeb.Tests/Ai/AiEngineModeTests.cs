using MeetingWeb.Services.Ai;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Tests.Ai;

public class AiEngineModeTests : IAsyncLifetime
{
    private readonly BriefynFactory _app = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Dotnet_mode_fails_at_startup_with_a_clear_validation_error()
    {
        _app.ExtraSettings["AiEngine:Mode"] = "dotnet";

        var error = await Record.ExceptionAsync(() => _app.InitializeAsync());

        Assert.NotNull(error);
        while (error is not OptionsValidationException && error.InnerException is not null)
            error = error.InnerException;
        var validation = Assert.IsType<OptionsValidationException>(error);
        Assert.Contains("AiEngine:Mode", validation.Message);
    }

    [Fact]
    public async Task Default_mode_resolves_the_python_engine()
    {
        await _app.InitializeAsync();

        using var scope = _app.Services.CreateScope();
        Assert.IsType<PythonAiEngine>(scope.ServiceProvider.GetRequiredService<IAiEngine>());
    }
}
