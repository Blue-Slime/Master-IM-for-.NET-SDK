using System;
using System.Collections.Generic;

namespace MasterIM.Models;

public class StreamData
{
    public string Type { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; }
}
