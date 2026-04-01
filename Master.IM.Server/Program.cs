using System;
using System.Threading.Tasks;
using MasterIM.Server.Storage;
using MasterIM.Server.WebSocket;
using MasterIM.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<DMConnectionManager>();
builder.Services.AddSingleton(new MessageStore("./data"));
builder.Services.AddSingleton(new ObjectStore("./data"));
builder.Services.AddSingleton(new DMAdvancedStore("./data"));
builder.Services.AddSingleton(new DMCleanupService("./data"));
builder.Services.AddSingleton(new FileService("./data"));
builder.Services.AddSingleton(new RoomMemberStore("./data"));
builder.Services.AddSingleton(new RoomStore("./data"));
builder.Services.AddSingleton<IMServer>();
builder.Services.AddSingleton<DMServer>();
builder.Services.AddSingleton<DMAdvancedServer>();

var app = builder.Build();

app.UseWebSockets();

// 启动定时清理任务
var cleanupService = app.Services.GetRequiredService<DMCleanupService>();
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromHours(24));
        await cleanupService.CleanupExpiredMessagesAsync();
    }
});

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var userId = context.Request.Query["userId"].ToString();
    var roomId = context.Request.Query["roomId"].ToString();
    var channelId = context.Request.Query["channelId"].ToString();

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var server = context.RequestServices.GetRequiredService<IMServer>();
    await server.HandleConnectionAsync(ws, userId, roomId, channelId);
});

app.Map("/dm", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var userId = context.Request.Query["userId"].ToString();
    var targetUserId = context.Request.Query["targetUserId"].ToString();

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var server = context.RequestServices.GetRequiredService<DMServer>();
    await server.HandleConnectionAsync(ws, userId, targetUserId);
});

app.Map("/dm_advanced", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var userId = context.Request.Query["userId"].ToString();
    var targetUserId = context.Request.Query["targetUserId"].ToString();
    var enableStorage = bool.Parse(context.Request.Query["enableStorage"].ToString() ?? "true");
    var retentionDays = int.Parse(context.Request.Query["retentionDays"].ToString() ?? "-1");

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var server = context.RequestServices.GetRequiredService<DMAdvancedServer>();
    await server.HandleConnectionAsync(ws, userId, targetUserId, enableStorage, retentionDays);
});

app.MapPost("/upload", async (HttpContext context) =>
{
    var userId = context.Request.Query["userId"].ToString();
    var roomId = context.Request.Query["roomId"].ToString();

    if (!context.Request.HasFormContentType || context.Request.Form.Files.Count == 0)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var file = context.Request.Form.Files[0];
    var fileService = context.RequestServices.GetRequiredService<FileService>();
    var fileTransfer = await fileService.SaveFileAsync(roomId, file.OpenReadStream(), file.FileName, userId);

    await context.Response.WriteAsJsonAsync(new
    {
        FileId = fileTransfer.FileId,
        FileName = fileTransfer.FileName,
        FileSize = fileTransfer.FileSize,
        FileType = fileTransfer.FileType,
        Url = $"/download?fileId={fileTransfer.FileId}&roomId={roomId}&fileName={fileTransfer.FileName}"
    });
});

app.MapGet("/download", async (HttpContext context) =>
{
    var fileId = context.Request.Query["fileId"].ToString();
    var roomId = context.Request.Query["roomId"].ToString();
    var fileName = context.Request.Query["fileName"].ToString();

    var fileService = context.RequestServices.GetRequiredService<FileService>();
    var filePath = fileService.GetFilePath(roomId, fileId, fileName);

    if (!File.Exists(filePath))
    {
        context.Response.StatusCode = 404;
        return;
    }

    await context.Response.SendFileAsync(filePath);
});

app.Run();
