using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Collections.Concurrent;

namespace MasterIM.Server.WebSocket;

public class ConnectionManager
{
    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public void Add(string userId, Connection conn)
    {
        _connections[userId] = conn;
    }

    public void Remove(string userId)
    {
        _connections.TryRemove(userId, out _);
    }

    public Connection? GetUserConnection(string userId)
    {
        _connections.TryGetValue(userId, out var conn);
        return conn;
    }

    public List<Connection> GetChannelConnections(string roomId, string channelId)
    {
        return _connections.Values.Where(c => c.RoomId == roomId && c.ChannelId == channelId).ToList();
    }

    public List<Connection> GetRoomConnections(string roomId)
    {
        return _connections.Values.Where(c => c.RoomId == roomId).ToList();
    }
}
