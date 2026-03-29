using System;
using System.Collections.Generic;

namespace MasterIM.SDK.Models;

public class DMMessage
{
    public string SenderId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public object? Data { get; set; }
    public long Timestamp { get; set; }
}
