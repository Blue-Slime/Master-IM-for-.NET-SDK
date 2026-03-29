using System;
using System.Collections.Generic;

namespace MasterIM.SDK.Protocol;

public class Packet
{
    public string T { get; set; } = string.Empty; // msg|qry|stm|ntf
    public object? P { get; set; }
    public string? Id { get; set; }
}
