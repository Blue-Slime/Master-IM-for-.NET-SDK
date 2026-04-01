using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.SDK;

public class DMClient : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string? _userId;
    private string? _targetUserId;
    private string? _url;

    public event Action<GroupMessage>? OnMessageReceived;
    public event Action? OnTargetOnline;
    public event Action? OnTargetOffline;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<int>? OnReconnecting;
    public event Action? OnReconnected;
    public event Action<string>? OnConnectionError;

    public async Task ConnectAsync(string url, string userId, string targetUserId)
    {
        _url = url;
        _userId = userId;
        _targetUserId = targetUserId;

        await ReconnectAsync();
        _ = AutoReconnect();
    }

    private async Task ReconnectAsync()
    {
        try
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            var uri = new Uri($"{_url}?userId={_userId}&targetUserId={_targetUserId}");
            await _ws.ConnectAsync(uri, _cts.Token);

            OnConnected?.Invoke();
            _ = ReceiveLoop();
        }
        catch (Exception ex)
        {
            OnConnectionError?.Invoke(ex.Message);
            throw;
        }
    }

    private async Task AutoReconnect()
    {
        int retryCount = 0;
        while (_cts?.IsCancellationRequested == false)
        {
            await Task.Delay(5000);
            if (_ws?.State != System.Net.WebSockets.WebSocketState.Open)
            {
                OnDisconnected?.Invoke();
                retryCount++;
                OnReconnecting?.Invoke(retryCount);
                try
                {
                    await ReconnectAsync();
                    OnReconnected?.Invoke();
                    retryCount = 0;
                }
                catch { }
            }
        }
    }

    public async Task SendTextAsync(string content)
    {
        await SendAsync(new Packet
        {
            T = "dm_msg",
            P = new { Type = "text", Content = content }
        });
    }

    public async Task SendImageAsync(string url, int width, int height)
    {
        await SendAsync(new Packet
        {
            T = "dm_msg",
            P = new { Type = "image", Url = url, Width = width, Height = height }
        });
    }

    public async Task SendFileAsync(string fileName, long fileSize, string url)
    {
        await SendAsync(new Packet
        {
            T = "dm_msg",
            P = new { Type = "file", FileName = fileName, FileSize = fileSize, Url = url }
        });
    }

    public async Task SendCustomAsync(object data)
    {
        await SendAsync(new Packet
        {
            T = "dm_msg",
            P = new { Type = "custom", Data = data }
        });
    }

    private async Task SendAsync(Packet packet)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(packet);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? default);
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[8192];
        while (_ws?.State == WebSocketState.Open)
        {
            var result = await _ws.ReceiveAsync(buffer, _cts?.Token ?? default);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            HandlePacket(json);
        }

        OnDisconnected?.Invoke();
    }

    private void HandlePacket(string json)
    {
        var packet = JsonSerializer.Deserialize<Packet>(json);
        if (packet == null) return;

        switch (packet.T)
        {
            case "dm_msg":
                var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
                if (msg != null) OnMessageReceived?.Invoke(msg);
                break;
            case "dm_online":
                OnTargetOnline?.Invoke();
                break;
            case "dm_offline":
                OnTargetOffline?.Invoke();
                break;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }
}
