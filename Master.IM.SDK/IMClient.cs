using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.SDK.Models;
using MasterIM.SDK.Protocol;

namespace MasterIM.SDK;

public class IMClient : IDisposable
{
    private ClientWebSocket? _ws;
    private readonly Dictionary<string, TaskCompletionSource<object>> _pending = new();
    private CancellationTokenSource? _cts;
    private string? _url;
    private string? _userId;
    private string? _roomId;
    private string? _channelId;

    public event Action<Message>? OnMessageReceived;
    public event Action<UpdateNotification>? OnUpdateNotification;
    public event Action<StreamData>? OnStreamReceived;
    public event Action<ObjectSyncData>? OnObjectSync;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public async Task ConnectAsync(string url, string userId, string roomId, string channelId)
    {
        _url = url;
        _userId = userId;
        _roomId = roomId;
        _channelId = channelId;

        await ReconnectAsync();
        _ = AutoReconnect();
    }

    private async Task ReconnectAsync()
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        var uri = new Uri($"{_url}?userId={_userId}&roomId={_roomId}&channelId={_channelId}");
        await _ws.ConnectAsync(uri, _cts.Token);

        OnConnected?.Invoke();

        _ = ReceiveLoop();
        _ = Heartbeat();
    }

    private async Task AutoReconnect()
    {
        while (_cts?.IsCancellationRequested == false)
        {
            await Task.Delay(5000);
            if (_ws?.State != WebSocketState.Open)
            {
                OnDisconnected?.Invoke();
                try { await ReconnectAsync(); }
                catch { }
            }
        }
    }

    public async Task SendMessageAsync(Message msg)
    {
        await SendAsync(new Packet { T = "msg", P = msg });
    }

    public async Task<List<Message>> QueryPageAsync(int lastPage, int lastSeq, int limit = 100)
    {
        var result = await RequestAsync<List<Message>>(new Packet
        {
            T = "qry",
            P = new { LastPage = lastPage, LastSeq = lastSeq, Limit = limit }
        });
        return result ?? new();
    }

    /// <summary>
    /// 检查分页修改时间
    /// </summary>
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

    /// <summary>
    /// 创建空白分页
    /// </summary>
    public async Task CreateEmptyPageAsync(int pageNumber)
    {
        await SendAsync(new Packet { T = "crt", P = new { PageNumber = pageNumber } });
    }

    /// <summary>
    /// 删除空白分页
    /// </summary>
    public async Task DeleteEmptyPageAsync(int pageNumber)
    {
        await SendAsync(new Packet { T = "del", P = new { PageNumber = pageNumber } });
    }

    /// <summary>
    /// 批量平移消息
    /// </summary>
    public async Task BatchMoveMessagesAsync(List<(int page, int seq)> messages, int targetPage, int targetSeq)
    {
        var msgList = messages.Select(m => new { Page = m.page, Seq = m.seq }).ToList();
        await SendAsync(new Packet
        {
            T = "bmv",
            P = new { Messages = msgList, TargetPage = targetPage, TargetSeq = targetSeq }
        });
    }

    /// <summary>
    /// 批量删除消息
    /// </summary>
    public async Task BatchDeleteMessagesAsync(List<(int page, int seq)> messages)
    {
        var msgList = messages.Select(m => new { Page = m.page, Seq = m.seq }).ToList();
        await SendAsync(new Packet { T = "bdl", P = new { Messages = msgList } });
    }

    public async Task InsertHistoryMessageAsync(Message msg, DateTime historicalTime)
    {
        msg.SendTime = historicalTime;
        await SendAsync(new Packet { T = "msg", P = new { Type = "insert", Message = msg } });
    }

    public async Task ModifyMessageAsync(int page, int seq, string newContent)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "modify", Page = page, Seq = seq, Content = newContent } });
    }

    public async Task SendStreamAsync(StreamData data)
    {
        await SendAsync(new Packet { T = "stm", P = data });
    }

    public async Task<string> CreateObjectAsync(GameObject obj)
    {
        await SendAsync(new Packet { T = "obj_create", P = obj });
        return obj.Id;
    }

    public async Task UpdateObjectAsync(GameObject obj)
    {
        await SendAsync(new Packet { T = "obj_update", P = obj });
    }

    public async Task DeleteObjectAsync(string objectId)
    {
        await SendAsync(new Packet { T = "obj_delete", P = new { ObjectId = objectId } });
    }

    public async Task<List<GameObject>> QueryObjectsByTypeAsync(string type)
    {
        var result = await RequestAsync<List<GameObject>>(new Packet
        {
            T = "obj_query",
            P = new { Type = type }
        });
        return result ?? new();
    }

    public async Task<List<GameObject>> QueryObjectsBySequenceAsync(long startSeq, long endSeq)
    {
        var result = await RequestAsync<List<GameObject>>(new Packet
        {
            T = "obj_query",
            P = new { StartSeq = startSeq, EndSeq = endSeq }
        });
        return result ?? new();
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

        switch (packet.T)
        {
            case "msg":
                var msg = JsonSerializer.Deserialize<Message>(packet.P?.ToString() ?? "");
                if (msg != null) OnMessageReceived?.Invoke(msg);
                break;
            case "ntf":
                var ntf = JsonSerializer.Deserialize<UpdateNotification>(packet.P?.ToString() ?? "");
                if (ntf != null) OnUpdateNotification?.Invoke(ntf);
                break;
            case "stm":
                var stm = JsonSerializer.Deserialize<StreamData>(packet.P?.ToString() ?? "");
                if (stm != null) OnStreamReceived?.Invoke(stm);
                break;
            case "obj_sync":
                var objSync = JsonSerializer.Deserialize<ObjectSyncData>(packet.P?.ToString() ?? "");
                if (objSync != null) OnObjectSync?.Invoke(objSync);
                break;
        }
    }

    private async Task Heartbeat()
    {
        while (_ws?.State == WebSocketState.Open)
        {
            await Task.Delay(30000);
            await SendAsync(new Packet { T = "ping" });
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }
}
