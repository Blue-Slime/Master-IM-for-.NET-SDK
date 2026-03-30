using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Services;

public class FileService
{
    private readonly string _dataRoot;

    public FileService(string dataRoot = "./data")
    {
        _dataRoot = dataRoot;
    }

    public async Task<FileTransfer> SaveFileAsync(string roomId, Stream fileStream, string fileName, string uploaderId)
    {
        var fileId = Guid.NewGuid().ToString();
        var ext = Path.GetExtension(fileName);
        var fileType = GetFileType(ext);

        var filesDir = Path.Combine(_dataRoot, "rooms", roomId, "files");
        Directory.CreateDirectory(filesDir);

        var filePath = Path.Combine(filesDir, $"{fileId}{ext}");

        using var fs = File.Create(filePath);
        await fileStream.CopyToAsync(fs);

        var fileInfo = new FileInfo(filePath);
        var fileTransfer = new FileTransfer
        {
            FileId = fileId,
            FileName = fileName,
            FileSize = fileInfo.Length,
            FileType = fileType,
            UploaderId = uploaderId,
            UploadTime = DateTime.UtcNow
        };

        await SaveFileMetadataAsync(roomId, fileTransfer);
        return fileTransfer;
    }

    private async Task SaveFileMetadataAsync(string roomId, FileTransfer file)
    {
        var dbPath = Path.Combine(_dataRoot, "rooms", roomId, "files", "files.db");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var createTable = conn.CreateCommand();
        createTable.CommandText = @"
            CREATE TABLE IF NOT EXISTS Files (
                FileId TEXT PRIMARY KEY,
                FileName TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                FileType TEXT NOT NULL,
                ThumbnailUrl TEXT,
                UploadTime TEXT NOT NULL,
                UploaderId TEXT NOT NULL
            )";
        await createTable.ExecuteNonQueryAsync();

        var insert = conn.CreateCommand();
        insert.CommandText = @"
            INSERT INTO Files (FileId, FileName, FileSize, FileType, ThumbnailUrl, UploadTime, UploaderId)
            VALUES (@id, @name, @size, @type, @thumb, @time, @uploader)";
        insert.Parameters.AddWithValue("@id", file.FileId);
        insert.Parameters.AddWithValue("@name", file.FileName);
        insert.Parameters.AddWithValue("@size", file.FileSize);
        insert.Parameters.AddWithValue("@type", file.FileType);
        insert.Parameters.AddWithValue("@thumb", file.ThumbnailUrl ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("@time", file.UploadTime.ToString("O"));
        insert.Parameters.AddWithValue("@uploader", file.UploaderId);
        await insert.ExecuteNonQueryAsync();
    }

    public string GetFilePath(string roomId, string fileId, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return Path.Combine(_dataRoot, "rooms", roomId, "files", $"{fileId}{ext}");
    }

    private string GetFileType(string ext)
    {
        return ext.ToLower() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "image",
            ".mp3" or ".wav" or ".ogg" or ".m4a" => "audio",
            ".mp4" or ".avi" or ".mov" or ".webm" => "video",
            _ => "file"
        };
    }
}
