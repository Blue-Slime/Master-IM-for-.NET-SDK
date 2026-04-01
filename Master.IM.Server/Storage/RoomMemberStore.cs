using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class RoomMemberStore
{
    private readonly string _basePath;

    public RoomMemberStore(string basePath)
    {
        _basePath = basePath;
    }

    private string GetDbPath(string roomId)
    {
        var dir = Path.Combine(_basePath, "rooms", roomId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "members.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS RoomMembers (
                RoomId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                SteamId TEXT NOT NULL,
                UserName TEXT NOT NULL,
                AvatarUrl TEXT,
                Nickname TEXT,
                Role TEXT NOT NULL DEFAULT 'member',
                AccessStatus TEXT NOT NULL DEFAULT 'allowed',
                JoinedAt TEXT NOT NULL,
                LastActiveAt TEXT NOT NULL,
                PRIMARY KEY (RoomId, UserId)
            )";
        cmd.ExecuteNonQuery();
    }

    public async Task AddOrUpdateMemberAsync(string roomId, RoomMember member)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO RoomMembers
            (RoomId, UserId, SteamId, UserName, AvatarUrl, Nickname, Role, AccessStatus, JoinedAt, LastActiveAt)
            VALUES (@roomId, @userId, @steamId, @userName, @avatar, @nickname, @role, @status, @joined, @active)";

        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@userId", member.UserId);
        cmd.Parameters.AddWithValue("@steamId", member.SteamId);
        cmd.Parameters.AddWithValue("@userName", member.UserName);
        cmd.Parameters.AddWithValue("@avatar", member.AvatarUrl ?? "");
        cmd.Parameters.AddWithValue("@nickname", member.Nickname ?? "");
        cmd.Parameters.AddWithValue("@role", member.Role);
        cmd.Parameters.AddWithValue("@status", member.AccessStatus);
        cmd.Parameters.AddWithValue("@joined", member.JoinedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@active", member.LastActiveAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<RoomMember?> GetMemberAsync(string roomId, string userId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return null;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RoomMembers WHERE RoomId=@roomId AND UserId=@userId";
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@userId", userId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new RoomMember
            {
                RoomId = reader.GetString(0),
                UserId = reader.GetString(1),
                SteamId = reader.GetString(2),
                UserName = reader.GetString(3),
                AvatarUrl = reader.GetString(4),
                Nickname = reader.GetString(5),
                Role = reader.GetString(6),
                AccessStatus = reader.GetString(7),
                JoinedAt = DateTime.Parse(reader.GetString(8)),
                LastActiveAt = DateTime.Parse(reader.GetString(9))
            };
        }

        return null;
    }

    public async Task<List<RoomMember>> GetAllMembersAsync(string roomId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RoomMembers WHERE RoomId=@roomId";
        cmd.Parameters.AddWithValue("@roomId", roomId);

        var members = new List<RoomMember>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            members.Add(new RoomMember
            {
                RoomId = reader.GetString(0),
                UserId = reader.GetString(1),
                SteamId = reader.GetString(2),
                UserName = reader.GetString(3),
                AvatarUrl = reader.GetString(4),
                Nickname = reader.GetString(5),
                Role = reader.GetString(6),
                AccessStatus = reader.GetString(7),
                JoinedAt = DateTime.Parse(reader.GetString(8)),
                LastActiveAt = DateTime.Parse(reader.GetString(9))
            });
        }

        return members;
    }

    public async Task RemoveMemberAsync(string roomId, string userId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RoomMembers WHERE RoomId=@roomId AND UserId=@userId";
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task BanMemberAsync(string roomId, string userId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE RoomMembers SET AccessStatus='banned' WHERE RoomId=@roomId AND UserId=@userId";
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteNonQueryAsync();
    }
}

