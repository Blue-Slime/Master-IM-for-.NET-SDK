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

    public IMServer(ConnectionManager connMgr, MessageStore store, ObjectStore objStore)
    {
        _connMgr = connMgr;
        _store = store;
        _objStore = objStore;
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
            case "ping":
                await SendAsync(conn.WebSocket, new Packet { T = "pong" });
                break;
        }
    }

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

    private async Task HandleStream(Connection conn, Packet packet)
    {
        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, new Packet { T = "stm", P = packet.P });
    }

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

    // 群组事件广播
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
}
