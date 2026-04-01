using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 服务器地址列表库（全局配置）
/// </summary>
public class ServerConfigStore
{
    private readonly string _basePath;

    public ServerConfigStore(string basePath)
    {
        _basePath = basePath;
    }

    private string GetDbPath()
    {
        Directory.CreateDirectory(_basePath);
        return Path.Combine(_basePath, "server_configs.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ServerConfigs (
                ServerId TEXT PRIMARY KEY,
                ServerName TEXT NOT NULL,
                Host TEXT NOT NULL,
                Port INTEGER NOT NULL,
                ServerType TEXT NOT NULL,
                Status TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_type ON ServerConfigs(ServerType)";
        cmd.ExecuteNonQuery();
    }

    public async Task SaveServerAsync(ServerConfig server)
    {
        var dbPath = GetDbPath();
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO ServerConfigs
            (ServerId, ServerName, Host, Port, ServerType, Status, UpdatedAt)
            VALUES (@id, @name, @host, @port, @type, @status, @updated)";

        cmd.Parameters.AddWithValue("@id", server.ServerId);
        cmd.Parameters.AddWithValue("@name", server.ServerName);
        cmd.Parameters.AddWithValue("@host", server.Host);
        cmd.Parameters.AddWithValue("@port", server.Port);
        cmd.Parameters.AddWithValue("@type", server.ServerType);
        cmd.Parameters.AddWithValue("@status", server.Status);
        cmd.Parameters.AddWithValue("@updated", server.UpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ServerConfig>> GetServersByTypeAsync(string serverType)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ServerConfigs WHERE ServerType=@type AND Status='online'";
        cmd.Parameters.AddWithValue("@type", serverType);

        var servers = new List<ServerConfig>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            servers.Add(new ServerConfig
            {
                ServerId = reader.GetString(0),
                ServerName = reader.GetString(1),
                Host = reader.GetString(2),
                Port = reader.GetInt32(3),
                ServerType = reader.GetString(4),
                Status = reader.GetString(5),
                UpdatedAt = DateTime.Parse(reader.GetString(6))
            });
        }

        return servers;
    }

    public async Task<List<ServerConfig>> GetAllServersAsync()
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ServerConfigs WHERE Status='online'";

        var servers = new List<ServerConfig>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            servers.Add(new ServerConfig
            {
                ServerId = reader.GetString(0),
                ServerName = reader.GetString(1),
                Host = reader.GetString(2),
                Port = reader.GetInt32(3),
                ServerType = reader.GetString(4),
                Status = reader.GetString(5),
                UpdatedAt = DateTime.Parse(reader.GetString(6))
            });
        }

        return servers;
    }
}
