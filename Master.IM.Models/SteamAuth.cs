using System;

namespace MasterIM.Models;

/// <summary>
/// Steam认证票据
/// </summary>
public class SteamAuthTicket
{
    public string Ticket { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Steam认证响应
/// </summary>
public class SteamAuthResponse
{
    public bool Success { get; set; }
    public string SteamId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
