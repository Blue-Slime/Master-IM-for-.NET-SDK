using System;

namespace MasterIM.Models;

public class RoomMember
{
    public string RoomId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;  // 房间内昵称
    public string Role { get; set; } = "member";  // owner, gm, player, observer, member
    public string AccessStatus { get; set; } = "allowed";  // allowed, banned
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
}
