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
    public event Action<int>? OnReconnecting;  // 重连中 (尝试次数)
    public event Action? OnReconnected;  // 重连成功
    public event Action<string>? OnConnectionError;  // 连接错误
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

    // 房间邀请回调
    public event Action<string, string, string>? OnRoomInviteReceived;  // 收到邀请 (inviterId, roomId, channelId)
    public event Action<string, string, string>? OnJoinRequestReceived;  // 收到加入请求 (requesterId, roomId, channelId)
    public event Action<string>? OnInviteAccepted;  // 邀请被接受 (userId)
    public event Action<string>? OnInviteRejected;  // 邀请被拒绝 (userId)

    // @提及回调
    public event Action<GroupMessage>? OnMentioned;  // 被@提及

    // 已读回执回调
    public event Action<string, int, int>? OnMessageRead;  // 消息已读 (userId, page, seq)

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
        try
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
            if (_ws?.State != WebSocketState.Open)
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
                catch
                {
                    // 继续重试
                }
            }
        }
    }

    /// <summary>
    /// 发送群聊消息
    /// </summary>
    public async Task SendMessageAsync(GroupMessage msg)
    {
        await SendAsync(new Packet { T = "msg", P = msg });
    }

    /// <summary>
    /// 分页查询历史消息
    /// </summary>
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

    /// <summary>
    /// 插入历史消息（超时空编辑）
    /// </summary>
    public async Task InsertHistoryMessageAsync(GroupMessage msg, DateTime historicalTime)
    {
        msg.SendTime = historicalTime;
        await SendAsync(new Packet { T = "msg", P = new { Type = "insert", GroupMessage = msg } });
    }

    /// <summary>
    /// 修改已发送的消息内容
    /// </summary>
    public async Task ModifyMessageAsync(int page, int seq, string newContent)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "modify", Page = page, Seq = seq, Content = newContent } });
    }

    /// <summary>
    /// 撤回消息
    /// </summary>
    public async Task RevokeMessageAsync(int page, int seq)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "revoke", Page = page, Seq = seq } });
    }

    /// <summary>
    /// 发送流式数据（用于实时同步）
    /// </summary>
    public async Task SendStreamAsync(StreamData data)
    {
        await SendAsync(new Packet { T = "stm", P = data });
    }

    /// <summary>
    /// 创建游戏对象
    /// </summary>
    public async Task<string> CreateObjectAsync(GameObject obj)
    {
        await SendAsync(new Packet { T = "obj_create", P = obj });
        return obj.Id;
    }

    /// <summary>
    /// 更新游戏对象
    /// </summary>
    public async Task UpdateObjectAsync(GameObject obj)
    {
        await SendAsync(new Packet { T = "obj_update", P = obj });
    }

    /// <summary>
    /// 删除游戏对象
    /// </summary>
    public async Task DeleteObjectAsync(string objectId)
    {
        await SendAsync(new Packet { T = "obj_delete", P = new { ObjectId = objectId } });
    }

    /// <summary>
    /// 按类型查询游戏对象
    /// </summary>
    public async Task<List<GameObject>> QueryObjectsByTypeAsync(string type)
    {
        var result = await RequestAsync<List<GameObject>>(new Packet
        {
            T = "obj_query",
            P = new { Type = type }
        });
        return result ?? new();
    }

    /// <summary>
    /// 按序列号范围查询游戏对象
    /// </summary>
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
                if (msg != null)
                {
                    OnMessageReceived?.Invoke(msg);
                    // 检测@提及
                    if (msg.MentionAll || msg.MentionedUserIds.Contains(_userId ?? ""))
                    {
                        OnMentioned?.Invoke(msg);
                    }
                }
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
                var tipsJson = packet.P?.ToString() ?? "";
                var tipsEvent = JsonSerializer.Deserialize<GroupTipsEvent>(tipsJson);

                if (tipsEvent != null)
                {
                    // 处理房间邀请
                    if (tipsEvent.Type == "room_invite" && tipsEvent.Data.ContainsKey("RoomId"))
                    {
                        OnRoomInviteReceived?.Invoke(
                            tipsEvent.UserId,
                            tipsEvent.Data["RoomId"].ToString() ?? "",
                            tipsEvent.Data.ContainsKey("ChannelId") ? tipsEvent.Data["ChannelId"].ToString() ?? "" : ""
                        );
                    }
                    // 处理加入请求
                    else if (tipsEvent.Type == "join_request" && tipsEvent.Data.ContainsKey("RoomId"))
                    {
                        OnJoinRequestReceived?.Invoke(
                            tipsEvent.UserId,
                            tipsEvent.Data["RoomId"].ToString() ?? "",
                            tipsEvent.Data.ContainsKey("ChannelId") ? tipsEvent.Data["ChannelId"].ToString() ?? "" : ""
                        );
                    }
                    // 邀请被接受
                    else if (tipsEvent.Type == "invite_accepted")
                    {
                        OnInviteAccepted?.Invoke(tipsEvent.UserId);
                    }
                    // 邀请被拒绝
                    else if (tipsEvent.Type == "invite_rejected")
                    {
                        OnInviteRejected?.Invoke(tipsEvent.UserId);
                    }
                }

                OnGroupTips?.Invoke(tipsJson);
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
            case "read_receipt":
                var receiptData = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
                if (receiptData != null && receiptData.ContainsKey("UserId"))
                    OnMessageRead?.Invoke(
                        receiptData["UserId"].ToString() ?? "",
                        Convert.ToInt32(receiptData["PageNumber"]),
                        Convert.ToInt32(receiptData["InPageSeq"])
                    );
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

    /// <summary>
    /// 添加群组成员
    /// </summary>
    public async Task AddGroupMemberAsync(string userId, string role = "member")
    {
        await SendAsync(new Packet { T = "grp_add_member", P = new { UserId = userId, Role = role } });
    }

    /// <summary>
    /// 移除群组成员
    /// </summary>
    public async Task RemoveGroupMemberAsync(string userId)
    {
        await SendAsync(new Packet { T = "grp_remove_member", P = new { UserId = userId } });
    }

    /// <summary>
    /// 上传文件到服务器
    /// </summary>
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

    /// <summary>
    /// 发送文件消息（上传后发送消息）
    /// </summary>
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

    /// <summary>
    /// 发送在线状态
    /// </summary>
    public async Task SendPresenceAsync(string status)
    {
        await SendAsync(new Packet { T = "presence", P = new { Status = status } });
    }

    /// <summary>
    /// 发送正在输入状态
    /// </summary>
    public async Task SendTypingAsync(bool isTyping)
    {
        await SendAsync(new Packet { T = "typing", P = new { IsTyping = isTyping } });
    }

    /// <summary>
    /// 发送骰子投掷结果
    /// </summary>
    public async Task SendDiceRollAsync(string formula, string result, bool isSecret = false)
    {
        await SendAsync(new Packet { T = "dice_roll", P = new { Formula = formula, Result = result, IsSecret = isSecret } });
    }

    /// <summary>
    /// 发送房间邀请给指定用户
    /// </summary>
    public async Task SendRoomInviteAsync(string targetUserId)
    {
        await SendAsync(new Packet { T = "room_invite", P = new { TargetUserId = targetUserId } });
    }

    /// <summary>
    /// 请求加入指定用户的房间
    /// </summary>
    public async Task RequestJoinRoomAsync(string targetUserId)
    {
        await SendAsync(new Packet { T = "join_request", P = new { TargetUserId = targetUserId } });
    }

    /// <summary>
    /// 接受房间邀请
    /// </summary>
    public async Task AcceptInviteAsync(string inviterId)
    {
        await SendAsync(new Packet { T = "invite_accept", P = new { InviterId = inviterId } });
    }

    /// <summary>
    /// 拒绝房间邀请
    /// </summary>
    public async Task RejectInviteAsync(string inviterId)
    {
        await SendAsync(new Packet { T = "invite_reject", P = new { InviterId = inviterId } });
    }

    /// <summary>
    /// 获取房间成员列表
    /// </summary>
    public async Task<List<RoomMember>> GetRoomMembersAsync()
    {
        var result = await RequestAsync<List<RoomMember>>(new Packet { T = "get_members" });
        return result ?? new();
    }

    /// <summary>
    /// 更新房间成员信息
    /// </summary>
    public async Task UpdateMemberAsync(RoomMember member)
    {
        await SendAsync(new Packet { T = "update_member", P = member });
    }

    /// <summary>
    /// 禁止成员进入房间
    /// </summary>
    public async Task BanMemberAsync(string userId)
    {
        await SendAsync(new Packet { T = "ban_member", P = new { UserId = userId } });
    }

    /// <summary>
    /// 发送消息已读回执
    /// </summary>
    public async Task SendReadReceiptAsync(int pageNumber, int inPageSeq)
    {
        await SendAsync(new Packet { T = "read_receipt", P = new { PageNumber = pageNumber, InPageSeq = inPageSeq } });
    }

    /// <summary>
    /// 搜索消息内容
    /// </summary>
    public async Task<List<GroupMessage>> SearchMessagesAsync(string keyword, int limit = 50)
    {
        var result = await RequestAsync<List<GroupMessage>>(new Packet
        {
            T = "search_msg",
            P = new { Keyword = keyword, Limit = limit }
        });
        return result ?? new();
    }

    /// <summary>
    /// 创建房间
    /// </summary>
    public async Task CreateRoomAsync(Room room)
    {
        await SendAsync(new Packet { T = "create_room", P = room });
    }

    /// <summary>
    /// 获取房间列表
    /// </summary>
    public async Task<List<Room>> GetRoomsAsync()
    {
        var result = await RequestAsync<List<Room>>(new Packet { T = "get_rooms" });
        return result ?? new();
    }

    /// <summary>
    /// 更新房间设置
    /// </summary>
    public async Task UpdateRoomAsync(Room room)
    {
        await SendAsync(new Packet { T = "update_room", P = room });
    }

    /// <summary>
    /// 删除房间
    /// </summary>
    public async Task DeleteRoomAsync(string roomId)
    {
        await SendAsync(new Packet { T = "delete_room", P = new { RoomId = roomId } });
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
