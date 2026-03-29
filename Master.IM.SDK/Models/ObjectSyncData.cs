using System;
using System.Collections.Generic;

namespace MasterIM.SDK.Models;

public class ObjectSyncData
{
    public string Action { get; set; } = string.Empty;
    public GameObject? Object { get; set; }
    public string? ObjectId { get; set; }
    public long SequenceNumber { get; set; }
}
