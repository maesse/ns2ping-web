using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PingService>();
builder.Services.AddHostedService<PingService>(p => p.GetRequiredService<PingService>());
builder.Services.Configure<JsonOptions>(opt =>
{
    opt.SerializerOptions.Converters.Add(new IPAddressConverter());
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions()
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers["Permissions-Policy"] = "autoplay";
        context.Context.Response.Headers["Feature-Policy"] = "autoplay";
    }
});

app.MapGet("/servers", () =>
{

    var pingService = app.Services.GetRequiredService<PingService>();
    return pingService.GetServerInfos();
});

app.Run();
