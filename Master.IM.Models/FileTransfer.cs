using System;

namespace MasterIM.Models;

public class FileTransfer
{
    public string FileId { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;  // image, audio, video, file
    public string? ThumbnailUrl { get; set; }
    public DateTime UploadTime { get; set; } = DateTime.UtcNow;
    public string UploaderId { get; set; } = string.Empty;
}
