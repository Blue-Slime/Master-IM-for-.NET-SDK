using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MasterIM.Server.Services;

public class DataExportService
{
    private readonly string _dataRoot;

    public DataExportService(string dataRoot = "./data")
    {
        _dataRoot = dataRoot;
    }

    public async Task<string> ExportRoomDataAsync(string roomId)
    {
        var roomDir = Path.Combine(_dataRoot, "rooms", roomId);
        if (!Directory.Exists(roomDir))
            throw new DirectoryNotFoundException($"Room {roomId} not found");

        var exportDir = Path.Combine(_dataRoot, "exports");
        Directory.CreateDirectory(exportDir);

        var zipPath = Path.Combine(exportDir, $"{roomId}_{DateTime.Now:yyyyMMddHHmmss}.zip");

        await Task.Run(() => ZipFile.CreateFromDirectory(roomDir, zipPath));

        return zipPath;
    }

    public async Task<string> ExportMessagesAsync(string roomId, string channelId)
    {
        var messagesDir = Path.Combine(_dataRoot, "rooms", roomId, "messages");
        if (!Directory.Exists(messagesDir))
            throw new DirectoryNotFoundException($"Messages not found");

        var exportDir = Path.Combine(_dataRoot, "exports");
        Directory.CreateDirectory(exportDir);

        var zipPath = Path.Combine(exportDir, $"{roomId}_{channelId}_messages_{DateTime.Now:yyyyMMddHHmmss}.zip");

        await Task.Run(() => ZipFile.CreateFromDirectory(messagesDir, zipPath));

        return zipPath;
    }

    public void DeleteRoomData(string roomId)
    {
        var roomDir = Path.Combine(_dataRoot, "rooms", roomId);
        if (Directory.Exists(roomDir))
        {
            Directory.Delete(roomDir, true);
        }
    }
}
