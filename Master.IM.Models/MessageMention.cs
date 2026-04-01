using System.Collections.Generic;

namespace MasterIM.Models;

public class MessageMention
{
    public List<string> MentionedUserIds { get; set; } = new();
    public bool MentionAll { get; set; } = false;
}
