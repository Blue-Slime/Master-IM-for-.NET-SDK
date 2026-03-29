using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Collections.Concurrent;

namespace MasterIM.Server.WebSocket;

public class DMConnectionManager
{
    private readonly ConcurrentDictionary<string, DMConnection> _connections = new();

    public void Add(string userId, DMConnection conn)
    {
        _connections[userId] = conn;
    }

    public void Remove(string userId)
    {
        _connections.TryRemove(userId, out _);
    }

    public DMConnection? GetConnection(string userId)
    {
        _connections.TryGetValue(userId, out var conn);
        return conn;
    }

    public bool IsOnline(string userId)
    {
        return _connections.ContainsKey(userId);
    }
}
