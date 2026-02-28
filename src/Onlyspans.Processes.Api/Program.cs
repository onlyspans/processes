using Onlyspans.Processes.Api.Startup;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddSerilog();
    builder.Services.AddApplication(builder.Configuration);

    var app = builder.Build();

    app.UseSerilogLogging();
    app.UseApplication();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
