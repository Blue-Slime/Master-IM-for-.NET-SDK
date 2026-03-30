using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class DMCleanupService
{
    private readonly string _basePath;

    public DMCleanupService(string basePath)
    {
        _basePath = basePath;
    }

    public async Task CleanupExpiredMessagesAsync()
    {
        var dmPath = Path.Combine(_basePath, "dm");
        if (!Directory.Exists(dmPath)) return;

        foreach (var pairDir in Directory.GetDirectories(dmPath))
        {
            var configPath = Path.Combine(pairDir, "config.json");
            if (!File.Exists(configPath)) continue;

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<DMConfig>(json);
            if (config == null || config.RetentionDays < 0) continue;

            await DeleteExpiredDatabasesAsync(pairDir, config);
        }
    }

    private async Task DeleteExpiredDatabasesAsync(string pairDir, DMConfig config)
    {
        var messagesDir = Path.Combine(pairDir, "messages");
        if (!Directory.Exists(messagesDir)) return;

        var cutoffDate = DateTime.Now.AddDays(-config.RetentionDays);
        var currentMonth = DateTime.Now.ToString("yyyy-MM");

        foreach (var dbFile in Directory.GetFiles(messagesDir, "*.db"))
        {
            var fileName = Path.GetFileNameWithoutExtension(dbFile);

            // 保留当前月份
            if (fileName == currentMonth) continue;

            if (DateTime.TryParseExact(fileName, "yyyy-MM", null,
                System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                if (fileDate < cutoffDate)
                {
                    File.Delete(dbFile);
                    await Task.CompletedTask;
                }
            }
        }
    }
}

