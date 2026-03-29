using System;
using System.Collections.Generic;

namespace MasterIM.Server.Models;

public class DMConfig
{
    public string PairId { get; set; } = string.Empty;
    public bool EnableStorage { get; set; }
    public bool EnableRoaming { get; set; }
    public bool EnableEdit { get; set; }
    public int RetentionDays { get; set; } = -1;  // -1=无限期, 0=不存储, >0=保留天数
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> Participants { get; set; } = new();
}
