using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class GroupStore
{
    private readonly string _basePath = "data/groups";

    public GroupStore()
    {
        Directory.CreateDirectory(_basePath);
    }

    private string GetDbPath(string roomId)
    {
        return Path.Combine(_basePath, $"{roomId}.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        if (File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS members (
                user_id TEXT PRIMARY KEY,
                role TEXT NOT NULL,
                join_time INTEGER NOT NULL,
                nickname TEXT
            );
            CREATE TABLE IF NOT EXISTS channels (
                channel_id TEXT PRIMARY KEY,
                channel_name TEXT NOT NULL,
                create_time INTEGER NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
    }

    // 添加成员
    public async Task AddMemberAsync(string roomId, GroupMember member)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO members (user_id, role, join_time, nickname) VALUES (@uid, @role, @time, @nick)";
        cmd.Parameters.AddWithValue("@uid", member.UserId);
        cmd.Parameters.AddWithValue("@role", member.Role);
        cmd.Parameters.AddWithValue("@time", new DateTimeOffset(member.JoinTime).ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@nick", member.Nickname ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // 移除成员
    public async Task RemoveMemberAsync(string roomId, string userId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM members WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    // 获取成员列表
    public async Task<List<GroupMember>> GetMembersAsync(string roomId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return new List<GroupMember>();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id, role, join_time, nickname FROM members";
        using var reader = await cmd.ExecuteReaderAsync();

        var members = new List<GroupMember>();
        while (await reader.ReadAsync())
        {
            members.Add(new GroupMember
            {
                UserId = reader.GetString(0),
                RoomId = roomId,
                Role = reader.GetString(1),
                JoinTime = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)).DateTime,
                Nickname = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }
        return members;
    }
}
