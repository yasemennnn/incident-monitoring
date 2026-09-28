using IncidentMonitoring.Api.Hubs;
using IncidentMonitoring.Api.Middleware;
using IncidentMonitoring.Core;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Rules;
using IncidentMonitoring.Core.Services;
using IncidentMonitoring.Infrastructure;
using IncidentMonitoring.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Web API + SignalR, both using the same JSON format (camelCase, enums as text).
builder.Services.AddControllers().AddJsonOptions(o => JsonSettings.Configure(o.JsonSerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => JsonSettings.Configure(o.PayloadSerializerOptions));

// Errors are returned as ProblemDetails.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Incident Monitoring API",
        Version = "v1",
        Description = "Events are consumed from Kafka, stored in PostgreSQL and summarised in Redis. " +
                      "Live updates: SignalR hub at /hubs/incidents."
    });
    o.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "IncidentMonitoring.Api.xml"));
});

// The Angular dev server. SignalR needs AllowCredentials, which requires an explicit origin.
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Business services (Core) and the SignalR notifier.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection("EventValidation").Get<EventValidationOptions>() ?? new EventValidationOptions());
builder.Services.AddScoped<EventProcessor>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IEventNotifier, SignalREventNotifier>();

// PostgreSQL, Redis, Kafka consumer, health checks.
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Create or update the database schema (EF Core migrations).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapControllers();
app.MapHub<IncidentHub>("/hubs/incidents");
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();
