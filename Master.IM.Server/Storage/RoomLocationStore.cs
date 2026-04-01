using System;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 本机房间列表库（用于定位房间位置）
/// </summary>
public class RoomLocationStore
{
    private readonly string _basePath;
    private readonly string _serverId;

    public RoomLocationStore(string basePath, string serverId)
    {
        _basePath = basePath;
        _serverId = serverId;
    }

    private string GetDbPath()
    {
        Directory.CreateDirectory(_basePath);
        return Path.Combine(_basePath, "room_locations.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS RoomLocations (
                RoomId TEXT PRIMARY KEY,
                ServerId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_server ON RoomLocations(ServerId)";
        cmd.ExecuteNonQuery();
    }

    public async Task<bool> IsRoomOnThisServerAsync(string roomId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return false;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ServerId FROM RoomLocations WHERE RoomId=@roomId";
        cmd.Parameters.AddWithValue("@roomId", roomId);

        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() == _serverId;
    }

    public async Task RegisterRoomAsync(string roomId)
    {
        var dbPath = GetDbPath();
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO RoomLocations (RoomId, ServerId, CreatedAt, UpdatedAt)
            VALUES (@roomId, @serverId, @created, @updated)";
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@serverId", _serverId);
        cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetRoomServerIdAsync(string roomId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return null;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ServerId FROM RoomLocations WHERE RoomId=@roomId";
        cmd.Parameters.AddWithValue("@roomId", roomId);

        return (await cmd.ExecuteScalarAsync())?.ToString();
    }
}
