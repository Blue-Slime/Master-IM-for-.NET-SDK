using System;
using System.Collections.Generic;

namespace MasterIM.Models;

public class GroupTipsEvent
{
    public string Type { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public DateTime Time { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Data { get; set; } = new();
}
