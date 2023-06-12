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
app.UseWebSockets();

app.MapGet("/servers", () =>
{

    var pingService = app.Services.GetRequiredService<PingService>();
    return pingService.GetServerInfos();
});


app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync(
    new WebSocketAcceptContext { DangerousEnableCompression = true });
            var socketFinishedTcs = new TaskCompletionSource<object>();
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
