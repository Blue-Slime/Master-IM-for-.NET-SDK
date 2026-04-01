using System;

namespace MasterIM.Models;

public class MessageReceipt
{
    public string UserId { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int InPageSeq { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
