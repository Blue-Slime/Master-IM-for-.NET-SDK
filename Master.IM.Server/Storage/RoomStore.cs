using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class RoomStore
{
    private readonly string _basePath;

    public RoomStore(string basePath)
    {
        _basePath = basePath;
    }

    private string GetDbPath()
    {
        var dir = Path.Combine(_basePath, "rooms");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "rooms.db");
    }

    private void EnsureDatabase()
    {
        var dbPath = GetDbPath();
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Rooms (
                RoomId TEXT PRIMARY KEY,
                RoomName TEXT NOT NULL,
                Description TEXT,
                Password TEXT,
                OwnerId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IsPublic INTEGER NOT NULL DEFAULT 1
            )";
        cmd.ExecuteNonQuery();
    }

    public async Task CreateRoomAsync(Room room)
    {
        EnsureDatabase();
        var dbPath = GetDbPath();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Rooms (RoomId, RoomName, Description, Password, OwnerId, CreatedAt, IsPublic)
            VALUES (@id, @name, @desc, @pwd, @owner, @created, @public)";

        cmd.Parameters.AddWithValue("@id", room.RoomId);
        cmd.Parameters.AddWithValue("@name", room.RoomName);
        cmd.Parameters.AddWithValue("@desc", room.Description ?? "");
        cmd.Parameters.AddWithValue("@pwd", room.Password ?? "");
        cmd.Parameters.AddWithValue("@owner", room.OwnerId);
        cmd.Parameters.AddWithValue("@created", room.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@public", room.IsPublic ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Room?> GetRoomAsync(string roomId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return null;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Rooms WHERE RoomId=@id";
        cmd.Parameters.AddWithValue("@id", roomId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Room
            {
                RoomId = reader.GetString(0),
                RoomName = reader.GetString(1),
                Description = reader.GetString(2),
                Password = reader.GetString(3),
                OwnerId = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                IsPublic = reader.GetInt32(6) == 1
            };
        }

        return null;
    }

    public async Task<List<Room>> GetAllRoomsAsync()
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Rooms WHERE IsPublic=1";

        var rooms = new List<Room>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rooms.Add(new Room
            {
                RoomId = reader.GetString(0),
                RoomName = reader.GetString(1),
                Description = reader.GetString(2),
                Password = reader.GetString(3),
                OwnerId = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                IsPublic = reader.GetInt32(6) == 1
            });
        }

        return rooms;
    }

    public async Task UpdateRoomAsync(Room room)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Rooms SET RoomName=@name, Description=@desc, Password=@pwd, IsPublic=@public
            WHERE RoomId=@id";

        cmd.Parameters.AddWithValue("@id", room.RoomId);
        cmd.Parameters.AddWithValue("@name", room.RoomName);
        cmd.Parameters.AddWithValue("@desc", room.Description ?? "");
        cmd.Parameters.AddWithValue("@pwd", room.Password ?? "");
        cmd.Parameters.AddWithValue("@public", room.IsPublic ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteRoomAsync(string roomId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Rooms WHERE RoomId=@id";
        cmd.Parameters.AddWithValue("@id", roomId);

        await cmd.ExecuteNonQueryAsync();

        // 删除房间文件夹
        var roomDir = Path.Combine(_basePath, "rooms", roomId);
        if (Directory.Exists(roomDir))
        {
            Directory.Delete(roomDir, true);
        }
    }
}


