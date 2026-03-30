using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.IO;
using System.Net.Http;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;

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

    public event Action<GroupMessage>? OnMessageReceived;
    public event Action<UpdateNotification>? OnUpdateNotification;
    public event Action<StreamData>? OnStreamReceived;
    public event Action<ObjectSyncData>? OnObjectSync;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<string>? OnGroupTips;  // 群组提示事件
    public event Action<int, int>? OnMessageRevoked;  // 消息撤回 (page, seq)

    // 超时空编辑细分回调
    public event Action<long>? OnMessageInserted;  // 历史消息插入 (TimeMs)
    public event Action<int, int>? OnMessageModified;  // 消息修改 (page, seq)
    public event Action<int>? OnPageCreated;  // 分页创建
    public event Action<int>? OnPageDeleted;  // 分页删除
    public event Action<List<int>>? OnBatchMoved;  // 批量移动
    public event Action<List<int>>? OnBatchDeleted;  // 批量删除

    // 群组成员回调
    public event Action<string, string>? OnMemberJoined;  // 成员加入 (userId, role)
    public event Action<string>? OnMemberLeft;  // 成员离开

    // 文件传输回调
    public event Action<string, long, long>? OnFileUploadProgress;  // 上传进度 (fileId, uploaded, total)
    public event Action<string>? OnFileUploadComplete;  // 上传完成 (fileId)
    public event Action<string, string>? OnFileUploadFailed;  // 上传失败 (fileId, error)

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

    public async Task InsertHistoryMessageAsync(GroupMessage msg, DateTime historicalTime)
    {
        msg.SendTime = historicalTime;
        await SendAsync(new Packet { T = "msg", P = new { Type = "insert", GroupMessage = msg } });
    }

    public async Task ModifyMessageAsync(int page, int seq, string newContent)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "modify", Page = page, Seq = seq, Content = newContent } });
    }

    public async Task RevokeMessageAsync(int page, int seq)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "revoke", Page = page, Seq = seq } });
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
                var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
                if (msg != null) OnMessageReceived?.Invoke(msg);
                break;
            case "ntf":
                var ntf = JsonSerializer.Deserialize<UpdateNotification>(packet.P?.ToString() ?? "");
                if (ntf != null)
                {
                    OnUpdateNotification?.Invoke(ntf);
                    HandleNotification(ntf);
                }
                break;
            case "stm":
                var stm = JsonSerializer.Deserialize<StreamData>(packet.P?.ToString() ?? "");
                if (stm != null) OnStreamReceived?.Invoke(stm);
                break;
            case "obj_sync":
                var objSync = JsonSerializer.Deserialize<ObjectSyncData>(packet.P?.ToString() ?? "");
                if (objSync != null) OnObjectSync?.Invoke(objSync);
                break;
            case "group_tips":
                var tips = packet.P?.ToString() ?? "";
                OnGroupTips?.Invoke(tips);
                break;
            case "msg_revoked":
                var revokeData = JsonSerializer.Deserialize<Dictionary<string, int>>(packet.P?.ToString() ?? "");
                if (revokeData != null && revokeData.ContainsKey("page") && revokeData.ContainsKey("seq"))
                    OnMessageRevoked?.Invoke(revokeData["page"], revokeData["seq"]);
                break;
            case "member_joined":
                var joinData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (joinData != null && joinData.ContainsKey("UserId") && joinData.ContainsKey("Role"))
                    OnMemberJoined?.Invoke(joinData["UserId"], joinData["Role"]);
                break;
            case "member_left":
                var leftData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (leftData != null && leftData.ContainsKey("UserId"))
                    OnMemberLeft?.Invoke(leftData["UserId"]);
                break;
            case "file_progress":
                var progressData = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
                if (progressData != null && progressData.ContainsKey("FileId"))
                    OnFileUploadProgress?.Invoke(
                        progressData["FileId"].ToString() ?? "",
                        Convert.ToInt64(progressData["Uploaded"]),
                        Convert.ToInt64(progressData["Total"]));
                break;
            case "file_complete":
                var completeData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (completeData != null && completeData.ContainsKey("FileId"))
                    OnFileUploadComplete?.Invoke(completeData["FileId"]);
                break;
            case "file_failed":
                var failData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (failData != null && failData.ContainsKey("FileId"))
                    OnFileUploadFailed?.Invoke(failData["FileId"], failData.GetValueOrDefault("Error", "Unknown error"));
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

    // 群组成员管理
    public async Task AddGroupMemberAsync(string userId, string role = "member")
    {
        await SendAsync(new Packet { T = "grp_add_member", P = new { UserId = userId, Role = role } });
    }

    public async Task RemoveGroupMemberAsync(string userId)
    {
        await SendAsync(new Packet { T = "grp_remove_member", P = new { UserId = userId } });
    }

    public async Task<FileUploadResult> UploadFileAsync(string filePath)
    {
        using var client = new HttpClient();
        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);

        var fileName = Path.GetFileName(filePath);
        var fileContent = new StreamContent(fileStream);
        form.Add(fileContent, "file", fileName);

        var uploadUrl = $"{_url?.Replace("ws://", "http://").Replace("wss://", "https://")}/upload?userId={_userId}&roomId={_roomId}";
        var response = await client.PostAsync(uploadUrl, form);

        var result = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(result);

        return new FileUploadResult
        {
            FileId = json?["FileId"]?.ToString() ?? "",
            FileName = json?["FileName"]?.ToString() ?? "",
            FileSize = Convert.ToInt64(json?["FileSize"]),
            FileType = json?["FileType"]?.ToString() ?? "",
            Url = json?["Url"]?.ToString() ?? ""
        };
    }

    public async Task SendFileMessageAsync(FileUploadResult fileResult, string? roleId = null)
    {
        var msg = new GroupMessage
        {
            MessageType = fileResult.FileType,
            Content = JsonSerializer.Serialize(new
            {
                FileId = fileResult.FileId,
                FileName = fileResult.FileName,
                FileSize = fileResult.FileSize,
                Url = fileResult.Url
            }),
            RoleId = roleId
        };

        await SendMessageAsync(msg);
    }

    public async Task SendPresenceAsync(string status)
    {
        await SendAsync(new Packet { T = "presence", P = new { Status = status } });
    }

    public async Task SendTypingAsync(bool isTyping)
    {
        await SendAsync(new Packet { T = "typing", P = new { IsTyping = isTyping } });
    }

    public async Task SendDiceRollAsync(string formula, string result, bool isSecret = false)
    {
        await SendAsync(new Packet { T = "dice_roll", P = new { Formula = formula, Result = result, IsSecret = isSecret } });
    }


    private void HandleNotification(UpdateNotification ntf)
    {
        var json = JsonSerializer.Serialize(ntf);
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        if (data == null) return;

        switch (ntf.Type)
        {
            case "message_inserted":
                if (data.ContainsKey("TimeMs"))
                    OnMessageInserted?.Invoke(Convert.ToInt64(data["TimeMs"]));
                break;
            case "message_modified":
                if (data.ContainsKey("Page") && data.ContainsKey("Seq"))
                    OnMessageModified?.Invoke(Convert.ToInt32(data["Page"]), Convert.ToInt32(data["Seq"]));
                break;
            case "page_created":
                if (data.ContainsKey("PageNumber"))
                    OnPageCreated?.Invoke(Convert.ToInt32(data["PageNumber"]));
                break;
            case "page_deleted":
                if (data.ContainsKey("PageNumber"))
                    OnPageDeleted?.Invoke(Convert.ToInt32(data["PageNumber"]));
                break;
            case "batch_moved":
                if (data.ContainsKey("AffectedPages"))
                {
                    var pages = JsonSerializer.Deserialize<List<int>>(data["AffectedPages"].ToString() ?? "[]");
                    if (pages != null) OnBatchMoved?.Invoke(pages);
                }
                break;
            case "batch_deleted":
                if (data.ContainsKey("AffectedPages"))
                {
                    var pages = JsonSerializer.Deserialize<List<int>>(data["AffectedPages"].ToString() ?? "[]");
                    if (pages != null) OnBatchDeleted?.Invoke(pages);
                }
                break;
        }
    }


    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }
}
