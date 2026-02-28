namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddSwaggerDocs(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title       = "Onlyspans Processes API",
                Version     = "v1",
                Description = "Pipeline orchestration and process management",
            });
        });

        return services;
    }

    public static WebApplication UseSwaggerDocs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Processes API v1"));
        }

        return app;
    }
}
