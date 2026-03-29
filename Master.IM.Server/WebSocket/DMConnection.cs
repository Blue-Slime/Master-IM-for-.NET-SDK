using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Net.WebSockets;

namespace MasterIM.Server.WebSocket;

public class DMConnection
{
    public System.Net.WebSockets.WebSocket WebSocket { get; }
    public string UserId { get; }
    public string TargetUserId { get; }

    public DMConnection(System.Net.WebSockets.WebSocket ws, string userId, string targetUserId)
    {
        WebSocket = ws;
        UserId = userId;
        TargetUserId = targetUserId;
    }
}
