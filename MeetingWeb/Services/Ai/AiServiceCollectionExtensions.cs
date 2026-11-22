namespace MeetingWeb.Services.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddGroqAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(AiOptions.FromConfiguration(configuration));
        services.AddHttpClient("Groq", c =>
        {
            c.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
            c.Timeout = TimeSpan.FromSeconds(300);
        });
        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        return services;
    }
}
