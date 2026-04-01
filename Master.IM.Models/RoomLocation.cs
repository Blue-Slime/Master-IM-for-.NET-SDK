using System;

namespace MasterIM.Models;

/// <summary>
/// 房间位置记录
/// </summary>
public class RoomLocation
{
    public string RoomId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
