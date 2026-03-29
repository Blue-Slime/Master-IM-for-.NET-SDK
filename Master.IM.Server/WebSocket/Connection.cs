using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Net.WebSockets;

namespace MasterIM.Server.WebSocket;

public class Connection
{
    public System.Net.WebSockets.WebSocket WebSocket { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;

    public Connection(System.Net.WebSockets.WebSocket ws, string userId, string roomId, string channelId)
    {
        WebSocket = ws;
        UserId = userId;
        RoomId = roomId;
        ChannelId = channelId;
    }
}
