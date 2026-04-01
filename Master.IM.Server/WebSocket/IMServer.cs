using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;
using MasterIM.Server.Storage;

namespace MasterIM.Server.WebSocket;

public class IMServer
{
    private readonly ConnectionManager _connMgr;
    private readonly MessageStore _store;
    private readonly ObjectStore _objStore;
    private readonly RoomMemberStore _memberStore;
    private readonly RoomStore _roomStore;

    public IMServer(ConnectionManager connMgr, MessageStore store, ObjectStore objStore, RoomMemberStore memberStore, RoomStore roomStore)
    {
        _connMgr = connMgr;
        _store = store;
        _objStore = objStore;
        _memberStore = memberStore;
        _roomStore = roomStore;
    }

    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket ws, string userId, string roomId, string channelId)
    {
        var conn = new Connection(ws, userId, roomId, channelId);
        _connMgr.Add(userId, conn);

        await ReceiveLoop(conn);

        _connMgr.Remove(userId);
    }

    private async Task ReceiveLoop(Connection conn)
    {
        var buffer = new byte[8192];
        while (conn.WebSocket.State == WebSocketState.Open)
        {
            var result = await conn.WebSocket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await HandlePacket(conn, json);
        }
    }

    private async Task HandlePacket(Connection conn, string json)
    {
        var packet = JsonSerializer.Deserialize<Packet>(json);
        if (packet == null) return;

        switch (packet.T)
        {
            case "msg":
                await HandleMessage(conn, packet);
                break;
            case "qry":
                await HandleQuery(conn, packet);
                break;
            case "chk":  // 检查分页修改时间
                await HandleCheckPage(conn, packet);
                break;
            case "crt":  // 创建空白分页
                await HandleCreatePage(conn, packet);
                break;
            case "del":  // 删除空白分页
                await HandleDeletePage(conn, packet);
                break;
            case "bmv":  // 批量平移消息
                await HandleBatchMove(conn, packet);
                break;
            case "bdl":  // 批量删除消息
                await HandleBatchDelete(conn, packet);
                break;
            case "obj_create":  // 创建对象
                await HandleObjectCreate(conn, packet);
                break;
            case "obj_update":  // 更新对象
                await HandleObjectUpdate(conn, packet);
                break;
            case "obj_delete":  // 删除对象
                await HandleObjectDelete(conn, packet);
                break;
            case "obj_query":  // 查询对象
                await HandleObjectQuery(conn, packet);
                break;
            case "stm":
                await HandleStream(conn, packet);
                break;
            case "grp_add_member":
                await HandleAddMember(conn, packet);
                break;
            case "grp_remove_member":
                await HandleRemoveMember(conn, packet);
                break;
            case "presence":
                await HandlePresence(conn, packet);
                break;
            case "typing":
                await HandleTyping(conn, packet);
                break;
            case "dice_roll":
                await HandleDiceRoll(conn, packet);
                break;
            case "room_invite":
                await HandleRoomInvite(conn, packet);
                break;
            case "join_request":
                await HandleJoinRequest(conn, packet);
                break;
            case "invite_accept":
                await HandleInviteAccept(conn, packet);
                break;
            case "invite_reject":
                await HandleInviteReject(conn, packet);
                break;
            case "read_receipt":
                await HandleReadReceipt(conn, packet);
                break;
            case "search_msg":
                await HandleSearchMessage(conn, packet);
                break;
            case "get_members":
                await HandleGetMembers(conn, packet);
                break;
            case "update_member":
                await HandleUpdateMember(conn, packet);
                break;
            case "ban_member":
                await HandleBanMember(conn, packet);
                break;
            case "create_room":
                await HandleCreateRoom(conn, packet);
                break;
            case "get_rooms":
                await HandleGetRooms(conn, packet);
                break;
            case "update_room":
                await HandleUpdateRoom(conn, packet);
                break;
            case "delete_room":
                await HandleDeleteRoom(conn, packet);
                break;
            case "ping":
                await SendAsync(conn.WebSocket, new Packet { T = "pong" });
                break;
        }
    }

    /// <summary>
    /// 处理消息发送（包括普通消息、插入历史消息、修改消息、撤回消息）
    /// </summary>
    private async Task HandleMessage(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        if (data.ContainsKey("Type"))
        {
            var type = data["Type"].ToString();
            if (type == "insert")
            {
                await HandleInsertHistory(conn, data);
                return;
            }
            if (type == "modify")
            {
                await HandleModify(conn, data);
                return;
            }
            if (type == "revoke")
            {
                await HandleRevoke(conn, data);
                return;
            }
        }

        var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
        if (msg == null) return;

        await _store.SaveAsync(conn.RoomId, conn.ChannelId, msg);
        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet { T = "msg", P = msg });
    }

    /// <summary>
    /// 处理插入历史消息（超时空编辑）
    /// </summary>
    private async Task HandleInsertHistory(Connection conn, Dictionary<string, object> data)
    {
        var msg = JsonSerializer.Deserialize<GroupMessage>(data["GroupMessage"]?.ToString() ?? "");
        if (msg == null) return;

        await _store.SaveAsync(conn.RoomId, conn.ChannelId, msg);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "ntf",
            P = new { Type = "message_inserted", TimeMs = new DateTimeOffset(msg.SendTime).ToUnixTimeMilliseconds() }
        });
    }

    /// <summary>
    /// 处理修改消息内容
    /// </summary>
    private async Task HandleModify(Connection conn, Dictionary<string, object> data)
    {
        var page = Convert.ToInt32(data["Page"]);
        var seq = Convert.ToInt32(data["Seq"]);
        var content = data["Content"].ToString() ?? "";

        await _store.UpdateAsync(conn.RoomId, conn.ChannelId, page, seq, content);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "ntf",
            P = new { Type = "message_modified", Page = page, Seq = seq }
        });
    }

    /// <summary>
    /// 处理分页查询历史消息
    /// </summary>
    private async Task HandleQuery(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var lastPage = Convert.ToInt32(req["LastPage"]);
        var lastSeq = Convert.ToInt32(req["LastSeq"]);
        var limit = Convert.ToInt32(req["Limit"]);

        var messages = await _store.GetPageAsync(conn.RoomId, conn.ChannelId, lastPage, lastSeq, limit);
        await SendAsync(conn.WebSocket, new Packet { T = "qry", P = messages, Id = packet.Id });
    }

    /// <summary>
    /// 处理检查分页修改时间请求
    /// </summary>
    private async Task HandleCheckPage(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var pageNumber = Convert.ToInt32(req["PageNumber"]);
        var modifiedTime = await _store.GetPageModifiedTimeAsync(conn.RoomId, conn.ChannelId, pageNumber);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "chk",
            P = new { PageNumber = pageNumber, LastModified = modifiedTime },
            Id = packet.Id
        });
    }

    /// <summary>
    /// 处理创建空白分页请求
    /// </summary>
    private async Task HandleCreatePage(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var pageNumber = Convert.ToInt32(req["PageNumber"]);
        await _store.CreateEmptyPageAsync(conn.RoomId, conn.ChannelId, pageNumber);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "ntf",
            P = new { Type = "page_created", PageNumber = pageNumber }
        });
    }

    /// <summary>
    /// 处理删除空白分页请求
    /// </summary>
    private async Task HandleDeletePage(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var pageNumber = Convert.ToInt32(req["PageNumber"]);
        var success = await _store.DeleteEmptyPageAsync(conn.RoomId, conn.ChannelId, pageNumber);

        if (success)
        {
            await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
            {
                T = "ntf",
                P = new { Type = "page_deleted", PageNumber = pageNumber }
            });
        }
    }

    /// <summary>
    /// 处理批量平移消息请求
    /// </summary>
    private async Task HandleBatchMove(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var messages = JsonSerializer.Deserialize<List<Dictionary<string, int>>>(req["Messages"]?.ToString() ?? "");
        var targetPage = Convert.ToInt32(req["TargetPage"]);
        var targetSeq = Convert.ToInt32(req["TargetSeq"]);

        if (messages == null) return;

        var msgList = messages.Select(m => (m["Page"], m["Seq"])).ToList();
        var affectedPages = await _store.BatchMoveMessagesAsync(conn.RoomId, conn.ChannelId, msgList, targetPage, targetSeq);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "ntf",
            P = new { Type = "batch_moved", AffectedPages = affectedPages }
        });
    }

    /// <summary>
    /// 处理批量删除消息请求
    /// </summary>
    private async Task HandleBatchDelete(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var messages = JsonSerializer.Deserialize<List<Dictionary<string, int>>>(req["Messages"]?.ToString() ?? "");
        if (messages == null) return;

        var msgList = messages.Select(m => (m["Page"], m["Seq"])).ToList();
        var affectedPages = await _store.BatchDeleteMessagesAsync(conn.RoomId, conn.ChannelId, msgList);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "ntf",
            P = new { Type = "batch_deleted", AffectedPages = affectedPages }
        });
    }

    /// <summary>
    /// 处理流式数据传输（实时同步）
    /// </summary>
    private async Task HandleStream(Connection conn, Packet packet)
    {
        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, new Packet { T = "stm", P = packet.P });
    }

    /// <summary>
    /// 处理创建游戏对象
    /// </summary>
    private async Task HandleObjectCreate(Connection conn, Packet packet)
    {
        var obj = JsonSerializer.Deserialize<Storage.GameObject>(packet.P?.ToString() ?? "");
        if (obj == null) return;

        obj.RoomId = conn.RoomId;
        obj.CreatorId = conn.UserId;
        var seqNumber = await _objStore.SaveAsync(conn.RoomId, obj);

        await BroadcastToRoom(conn.RoomId, new Packet
        {
            T = "obj_sync",
            P = new { Action = "create", Object = obj, SequenceNumber = seqNumber }
        });
    }

    /// <summary>
    /// 处理更新游戏对象
    /// </summary>
    private async Task HandleObjectUpdate(Connection conn, Packet packet)
    {
        var obj = JsonSerializer.Deserialize<Storage.GameObject>(packet.P?.ToString() ?? "");
        if (obj == null) return;

        var seqNumber = await _objStore.SaveAsync(conn.RoomId, obj);

        await BroadcastToRoom(conn.RoomId, new Packet
        {
            T = "obj_sync",
            P = new { Action = "update", Object = obj, SequenceNumber = seqNumber }
        });
    }

    /// <summary>
    /// 处理删除游戏对象
    /// </summary>
    private async Task HandleObjectDelete(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var objectId = req["ObjectId"].ToString() ?? "";
        await _objStore.DeleteAsync(conn.RoomId, objectId);

        await BroadcastToRoom(conn.RoomId, new Packet
        {
            T = "obj_sync",
            P = new { Action = "delete", ObjectId = objectId }
        });
    }

    /// <summary>
    /// 处理查询游戏对象（按类型或序列号范围）
    /// </summary>
    private async Task HandleObjectQuery(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        List<Storage.GameObject> objects;

        if (req.ContainsKey("Type"))
        {
            var type = req["Type"].ToString() ?? "";
            objects = await _objStore.GetByTypeAsync(conn.RoomId, type);
        }
        else if (req.ContainsKey("StartSeq") && req.ContainsKey("EndSeq"))
        {
            var startSeq = Convert.ToInt64(req["StartSeq"]);
            var endSeq = Convert.ToInt64(req["EndSeq"]);
            objects = await _objStore.GetBySequenceRangeAsync(conn.RoomId, startSeq, endSeq);
        }
        else
        {
            objects = new();
        }

        await SendAsync(conn.WebSocket, new Packet { T = "obj_query", P = objects, Id = packet.Id });
    }

    private async Task BroadcastToRoom(string roomId, Packet packet)
    {
        var connections = _connMgr.GetRoomConnections(roomId);
        foreach (var conn in connections)
        {
            await SendAsync(conn.WebSocket, packet);
        }
    }

    private async Task BroadcastToChannel(string roomId, string channelId, Packet packet)
    {
        var connections = _connMgr.GetChannelConnections(roomId, channelId);
        foreach (var conn in connections)
        {
            await SendAsync(conn.WebSocket, packet);
        }
    }

    private async Task BroadcastToChannelExcept(string roomId, string channelId, string exceptUserId, Packet packet)
    {
        var connections = _connMgr.GetChannelConnections(roomId, channelId).Where(c => c.UserId != exceptUserId);
        foreach (var conn in connections)
        {
            await SendAsync(conn.WebSocket, packet);
        }
    }

    private async Task SendAsync(System.Net.WebSockets.WebSocket ws, Packet packet)
    {
        if (ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(packet);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>
    /// 处理消息撤回
    /// </summary>
    private async Task HandleRevoke(Connection conn, Dictionary<string, object> data)
    {
        var page = Convert.ToInt32(data["Page"]);
        var seq = Convert.ToInt32(data["Seq"]);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "msg_revoked",
            P = new { page, seq }
        });
    }

    /// <summary>
    /// 处理添加群组成员
    /// </summary>
    private async Task HandleAddMember(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var userId = data["UserId"];
        var role = data.ContainsKey("Role") ? data["Role"] : "member";

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "member_joined",
            P = new { UserId = userId, Role = role }
        });
    }

    /// <summary>
    /// 处理移除群组成员
    /// </summary>
    private async Task HandleRemoveMember(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var userId = data["UserId"];

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "member_left",
            P = new { UserId = userId }
        });
    }

    /// <summary>
    /// 处理用户在线状态更新
    /// </summary>
    private async Task HandlePresence(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var status = data["Status"]; // online, away, busy, offline

        await BroadcastGroupTips(conn, new GroupTipsEvent
        {
            Type = $"user_{status}",
            RoomId = conn.RoomId,
            ChannelId = conn.ChannelId,
            UserId = conn.UserId,
            Data = new() { { "Status", status } }
        });
    }

    /// <summary>
    /// 处理正在输入状态
    /// </summary>
    private async Task HandleTyping(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var isTyping = data.ContainsKey("IsTyping") && Convert.ToBoolean(data["IsTyping"]);

        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, new Packet
        {
            T = "group_tips",
            P = new GroupTipsEvent
            {
                Type = isTyping ? "typing" : "stop_typing",
                UserId = conn.UserId
            }
        });
    }

    /// <summary>
    /// 处理骰子投掷
    /// </summary>
    private async Task HandleDiceRoll(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var isSecret = data.ContainsKey("IsSecret") && Convert.ToBoolean(data["IsSecret"]);
        var formula = data["Formula"]?.ToString() ?? "";
        var result = data["Result"]?.ToString() ?? "";

        await BroadcastGroupTips(conn, new GroupTipsEvent
        {
            Type = isSecret ? "secret_dice_roll" : "dice_roll",
            RoomId = conn.RoomId,
            ChannelId = conn.ChannelId,
            UserId = conn.UserId,
            Data = new()
            {
                { "Formula", formula },
                { "Result", result },
                { "IsSecret", isSecret }
            }
        });
    }

    /// <summary>
    /// 处理发送房间邀请
    /// </summary>
    private async Task HandleRoomInvite(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var targetUserId = data["TargetUserId"];

        // 发送给目标用户
        var targetConn = _connMgr.GetUserConnection(targetUserId);
        if (targetConn != null)
        {
            await SendAsync(targetConn.WebSocket, new Packet
            {
                T = "group_tips",
                P = new GroupTipsEvent
                {
                    Type = "room_invite",
                    UserId = conn.UserId,
                    Data = new()
                    {
                        { "RoomId", conn.RoomId },
                        { "ChannelId", conn.ChannelId }
                    }
                }
            });
        }
    }

    /// <summary>
    /// 处理请求加入房间
    /// </summary>
    private async Task HandleJoinRequest(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var targetUserId = data["TargetUserId"];

        // 发送给目标用户
        var targetConn = _connMgr.GetUserConnection(targetUserId);
        if (targetConn != null)
        {
            await SendAsync(targetConn.WebSocket, new Packet
            {
                T = "group_tips",
                P = new GroupTipsEvent
                {
                    Type = "join_request",
                    UserId = conn.UserId,
                    Data = new()
                    {
                        { "RoomId", conn.RoomId },
                        { "ChannelId", conn.ChannelId }
                    }
                }
            });
        }
    }

    /// <summary>
    /// 处理接受邀请
    /// </summary>
    private async Task HandleInviteAccept(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var inviterId = data["InviterId"];

        var inviterConn = _connMgr.GetUserConnection(inviterId);
        if (inviterConn != null)
        {
            await SendAsync(inviterConn.WebSocket, new Packet
            {
                T = "group_tips",
                P = new GroupTipsEvent
                {
                    Type = "invite_accepted",
                    UserId = conn.UserId
                }
            });
        }
    }

    /// <summary>
    /// 处理拒绝邀请
    /// </summary>
    private async Task HandleInviteReject(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var inviterId = data["InviterId"];

        var inviterConn = _connMgr.GetUserConnection(inviterId);
        if (inviterConn != null)
        {
            await SendAsync(inviterConn.WebSocket, new Packet
            {
                T = "group_tips",
                P = new GroupTipsEvent
                {
                    Type = "invite_rejected",
                    UserId = conn.UserId
                }
            });
        }
    }

    /// <summary>
    /// 处理消息已读回执
    /// </summary>
    private async Task HandleReadReceipt(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, int>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, new Packet
        {
            T = "read_receipt",
            P = new
            {
                UserId = conn.UserId,
                PageNumber = data["PageNumber"],
                InPageSeq = data["InPageSeq"]
            }
        });
    }



    private async Task BroadcastGroupTips(Connection conn, GroupTipsEvent tips)
    {
        var packet = new Packet
        {
            T = "group_tips",
            P = tips
        };

        var conns = _connMgr.GetChannelConnections(conn.RoomId, conn.ChannelId);
        foreach (var c in conns)
        {
            await SendAsync(c.WebSocket, packet);
        }
    }

    /// <summary>
    /// 处理消息搜索请求
    /// </summary>
    private async Task HandleSearchMessage(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var keyword = data["Keyword"]?.ToString() ?? "";
        var limit = data.ContainsKey("Limit") ? Convert.ToInt32(data["Limit"]) : 50;

        var results = await _store.SearchMessagesAsync(conn.RoomId, conn.ChannelId, keyword, limit);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "search_msg",
            Id = packet.Id,
            P = results
        });
    }

    /// <summary>
    /// 获取房间成员列表
    /// </summary>
    private async Task HandleGetMembers(Connection conn, Packet packet)
    {
        var members = await _memberStore.GetAllMembersAsync(conn.RoomId);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "get_members",
            Id = packet.Id,
            P = members
        });
    }

    /// <summary>
    /// 更新成员信息
    /// </summary>
    private async Task HandleUpdateMember(Connection conn, Packet packet)
    {
        var member = JsonSerializer.Deserialize<RoomMember>(packet.P?.ToString() ?? "");
        if (member == null) return;

        await _memberStore.AddOrUpdateMemberAsync(conn.RoomId, member);
    }

    /// <summary>
    /// 禁止成员进入房间
    /// </summary>
    private async Task HandleBanMember(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var userId = data["UserId"];
        await _memberStore.BanMemberAsync(conn.RoomId, userId);
    }

    /// <summary>
    /// 创建房间
    /// </summary>
    private async Task HandleCreateRoom(Connection conn, Packet packet)
    {
        var room = JsonSerializer.Deserialize<Room>(packet.P?.ToString() ?? "");
        if (room == null) return;

        await _roomStore.CreateRoomAsync(room);
    }

    /// <summary>
    /// 获取房间列表
    /// </summary>
    private async Task HandleGetRooms(Connection conn, Packet packet)
    {
        var rooms = await _roomStore.GetAllRoomsAsync();

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "get_rooms",
            Id = packet.Id,
            P = rooms
        });
    }

    /// <summary>
    /// 更新房间设置
    /// </summary>
    private async Task HandleUpdateRoom(Connection conn, Packet packet)
    {
        var room = JsonSerializer.Deserialize<Room>(packet.P?.ToString() ?? "");
        if (room == null) return;

        await _roomStore.UpdateRoomAsync(room);
    }

    /// <summary>
    /// 删除房间
    /// </summary>
    private async Task HandleDeleteRoom(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var roomId = data["RoomId"];
        await _roomStore.DeleteRoomAsync(roomId);
    }
}
