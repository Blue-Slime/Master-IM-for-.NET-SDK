using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MasterIM.Server.Storage;

/// <summary>
/// 游戏对象存储 - 支持动态类型表
/// </summary>
public class ObjectStore
{
    private readonly string _basePath;
    private readonly Dictionary<string, bool> _registeredTypes = new();

    public ObjectStore(string basePath)
    {
        _basePath = basePath;
    }

    /// <summary>
    /// 保存游戏对象
    /// </summary>
    public async Task<long> SaveAsync(string roomId, GameObject obj)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 确保类型已注册
        await EnsureTypeRegisteredAsync(conn, obj.Type);

        // 分配序列号
        var seqNumber = await GetNextSequenceAsync(conn, roomId);
        obj.SequenceNumber = seqNumber;
        obj.UpdatedAt = DateTime.UtcNow;

        // 序列化对象
        var data = JsonSerializer.SerializeToUtf8Bytes(obj);

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO GameObjects
            (Id, Type, Name, SequenceNumber, RoomId, ChannelId, CreatedAt, UpdatedAt,
             CreatorId, OwnerId, ParentId, IsDeleted, Version, Data)
            VALUES (@id, @type, @name, @seq, @room, @channel, @created, @updated,
                    @creator, @owner, @parent, @deleted, @version, @data)";

        cmd.Parameters.AddWithValue("@id", obj.Id);
        cmd.Parameters.AddWithValue("@type", obj.Type);
        cmd.Parameters.AddWithValue("@name", obj.Name);
        cmd.Parameters.AddWithValue("@seq", seqNumber);
        cmd.Parameters.AddWithValue("@room", obj.RoomId ?? roomId);
        cmd.Parameters.AddWithValue("@channel", (object?)obj.ChannelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", obj.CreatedAt.Ticks);
        cmd.Parameters.AddWithValue("@updated", obj.UpdatedAt.Ticks);
        cmd.Parameters.AddWithValue("@creator", (object?)obj.CreatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@owner", (object?)obj.OwnerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@parent", (object?)obj.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@deleted", obj.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@version", obj.Version);
        cmd.Parameters.AddWithValue("@data", data);

        await cmd.ExecuteNonQueryAsync();
        return seqNumber;
    }

    /// <summary>
    /// 获取对象
    /// </summary>
    public async Task<GameObject?> GetAsync(string roomId, string objectId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return null;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Data FROM GameObjects WHERE Id=@id AND IsDeleted=0";
        cmd.Parameters.AddWithValue("@id", objectId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var data = (byte[])reader["Data"];
            return JsonSerializer.Deserialize<GameObject>(data);
        }

        return null;
    }

    /// <summary>
    /// 按类型查询对象
    /// </summary>
    public async Task<List<GameObject>> GetByTypeAsync(string roomId, string type, int limit = 100)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Data FROM GameObjects
            WHERE RoomId=@room AND Type=@type AND IsDeleted=0
            ORDER BY SequenceNumber DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@room", roomId);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@limit", limit);

        var objects = new List<GameObject>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var data = (byte[])reader["Data"];
            var obj = JsonSerializer.Deserialize<GameObject>(data);
            if (obj != null) objects.Add(obj);
        }

        return objects;
    }

    /// <summary>
    /// 按序列号范围查询（用于同步）
    /// </summary>
    public async Task<List<GameObject>> GetBySequenceRangeAsync(string roomId, long startSeq, long endSeq)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Data FROM GameObjects
            WHERE RoomId=@room AND SequenceNumber >= @start AND SequenceNumber <= @end
            ORDER BY SequenceNumber ASC";
        cmd.Parameters.AddWithValue("@room", roomId);
        cmd.Parameters.AddWithValue("@start", startSeq);
        cmd.Parameters.AddWithValue("@end", endSeq);

        var objects = new List<GameObject>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var data = (byte[])reader["Data"];
            var obj = JsonSerializer.Deserialize<GameObject>(data);
            if (obj != null) objects.Add(obj);
        }

        return objects;
    }

    /// <summary>
    /// 删除对象（软删除）
    /// </summary>
    public async Task<bool> DeleteAsync(string roomId, string objectId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return false;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE GameObjects SET IsDeleted=1, UpdatedAt=@time WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", objectId);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.Ticks);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// 获取最大序列号
    /// </summary>
    public async Task<long> GetMaxSequenceAsync(string roomId)
    {
        var dbPath = GetDbPath(roomId);
        if (!File.Exists(dbPath)) return 0;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(SequenceNumber) FROM GameObjects WHERE RoomId=@room";
        cmd.Parameters.AddWithValue("@room", roomId);

        var result = await cmd.ExecuteScalarAsync();
        return result != DBNull.Value ? Convert.ToInt64(result) : 0;
    }

    private async Task<long> GetNextSequenceAsync(SqliteConnection conn, string roomId)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SequenceCounters (RoomId, ChannelId, CurrentValue)
            VALUES (@room, '', 1)
            ON CONFLICT(RoomId, ChannelId) DO UPDATE SET CurrentValue = CurrentValue + 1
            RETURNING CurrentValue";
        cmd.Parameters.AddWithValue("@room", roomId);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task EnsureTypeRegisteredAsync(SqliteConnection conn, string type)
    {
        if (_registeredTypes.ContainsKey(type)) return;

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO ObjectTypes (TypeName, TableName, HasCustomTable, CreatedAt)
            VALUES (@type, @table, 0, @time)";
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@table", $"obj_{type}");
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.Ticks);

        await cmd.ExecuteNonQueryAsync();
        _registeredTypes[type] = true;
    }

    private string GetDbPath(string roomId)
    {
        var dir = Path.Combine(_basePath, "rooms", roomId, "objects");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "objects.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // 主表
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS GameObjects (
                Id TEXT PRIMARY KEY,
                Type TEXT NOT NULL,
                Name TEXT NOT NULL,
                SequenceNumber INTEGER NOT NULL,
                RoomId TEXT,
                ChannelId TEXT,
                CreatedAt INTEGER NOT NULL,
                UpdatedAt INTEGER NOT NULL,
                CreatorId TEXT,
                OwnerId TEXT,
                ParentId TEXT,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                Data BLOB NOT NULL
            )";
        cmd.ExecuteNonQuery();

        // 索引
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_objects_sequence ON GameObjects(SequenceNumber)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_objects_type ON GameObjects(Type)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_objects_room ON GameObjects(RoomId)";
        cmd.ExecuteNonQuery();

        // 序列号生成器
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS SequenceCounters (
                RoomId TEXT NOT NULL,
                ChannelId TEXT NOT NULL,
                CurrentValue INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (RoomId, ChannelId)
            )";
        cmd.ExecuteNonQuery();

        // 类型注册表
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ObjectTypes (
                TypeName TEXT PRIMARY KEY,
                TableName TEXT NOT NULL,
                HasCustomTable INTEGER NOT NULL,
                CreatedAt INTEGER NOT NULL,
                Schema TEXT
            )";
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// 游戏对象基类
/// </summary>
public class GameObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public string? RoomId { get; set; }
    public string? ChannelId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatorId { get; set; }
    public string? OwnerId { get; set; }
    public string? ParentId { get; set; }
    public bool IsDeleted { get; set; }
    public int Version { get; set; } = 1;
    public Dictionary<string, object> Properties { get; set; } = new();
}
