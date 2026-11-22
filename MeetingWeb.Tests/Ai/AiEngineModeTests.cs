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

    [Theory]
    [InlineData("dotnet")]
    [InlineData(" DoTnEt ")]
    public async Task Dotnet_mode_resolves_without_a_groq_key(string mode)
    {
        _app.ExtraSettings["AiEngine:Mode"] = mode;
        _app.ExtraSettings["GROQ_API_KEY"] = "";

        await _app.InitializeAsync();

        using var scope = _app.Services.CreateScope();
        Assert.IsType<DotNetAiEngine>(scope.ServiceProvider.GetRequiredService<IAiEngine>());
        Assert.Equal("", scope.ServiceProvider.GetRequiredService<AiOptions>().GroqApiKey);
    }

    [Fact]
    public async Task Unknown_mode_fails_at_startup_with_a_clear_validation_error()
    {
        _app.ExtraSettings["AiEngine:Mode"] = "rust";

        var error = await Record.ExceptionAsync(() => _app.InitializeAsync());

        Assert.NotNull(error);
        while (error is not OptionsValidationException && error.InnerException is not null)
            error = error.InnerException;
        var validation = Assert.IsType<OptionsValidationException>(error);
        Assert.Contains("AiEngine:Mode", validation.Message);
        Assert.Contains("AiEngine:Mode must be 'python' or 'dotnet'.", validation.Message);
    }

    [Fact]
    public async Task Default_mode_resolves_the_python_engine()
    {
        await _app.InitializeAsync();

        using var scope = _app.Services.CreateScope();
        Assert.IsType<PythonAiEngine>(scope.ServiceProvider.GetRequiredService<IAiEngine>());
    }

    [Fact]
    public async Task Dotnet_mode_registers_the_complete_pipeline()
    {
        _app.ExtraSettings["AiEngine:Mode"] = "dotnet";
        _app.ExtraSettings["GROQ_API_KEY"] = "";
        await _app.InitializeAsync();

        using var scope = _app.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.IsType<DotNetAiEngine>(services.GetRequiredService<IAiEngine>());
        Assert.IsType<TranscriberFactory>(services.GetRequiredService<ITranscriberFactory>());
        Assert.IsType<FfmpegAudioTools>(services.GetRequiredService<IAudioTools>());
        using var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("AzureSpeech");
        Assert.Equal(TimeSpan.FromSeconds(900), http.Timeout);
    }
}
