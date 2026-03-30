using System;

namespace MasterIM.Models;

public class GroupMember
{
    public string UserId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Role { get; set; } = "member";  // owner, admin, member
    public DateTime JoinTime { get; set; } = DateTime.UtcNow;
    public string? Nickname { get; set; }
}
