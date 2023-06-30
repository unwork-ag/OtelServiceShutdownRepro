using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

const string serviceName = "OtelRepro";

var appDir = new FileInfo(typeof(Program).Assembly.Location).Directory.FullName;
var logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(), Path.Combine(appDir, "log.txt"), rollingInterval: RollingInterval.Day)
    .Enrich.WithProcessId()
    .Enrich.WithProcessName()
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);
builder.Host
    .UseWindowsService(options => options.ServiceName = serviceName)
    .UseSerilog(logger);

// Add services to the container.
builder.Services.AddSingleton<Serilog.ILogger>(logger);

builder.Services.AddOpenTelemetry()
    .WithMetrics(mb =>
    {
        mb
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    })
    .WithTracing(tb =>
    {
        tb
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    });

builder.Services.AddWindowsService(options => options.ServiceName = serviceName);

var app = builder.Build();

logger.Information("---- program started!");
var monitor = new OtelServiceShutdownRepro.Monitor(serviceName, logger);

try
{
    app.Run();
}
catch (Exception ex)
{
    logger.Fatal(ex, "Application start-up failed");
}

monitor.Dispose();
logger.Information("---- program stopped!");
logger.Dispose();