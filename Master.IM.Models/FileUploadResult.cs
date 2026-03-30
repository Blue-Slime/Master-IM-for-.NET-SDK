namespace MasterIM.Models;

public class FileUploadResult
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
