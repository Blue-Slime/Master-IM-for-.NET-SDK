using System;
using System.Collections.Generic;

namespace MasterIM.Models;

public class PlayerInfo
{
    // 基础信息
    public string UserId { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;  // 个人简介

    // 状态信息
    public string Status { get; set; } = "offline";  // online, away, busy, offline
    public string StatusText { get; set; } = string.Empty;  // 自定义状态文本
    public string StatusEmoji { get; set; } = string.Empty;  // 状态表情

    // 房间信息
    public string CurrentRoomId { get; set; } = string.Empty;
    public string CurrentChannelId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;  // 房间内昵称
    public string Role { get; set; } = "member";  // owner, gm, player, observer, member

    // 时间信息
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 扩展信息
    public Dictionary<string, object> CustomFields { get; set; } = new();  // 自定义字段
}
