using NS2Ping;
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
    options.MimeTypes =
    ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json" });
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
        context.Context.Response.Headers.Add("cache-control", new[] { "public,max-age=3600" });
        context.Context.Response.Headers.Add("Expires", new[] { DateTime.UtcNow.AddHours(1).ToString("R") }); // Format RFC1123
    }
});
app.UseWebSockets();

app.MapGet("/servers", () =>
{
    var pingService = app.Services.GetRequiredService<PingService>();
    return pingService.GetServerInfos();
});



app.MapGet("/server/{id}", (int id) =>
{
    var pingService = app.Services.GetRequiredService<PingService>();
    var info = new ServerInfo()
    {
        playerInfo = pingService.GetPlayerInfo(id),
        rules = pingService.GetServerRules(id)?.Rules
    };
    return info;
});


app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync(
    new WebSocketAcceptContext { DangerousEnableCompression = true });
            var socketFinishedTcs = new TaskCompletionSource();
            var pingService = app.Services.GetRequiredService<PingService>();
            var ws = pingService.AddSocket(webSocket, socketFinishedTcs);
            ws.Read();
            await socketFinishedTcs.Task;
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
    else
    {
        await next(context);
    }

});

app.Run();
