using System;
using System.Collections.Generic;

namespace MasterIM.Models;

public class Packet
{
    public string T { get; set; } = string.Empty;
    public object? P { get; set; }
    public string? Id { get; set; }
}
