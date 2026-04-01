using System;

namespace MasterIM.Models;

/// <summary>
/// 服务器配置信息
/// </summary>
public class ServerConfig
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }

    /// <summary>
    /// 服务器类型: auth, storage, backup, hybrid
    /// </summary>
    public string ServerType { get; set; } = "hybrid";

    /// <summary>
    /// 服务器状态: online, offline, maintenance
    /// </summary>
    public string Status { get; set; } = "online";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
