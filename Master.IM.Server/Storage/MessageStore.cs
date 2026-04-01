using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class MessageStore
{
    private readonly string _basePath;

    public MessageStore(string basePath)
    {
        _basePath = basePath;
    }

    public async Task SaveAsync(string roomId, string channelId, GroupMessage msg)
    {
        var dbPath = GetDbPath(roomId, msg.SendTime);
        EnsureDatabase(dbPath, channelId);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 自动分配页号和序号
        if (msg.PageNumber == 0 && msg.InPageSeq == 0)
        {
            var getMaxCmd = conn.CreateCommand();
            getMaxCmd.CommandText = $"SELECT COALESCE(MAX(page_number), 0), COALESCE(MAX(in_page_seq), -1) FROM channel_{channelId}";
            using var reader = await getMaxCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var maxPage = reader.GetInt32(0);
                var maxSeq = reader.GetInt32(1);
                msg.PageNumber = maxPage;
                msg.InPageSeq = maxSeq + 1;
            }
        }

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO channel_{channelId} (page_number, in_page_seq, send_time_ms, sender_id, content, reply_to_time_ms, quoted_content)
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

        // 更新分页修改时间
        await UpdatePageMetadataAsync(conn, channelId, msg.PageNumber);
    }

    public async Task<List<GroupMessage>> GetPageAsync(string roomId, string channelId, int lastPage, int lastSeq, int limit)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT page_number, in_page_seq, send_time_ms, sender_id, content, reply_to_time_ms, quoted_content
            FROM channel_{channelId}
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

    public async Task UpdateAsync(string roomId, string channelId, int page, int seq, string newContent)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 更新消息内容
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE channel_{channelId} SET content=@content WHERE page_number=@page AND in_page_seq=@seq";
        cmd.Parameters.AddWithValue("@content", newContent);
        cmd.Parameters.AddWithValue("@page", page);
        cmd.Parameters.AddWithValue("@seq", seq);

        await cmd.ExecuteNonQueryAsync();

        // 更新分页修改时间
        await UpdatePageMetadataAsync(conn, channelId, page);
    }

    /// <summary>
    /// 检查分页修改时间
    /// </summary>
    public async Task<long> GetPageModifiedTimeAsync(string roomId, string channelId, int pageNumber)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return 0;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_modified FROM page_metadata WHERE channel_id=@channel AND page_number=@page";
        cmd.Parameters.AddWithValue("@channel", channelId);
        cmd.Parameters.AddWithValue("@page", pageNumber);

        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    /// <summary>
    /// 新建空白分页
    /// </summary>
    public async Task CreateEmptyPageAsync(string roomId, string channelId, int pageNumber)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        EnsureDatabase(dbPath, channelId);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 更新分页元数据
        await UpdatePageMetadataAsync(conn, channelId, pageNumber);
    }

    /// <summary>
    /// 删除空白分页
    /// </summary>
    public async Task<bool> DeleteEmptyPageAsync(string roomId, string channelId, int pageNumber)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return false;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 检查分页是否为空
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = $"SELECT COUNT(*) FROM channel_{channelId} WHERE page_number=@page";
        checkCmd.Parameters.AddWithValue("@page", pageNumber);
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        if (count > 0) return false; // 分页不为空,不能删除

        // 删除元数据
        var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM page_metadata WHERE channel_id=@channel AND page_number=@page";
        deleteCmd.Parameters.AddWithValue("@channel", channelId);
        deleteCmd.Parameters.AddWithValue("@page", pageNumber);
        await deleteCmd.ExecuteNonQueryAsync();

        return true;
    }

    /// <summary>
    /// 批量平移消息
    /// </summary>
    public async Task<List<int>> BatchMoveMessagesAsync(string roomId, string channelId, List<(int page, int seq)> messages, int targetPage, int targetSeq)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var affectedPages = new HashSet<int>();
        var currentSeq = targetSeq;

        foreach (var (page, seq) in messages)
        {
            // 更新消息位置
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE channel_{channelId} SET page_number=@newPage, in_page_seq=@newSeq WHERE page_number=@oldPage AND in_page_seq=@oldSeq";
            cmd.Parameters.AddWithValue("@newPage", targetPage);
            cmd.Parameters.AddWithValue("@newSeq", currentSeq);
            cmd.Parameters.AddWithValue("@oldPage", page);
            cmd.Parameters.AddWithValue("@oldSeq", seq);
            await cmd.ExecuteNonQueryAsync();

            affectedPages.Add(page);
            affectedPages.Add(targetPage);
            currentSeq++;
        }

        // 更新所有受影响分页的修改时间
        foreach (var page in affectedPages)
        {
            await UpdatePageMetadataAsync(conn, channelId, page);
        }

        return affectedPages.ToList();
    }

    /// <summary>
    /// 批量删除消息
    /// </summary>
    public async Task<List<int>> BatchDeleteMessagesAsync(string roomId, string channelId, List<(int page, int seq)> messages)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var affectedPages = new HashSet<int>();

        foreach (var (page, seq) in messages)
        {
            // 删除消息
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM channel_{channelId} WHERE page_number=@page AND in_page_seq=@seq";
            cmd.Parameters.AddWithValue("@page", page);
            cmd.Parameters.AddWithValue("@seq", seq);
            await cmd.ExecuteNonQueryAsync();

            affectedPages.Add(page);
        }

        // 更新所有受影响分页的修改时间
        foreach (var page in affectedPages)
        {
            await UpdatePageMetadataAsync(conn, channelId, page);
        }

        return affectedPages.ToList();
    }

    /// <summary>
    /// 更新分页修改时间
    /// </summary>
    private async Task UpdatePageMetadataAsync(SqliteConnection conn, string channelId, int pageNumber)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO page_metadata (channel_id, page_number, last_modified)
            VALUES (@channel, @page, @time)";
        cmd.Parameters.AddWithValue("@channel", channelId);
        cmd.Parameters.AddWithValue("@page", pageNumber);
        cmd.Parameters.AddWithValue("@time", DateTimeOffset.Now.ToUnixTimeMilliseconds());

        await cmd.ExecuteNonQueryAsync();
    }

    private string GetDbPath(string roomId, DateTime sendTime)
    {
        var month = sendTime.ToString("yyyy-MM");
        var dir = Path.Combine(_basePath, "rooms", roomId, "messages");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{month}.db");
    }

    private void EnsureDatabase(string dbPath, string channelId)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // 创建频道消息表
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS channel_{channelId} (
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

        // 创建时间戳索引
        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS idx_time ON channel_{channelId}(send_time_ms)";
        cmd.ExecuteNonQuery();

        // 创建分页元数据表
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS page_metadata (
                channel_id TEXT,
                page_number INTEGER,
                last_modified INTEGER,
                PRIMARY KEY (channel_id, page_number)
            )";
        cmd.ExecuteNonQuery();
    }

    public async Task<List<GroupMessage>> SearchMessagesAsync(string roomId, string channelId, string keyword, int limit = 50)
    {
        var dbPath = GetDbPath(roomId, DateTime.Now);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT * FROM channel_{channelId}
            WHERE content LIKE @keyword
            ORDER BY page_number DESC, in_page_seq DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
        cmd.Parameters.AddWithValue("@limit", limit);

        var messages = new List<GroupMessage>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new GroupMessage
            {
                PageNumber = reader.GetInt32(0),
                InPageSeq = reader.GetInt32(1),
                SendTime = new DateTime(reader.GetInt64(2)),
                SenderId = reader.GetString(3),
                Content = reader.GetString(4)
            });
        }

        return messages;
    }
}

