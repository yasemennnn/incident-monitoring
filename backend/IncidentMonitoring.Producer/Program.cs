using IncidentMonitoring.Producer;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ProducerSettings>(builder.Configuration.GetSection("Producer"));
builder.Services.AddHostedService<ProducerWorker>();

builder.Build().Run();
