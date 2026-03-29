using System;
using System.Collections.Generic;

namespace MasterIM.SDK.Models;

public class GameObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public string? RoomId { get; set; }
    public string? ChannelId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatorId { get; set; }
    public string? OwnerId { get; set; }
    public string? ParentId { get; set; }
    public bool IsDeleted { get; set; }
    public int Version { get; set; } = 1;
    public Dictionary<string, object> Properties { get; set; } = new();
}
