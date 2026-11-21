using Serilog;
using Serilog.Events;

namespace MeetingWeb.Services;

public static class ProductionLogging
{
    public static LoggerConfiguration ApplyLevels(this LoggerConfiguration config) =>
        config.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);
}
