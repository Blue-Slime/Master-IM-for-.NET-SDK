using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.SDK;

public class DMAdvancedClient : IDisposable
{
    private ClientWebSocket? _ws;
    private readonly Dictionary<string, TaskCompletionSource<object>> _pending = new();
    private CancellationTokenSource? _cts;
    private string? _userId;
    private string? _targetUserId;
    private string? _url;
    private bool _enableStorage;
    private int _retentionDays;

    public event Action<GroupMessage>? OnMessageReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<int>? OnReconnecting;
    public event Action? OnReconnected;
    public event Action<string>? OnConnectionError;

    public async Task ConnectAsync(string url, string userId, string targetUserId, bool enableStorage = true, int retentionDays = -1)
    {
        _url = url;
        _userId = userId;
        _targetUserId = targetUserId;
        _enableStorage = enableStorage;
        _retentionDays = retentionDays;

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

            var uri = new Uri($"{_url}?userId={_userId}&targetUserId={_targetUserId}&enableStorage={_enableStorage}&retentionDays={_retentionDays}");
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

    public async Task SendMessageAsync(GroupMessage msg)
    {
        await SendAsync(new Packet { T = "msg", P = msg });
    }

    public async Task<List<GroupMessage>> QueryPageAsync(int lastPage, int lastSeq, int limit = 100)
    {
        var result = await RequestAsync<List<GroupMessage>>(new Packet
        {
            T = "qry",
            P = new { LastPage = lastPage, LastSeq = lastSeq, Limit = limit }
        });
        return result ?? new();
    }

    public async Task<long> CheckPageModifiedAsync(int pageNumber)
    {
        var result = await RequestAsync<Dictionary<string, object>>(new Packet
        {
            T = "chk",
            P = new { PageNumber = pageNumber }
        });

        if (result != null && result.TryGetValue("LastModified", out var time))
        {
            return Convert.ToInt64(time);
        }
        return 0;
    }

    public async Task ModifyMessageAsync(int page, int seq, string newContent)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "modify", Page = page, Seq = seq, Content = newContent } });
    }

    private async Task SendAsync(Packet packet)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(packet);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? default);
    }

    private async Task<T?> RequestAsync<T>(Packet packet)
    {
        packet.Id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<object>();
        _pending[packet.Id] = tcs;

        await SendAsync(packet);
        var result = await tcs.Task;
        return JsonSerializer.Deserialize<T>(result.ToString() ?? "");
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

        if (!string.IsNullOrEmpty(packet.Id) && _pending.TryGetValue(packet.Id, out var tcs))
        {
            tcs.SetResult(packet.P ?? new());
            _pending.Remove(packet.Id);
            return;
        }

        if (packet.T == "msg")
        {
            var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
            if (msg != null) OnMessageReceived?.Invoke(msg);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }
}


