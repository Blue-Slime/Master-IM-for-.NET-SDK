using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.Server.WebSocket;

public class DMServer
{
    private readonly DMConnectionManager _connMgr;

    public DMServer(DMConnectionManager connMgr)
    {
        _connMgr = connMgr;
    }

    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket ws, string userId, string targetUserId)
    {
        var conn = new DMConnection(ws, userId, targetUserId);
        _connMgr.Add(userId, conn);

        // 如果对方在线，通知双方
        if (_connMgr.IsOnline(targetUserId))
        {
            await NotifyOnline(targetUserId, userId);
            await NotifyOnline(userId, targetUserId);
        }

        await ReceiveLoop(conn);

        // 通知对方离线
        await NotifyOffline(targetUserId, userId);
        _connMgr.Remove(userId);
    }

    private async Task ReceiveLoop(DMConnection conn)
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

    private async Task HandlePacket(DMConnection conn, string json)
    {
        var packet = JsonSerializer.Deserialize<Packet>(json);
        if (packet == null) return;

        if (packet.T == "dm_msg")
        {
            await ForwardMessage(conn, packet);
        }
    }

    private async Task ForwardMessage(DMConnection conn, Packet packet)
    {
        var targetConn = _connMgr.GetConnection(conn.TargetUserId);
        if (targetConn == null) return;

        var msg = new Packet
        {
            T = "dm_msg",
            P = new
            {
                SenderId = conn.UserId,
                Content = packet.P,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };

        await SendAsync(targetConn.WebSocket, msg);
    }

    private async Task NotifyOnline(string userId, string targetUserId)
    {
        var conn = _connMgr.GetConnection(userId);
        if (conn == null) return;

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "dm_online",
            P = new { UserId = targetUserId }
        });
    }

    private async Task NotifyOffline(string userId, string targetUserId)
    {
        var conn = _connMgr.GetConnection(userId);
        if (conn == null) return;

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "dm_offline",
            P = new { UserId = targetUserId }
        });
    }

    private async Task SendAsync(System.Net.WebSockets.WebSocket ws, Packet packet)
    {
        if (ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(packet);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
