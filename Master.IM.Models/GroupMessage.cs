using System;
using System.Collections.Generic;

namespace MasterIM.Models;

public class GroupMessage
{
    public int PageNumber { get; set; }
    public int InPageSeq { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SendTime { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public DateTime? ReplyToTime { get; set; }
    public string? QuotedContent { get; set; }

    // 角色扮演
    public string? RoleId { get; set; }

    // 消息类型
    public string MessageType { get; set; } = "text";  // text, image, audio, video, file, custom
}
