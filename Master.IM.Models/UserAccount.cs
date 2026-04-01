using System;

namespace MasterIM.Models;

/// <summary>
/// 用户账户信息（全局中心库）
/// </summary>
public class UserAccount
{
    public string UserId { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// 会员等级: free, plus, pro, gm
    /// </summary>
    public string MembershipTier { get; set; } = "free";

    /// <summary>
    /// 订阅开始日期
    /// </summary>
    public DateTime? SubscriptionStartDate { get; set; }

    /// <summary>
    /// 订阅结束日期
    /// </summary>
    public DateTime? SubscriptionEndDate { get; set; }

    /// <summary>
    /// 订阅状态: active, expired, cancelled, trial
    /// </summary>
    public string SubscriptionStatus { get; set; } = "free";

    /// <summary>
    /// 账户创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 账户状态: active, suspended, banned
    /// </summary>
    public string AccountStatus { get; set; } = "active";
}
