using System;

namespace MasterIM.Models;

public class Channel
{
    public string ChannelId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
