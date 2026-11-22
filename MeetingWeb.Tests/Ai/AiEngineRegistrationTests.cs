using MeetingWeb.Services.Ai;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingWeb.Tests.Ai;

public class AiEngineRegistrationTests
{
    [Fact]
    public void Application_registers_the_complete_dotnet_pipeline_without_a_groq_key()
    {
        using var app = new RegistrationFactory();
        app.ExtraSettings["GROQ_API_KEY"] = "";
        app.ExtraServices = services =>
        {
            services.RemoveAll<IAiEngine>();
            services.Add(app.Registration!);
        };

        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        Assert.Equal(typeof(DotNetAiEngine), app.Registration!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, app.Registration.Lifetime);
        Assert.IsType<DotNetAiEngine>(provider.GetRequiredService<IAiEngine>());
        Assert.Equal("", provider.GetRequiredService<AiOptions>().GroqApiKey);
        Assert.IsType<TranscriberFactory>(provider.GetRequiredService<ITranscriberFactory>());
        Assert.IsType<FfmpegAudioTools>(provider.GetRequiredService<IAudioTools>());
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("AzureSpeech");
        Assert.Equal(TimeSpan.FromSeconds(900), http.Timeout);
    }

    private sealed class RegistrationFactory : BriefynFactory
    {
        public ServiceDescriptor? Registration { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Capture the production registration before BriefynFactory installs its fake.
            builder.ConfigureTestServices(services =>
                Registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAiEngine)));
            base.ConfigureWebHost(builder);
        }
    }
}
