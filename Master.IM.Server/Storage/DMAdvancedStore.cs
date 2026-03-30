using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class DMAdvancedStore
{
    private readonly string _basePath;

    public DMAdvancedStore(string basePath)
    {
        _basePath = basePath;
    }

    public static string GetPairId(string userId1, string userId2)
    {
        var users = new[] { userId1, userId2 };
        Array.Sort(users);
        return $"{users[0]}_{users[1]}";
    }

    public async Task<DMConfig> GetOrCreateConfigAsync(string userId1, string userId2, bool enableStorage, int retentionDays = -1)
    {
        var pairId = GetPairId(userId1, userId2);
        var configPath = GetConfigPath(pairId);

        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<DMConfig>(json) ?? CreateDefaultConfig(pairId, userId1, userId2, enableStorage, retentionDays);
        }

        var config = CreateDefaultConfig(pairId, userId1, userId2, enableStorage, retentionDays);
        await SaveConfigAsync(config);
        return config;
    }

    private DMConfig CreateDefaultConfig(string pairId, string userId1, string userId2, bool enableStorage, int retentionDays)
    {
        return new DMConfig
        {
            PairId = pairId,
            EnableStorage = enableStorage,
            EnableRoaming = enableStorage,
            EnableEdit = enableStorage,
            RetentionDays = retentionDays,
            Participants = new() { userId1, userId2 }
        };
    }

    private async Task SaveConfigAsync(DMConfig config)
    {
        var configPath = GetConfigPath(config.PairId);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configPath, json);
    }

    public async Task SaveAsync(string pairId, GroupMessage msg)
    {
        var dbPath = GetDbPath(pairId, msg.SendTime);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO channel_dm (page_number, in_page_seq, send_time_ms, sender_id, content, reply_to_time_ms, quoted_content)
            VALUES (@page, @seq, @time, @sender, @content, @reply, @quoted)";

        cmd.Parameters.AddWithValue("@page", msg.PageNumber);
        cmd.Parameters.AddWithValue("@seq", msg.InPageSeq);
        cmd.Parameters.AddWithValue("@time", new DateTimeOffset(msg.SendTime).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@sender", msg.SenderId);
        cmd.Parameters.AddWithValue("@content", msg.Content);
        cmd.Parameters.AddWithValue("@reply", msg.ReplyToTime.HasValue ?
            new DateTimeOffset(msg.ReplyToTime.Value).ToUnixTimeMilliseconds() : DBNull.Value);
        cmd.Parameters.AddWithValue("@quoted", (object?)msg.QuotedContent ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        await UpdatePageMetadataAsync(conn, msg.PageNumber);
    }

    public async Task<List<GroupMessage>> GetPageAsync(string pairId, int lastPage, int lastSeq, int limit)
    {
        var dbPath = GetDbPath(pairId, DateTime.Now);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT page_number, in_page_seq, send_time_ms, sender_id, content, reply_to_time_ms, quoted_content
            FROM channel_dm
            WHERE page_number < @page OR (page_number = @page AND in_page_seq < @seq)
            ORDER BY page_number DESC, in_page_seq DESC
            LIMIT @limit";

        cmd.Parameters.AddWithValue("@page", lastPage);
        cmd.Parameters.AddWithValue("@seq", lastSeq);
        cmd.Parameters.AddWithValue("@limit", limit);

        var messages = new List<GroupMessage>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new GroupMessage
            {
                PageNumber = reader.GetInt32(0),
                InPageSeq = reader.GetInt32(1),
                SendTime = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)).DateTime,
                SenderId = reader.GetString(3),
                Content = reader.GetString(4),
                ReplyToTime = reader.IsDBNull(5) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)).DateTime,
                QuotedContent = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return messages;
    }

    public async Task UpdateAsync(string pairId, int page, int seq, string newContent)
    {
        var dbPath = GetDbPath(pairId, DateTime.Now);
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE channel_dm SET content=@content WHERE page_number=@page AND in_page_seq=@seq";
        cmd.Parameters.AddWithValue("@content", newContent);
        cmd.Parameters.AddWithValue("@page", page);
        cmd.Parameters.AddWithValue("@seq", seq);

        await cmd.ExecuteNonQueryAsync();
        await UpdatePageMetadataAsync(conn, page);
    }

    public async Task<long> GetPageModifiedTimeAsync(string pairId, int pageNumber)
    {
        var dbPath = GetDbPath(pairId, DateTime.Now);
        if (!File.Exists(dbPath)) return 0;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_modified FROM page_metadata WHERE channel_id='dm' AND page_number=@page";
        cmd.Parameters.AddWithValue("@page", pageNumber);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    private async Task UpdatePageMetadataAsync(SqliteConnection conn, int pageNumber)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO page_metadata (channel_id, page_number, last_modified)
            VALUES ('dm', @page, @time)";
        cmd.Parameters.AddWithValue("@page", pageNumber);
        cmd.Parameters.AddWithValue("@time", DateTimeOffset.Now.ToUnixTimeMilliseconds());
        await cmd.ExecuteNonQueryAsync();
    }

    private string GetConfigPath(string pairId)
    {
        return Path.Combine(_basePath, "dm", pairId, "config.json");
    }

    private string GetDbPath(string pairId, DateTime sendTime)
    {
        var month = sendTime.ToString("yyyy-MM");
        var dir = Path.Combine(_basePath, "dm", pairId, "messages");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{month}.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS channel_dm (
                page_number INTEGER,
                in_page_seq INTEGER,
                send_time_ms INTEGER,
                sender_id TEXT,
                content TEXT,
                reply_to_time_ms INTEGER,
                quoted_content TEXT,
                PRIMARY KEY (page_number, in_page_seq)
            )";
        cmd.ExecuteNonQuery();

        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS idx_time ON channel_dm(send_time_ms)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS page_metadata (
                channel_id TEXT,
                page_number INTEGER,
                last_modified INTEGER,
                PRIMARY KEY (channel_id, page_number)
            )";
        cmd.ExecuteNonQuery();
    }
}

