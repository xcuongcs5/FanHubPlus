var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    service = "FanHub API Gateway (YARP)",
    status = "Healthy",
    version = "1.0.0"
}));

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapReverseProxy();

app.Run();
