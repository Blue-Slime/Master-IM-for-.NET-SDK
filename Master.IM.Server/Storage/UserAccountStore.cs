using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 用户账户中心存储（全局非房间级别）
/// </summary>
public class UserAccountStore
{
    private readonly string _basePath;

    public UserAccountStore(string basePath)
    {
        _basePath = basePath;
    }

    private string GetDbPath()
    {
        Directory.CreateDirectory(_basePath);
        return Path.Combine(_basePath, "users.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS UserAccounts (
                UserId TEXT PRIMARY KEY,
                SteamId TEXT NOT NULL UNIQUE,
                UserName TEXT NOT NULL,
                Email TEXT,
                AvatarUrl TEXT,
                MembershipTier INTEGER NOT NULL DEFAULT 0,
                SubscriptionStartDate TEXT,
                SubscriptionEndDate TEXT,
                SubscriptionStatus TEXT NOT NULL DEFAULT 'free',
                CreatedAt TEXT NOT NULL,
                LastLoginAt TEXT NOT NULL,
                AccountStatus TEXT NOT NULL DEFAULT 'active'
            )";
        cmd.ExecuteNonQuery();

        // 创建索引
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_steamid ON UserAccounts(SteamId)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_membership ON UserAccounts(MembershipTier)";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 创建或更新用户账户
    /// </summary>
    public async Task SaveUserAsync(UserAccount user)
    {
        var dbPath = GetDbPath();
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO UserAccounts
            (UserId, SteamId, UserName, Email, AvatarUrl, MembershipTier,
             SubscriptionStartDate, SubscriptionEndDate, SubscriptionStatus,
             CreatedAt, LastLoginAt, AccountStatus)
            VALUES (@userId, @steamId, @userName, @email, @avatar, @tier,
                    @startDate, @endDate, @subStatus, @created, @lastLogin, @status)";

        cmd.Parameters.AddWithValue("@userId", user.UserId);
        cmd.Parameters.AddWithValue("@steamId", user.SteamId);
        cmd.Parameters.AddWithValue("@userName", user.UserName);
        cmd.Parameters.AddWithValue("@email", user.Email ?? "");
        cmd.Parameters.AddWithValue("@avatar", user.AvatarUrl ?? "");
        cmd.Parameters.AddWithValue("@tier", user.MembershipTier);
        cmd.Parameters.AddWithValue("@startDate", user.SubscriptionStartDate?.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@endDate", user.SubscriptionEndDate?.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@subStatus", user.SubscriptionStatus);
        cmd.Parameters.AddWithValue("@created", user.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastLogin", user.LastLoginAt.ToString("O"));
        cmd.Parameters.AddWithValue("@status", user.AccountStatus);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 根据UserId获取用户
    /// </summary>
    public async Task<UserAccount?> GetUserByIdAsync(string userId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return null;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM UserAccounts WHERE UserId=@userId";
        cmd.Parameters.AddWithValue("@userId", userId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadUser(reader);
        }

        return null;
    }

    /// <summary>
    /// 更新最后登录时间
    /// </summary>
    public async Task UpdateLastLoginAsync(string userId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE UserAccounts SET LastLoginAt=@time WHERE UserId=@userId";
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 更新会员等级
    /// </summary>
    public async Task UpdateMembershipAsync(string userId, int tier, DateTime? startDate, DateTime? endDate, string status)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE UserAccounts
            SET MembershipTier=@tier, SubscriptionStartDate=@start,
                SubscriptionEndDate=@end, SubscriptionStatus=@status
            WHERE UserId=@userId";
        cmd.Parameters.AddWithValue("@tier", tier);
        cmd.Parameters.AddWithValue("@start", startDate?.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@end", endDate?.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 根据SteamId获取用户
    /// </summary>
    public async Task<UserAccount?> GetUserBySteamIdAsync(string steamId)
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return null;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM UserAccounts WHERE SteamId=@steamId";
        cmd.Parameters.AddWithValue("@steamId", steamId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadUser(reader);
        }

        return null;
    }

    /// <summary>
    /// 获取所有会员用户
    /// </summary>
    public async Task<List<UserAccount>> GetMemberUsersAsync()
    {
        var dbPath = GetDbPath();
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM UserAccounts WHERE MembershipTier > 0";

        var users = new List<UserAccount>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    private UserAccount ReadUser(SqliteDataReader reader)
    {
        return new UserAccount
        {
            UserId = reader.GetString(0),
            SteamId = reader.GetString(1),
            UserName = reader.GetString(2),
            Email = reader.GetString(3),
            AvatarUrl = reader.GetString(4),
            MembershipTier = reader.GetInt32(5),
            SubscriptionStartDate = string.IsNullOrEmpty(reader.GetString(6)) ? null : DateTime.Parse(reader.GetString(6)),
            SubscriptionEndDate = string.IsNullOrEmpty(reader.GetString(7)) ? null : DateTime.Parse(reader.GetString(7)),
            SubscriptionStatus = reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9)),
            LastLoginAt = DateTime.Parse(reader.GetString(10)),
            AccountStatus = reader.GetString(11)
        };
    }
}
