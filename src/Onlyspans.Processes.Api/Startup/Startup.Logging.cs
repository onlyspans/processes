using Serilog;
using Serilog.Events;

namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Onlyspans.Processes")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/processes-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}");
        });

        return builder;
    }

    public static WebApplication UseSerilogLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (ex is not null || httpContext.Response.StatusCode >= 500)
                    return LogEventLevel.Error;

                if (elapsed > 3000)
                    return LogEventLevel.Warning;

                return LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.FirstOrDefault() ?? string.Empty);
            };
        });

        return app;
    }
}
