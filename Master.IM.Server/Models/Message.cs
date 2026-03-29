using System;
using System.Collections.Generic;

namespace MasterIM.Server.Models;

public class Message
{
    public int PageNumber { get; set; }
    public int InPageSeq { get; set; }
    public DateTime SendTime { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime? ReplyToTime { get; set; }
    public string? QuotedContent { get; set; }
}
