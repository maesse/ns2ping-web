using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PingService>();
builder.Services.AddHostedService<PingService>(p => p.GetRequiredService<PingService>());
builder.Services.Configure<JsonOptions>(opt =>
{
    opt.SerializerOptions.Converters.Add(new IPAddressConverter());
});
var app = builder.Build();
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
