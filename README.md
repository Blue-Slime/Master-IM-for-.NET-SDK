# Master IM - 跑团即时通讯系统

基于 .NET 8.0 的 WebSocket 即时通讯系统，专为 TRPG 跑团场景设计。

## 特性

- 🎲 **跑团专用** - 骰子投掷、角色扮演、游戏对象同步
- ⏰ **超时空编辑** - 历史消息插入、批量移动、精准修改
- 📁 **智能存储** - 月度分库、智能外部化、覆盖更新
- 🔄 **实时同步** - 在线状态、正在输入、流式数据
- 📦 **文件传输** - 解耦上传、自动消息、进度回调
- 👥 **房间管理** - 创建/删除房间、成员管理、权限控制
- 🔍 **消息搜索** - 关键词搜索、@提及通知
- ✅ **已读回执** - 消息已读状态同步
- 🤝 **好友邀请** - Steam好友邀请、加入请求

## 项目结构

```
Master.IM/
├── Master.IM.Models/      # 共享数据模型
├── Master.IM.Server/      # WebSocket服务端
├── Master.IM.SDK/         # 客户端SDK
└── Master.IM.Test/        # 单元测试
```

## 存储结构

```
/data/
├── users.db                 # 用户中心库（全局）
└── /rooms/{roomId}/
    ├── messages/
    │   ├── 2026-01.db
    │   ├── 2026-02.db
    │   └── 2026-03.db
    ├── objects/
    │   ├── objects.db
    │   ├── {objId}.obj      # 大型对象外部化
    │   └── ...
    ├── files/
    │   ├── files.db         # 文件元数据
    │   ├── {fileId}.jpg
    │   └── {fileId}.pdf
    └── members.db           # 房间成员信息
```

**用户中心表结构**:
```sql
CREATE TABLE UserAccounts (
    UserId TEXT PRIMARY KEY,
    SteamId TEXT NOT NULL UNIQUE,
    UserName TEXT NOT NULL,
    Email TEXT,
    AvatarUrl TEXT,
    MembershipTier INTEGER NOT NULL DEFAULT 0,  -- 0=free, 1=standard, 2=plus, 3=pro, 4=god
    SubscriptionStartDate TEXT,
    SubscriptionEndDate TEXT,
    SubscriptionStatus TEXT NOT NULL DEFAULT 'free',
    CreatedAt TEXT NOT NULL,
    LastLoginAt TEXT NOT NULL,
    AccountStatus TEXT NOT NULL DEFAULT 'active'
);
CREATE INDEX idx_steamid ON UserAccounts(SteamId);
CREATE INDEX idx_membership ON UserAccounts(MembershipTier);
```

**消息表结构**:
```sql
CREATE TABLE channel_{channelId} (
    page_number INTEGER,
    in_page_seq INTEGER,
    send_time_ms INTEGER,
    sender_id TEXT,
    content TEXT,
    reply_to_time_ms INTEGER,
    quoted_content TEXT,
    PRIMARY KEY (page_number, in_page_seq)
)
CREATE INDEX idx_time ON channel_{channelId}(send_time_ms);
```

**游戏对象表结构**:
```sql
CREATE TABLE GameObjects (
    Id TEXT PRIMARY KEY,
    Type TEXT NOT NULL,
    Name TEXT NOT NULL,
    SequenceNumber INTEGER NOT NULL,
    RoomId TEXT,
    ChannelId TEXT,
    CreatedAt INTEGER NOT NULL,
    UpdatedAt INTEGER NOT NULL,
    CreatorId TEXT,
    OwnerId TEXT,
    ParentId TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    Version INTEGER NOT NULL DEFAULT 1,
    Data BLOB NOT NULL
);
CREATE INDEX idx_objects_sequence ON GameObjects(SequenceNumber);
CREATE INDEX idx_objects_type ON GameObjects(Type);
CREATE INDEX idx_objects_room ON GameObjects(RoomId);
```

## 快速开始

**服务端**:
```bash
cd Master.IM.Server
dotnet run
```

**客户端示例**:
```csharp
var client = new IMClient();
await client.ConnectAsync("ws://localhost:5000/ws", "user1", "room1", "lobby");

client.OnMessageReceived += msg => Console.WriteLine($"收到: {msg.Content}");

// 发送消息
await client.SendMessageAsync(new GroupMessage {
    Content = "Hello",
    MessageType = "text"
});

// 角色扮演
await client.SendMessageAsync(new GroupMessage {
    Content = "我要攻击！",
    RoleId = "character-123"
});

// 在线状态
await client.SendPresenceAsync("online");

// 正在输入
await client.SendTypingAsync(true);

// 骰子投掷
await client.SendDiceRollAsync("1d20+5", "18", isSecret: false);

// 房间邀请
await client.SendRoomInviteAsync("targetUserId");
await client.AcceptInviteAsync("inviterId");

// 房间管理
await client.CreateRoomAsync(new Room { RoomId = "room1", RoomName = "我的房间" });
var rooms = await client.GetRoomsAsync();
await client.UpdateRoomAsync(room);
await client.DeleteRoomAsync("room1");

// 成员管理
var members = await client.GetRoomMembersAsync();
await client.UpdateMemberAsync(member);
await client.BanMemberAsync("userId");

// 消息搜索
var results = await client.SearchMessagesAsync("关键词", limit: 50);

// 已读回执
await client.SendReadReceiptAsync(pageNumber: 5, inPageSeq: 10);

// @提及通知
client.OnMentioned += msg => Console.WriteLine($"你被@了: {msg.Content}");

// 文件上传
var fileResult = await client.UploadFileAsync("path/to/file.jpg");
await client.SendFileMessageAsync(fileResult);
```

// 创建游戏对象
var character = new GameObject {
    Type = "Character",
    Name = "勇者",
    Properties = new() {
        { "Level", 5 },
        { "HP", 100 }
    }
};
await client.CreateObjectAsync(character);

// 查询对象
var characters = await client.QueryObjectsByTypeAsync("Character");

// 监听对象同步
client.OnObjectSync += sync => {
    Console.WriteLine($"对象同步: {sync.Action} {sync.Object?.Name}");
};

// 单聊
var dmClient = new DMClient();
await dmClient.ConnectAsync("ws://localhost:5000/dm", "user1", "user2");

dmClient.OnMessageReceived += msg => Console.WriteLine($"{msg.SenderId}: {msg.Content}");
dmClient.OnTargetOffline += () => Console.WriteLine("对方已离线");

await dmClient.SendTextAsync("你好");
await dmClient.SendImageAsync("https://example.com/image.jpg", 800, 600);

// 高级单聊（可选存储）
var dmAdvClient = new DMAdvancedClient();
await dmAdvClient.ConnectAsync("ws://localhost:5000/dm_advanced", "user1", "user2", enableStorage: true);

dmAdvClient.OnMessageReceived += msg => Console.WriteLine($"{msg.SenderId}: {msg.Content}");

await dmAdvClient.SendMessageAsync(new Message { Content = "你好", SenderId = "user1" });
var history = await dmAdvClient.QueryPageAsync(int.MaxValue, int.MaxValue, 100);
await dmAdvClient.ModifyMessageAsync(0, 5, "修改后的内容");

// 设置保留期限
await dmAdvClient.ConnectAsync("ws://localhost:5000/dm_advanced", "user1", "user2",
    enableStorage: true,
    retentionDays: 30);  // -1=无限期, 0=不存储, >0=保留天数
```
## 离线同步

**懒加载策略**:
```csharp
// 查看分页时检查是否需要更新
async Task<List<Message>> ViewPage(int pageNumber)
{
    // 1. 检查本地缓存
    if (_cache.TryGetValue(pageNumber, out var cached))
    {
        // 2. 询问服务端该页修改时间
        var serverModified = await client.CheckPageModifiedAsync(pageNumber);

        if (serverModified <= cached.cacheTime)
        {
            // 3. 缓存有效,直接返回
            return cached.messages;
        }
    }

    // 4. 从服务端拉取最新数据
    var messages = await client.QueryPageAsync(pageNumber, int.MaxValue, 100);
    _cache[pageNumber] = (messages, DateTime.Now);
    return messages;
}
```

**元数据表**:
```sql
CREATE TABLE page_metadata (
    channel_id TEXT,
    page_number INTEGER,
    last_modified INTEGER,
    PRIMARY KEY (channel_id, page_number)
)
```

## 分页管理

**创建空白分页**:
```csharp
// 在尾部创建新的空白分页
await client.CreateEmptyPageAsync(pageNumber: 10);
```

**删除空白分页**:
```csharp
// 删除无消息的分页
await client.DeleteEmptyPageAsync(pageNumber: 10);
```

**批量平移消息**:
```csharp
// 将多条消息移动到目标分页
var messages = new List<(int page, int seq)>
{
    (5, 10), (5, 11), (5, 12)
};
await client.BatchMoveMessagesAsync(messages, targetPage: 6, targetSeq: 0);
```

**批量删除消息**:
```csharp
// 删除多条消息
var messages = new List<(int page, int seq)>
{
    (5, 10), (5, 11), (5, 12)
};
await client.BatchDeleteMessagesAsync(messages);
```

**用途**:
- 为超时空编辑预留空间
- 清理未使用的分页

## 已实现功能

✅ **核心通讯**
- WebSocket 连接管理
- 消息发送和接收
- 分页查询
- 心跳检测
- 自动重连（带重试计数）
- 连接状态回调

✅ **跑团特性**
- 骰子投掷（公开/暗骰）
- 角色扮演消息
- 游戏对象同步
- 流式数据（鼠标/绘制）

✅ **实时交互**
- 在线状态（online/away/busy/offline）
- 正在输入指示器
- 群组提示消息

✅ **超时空编辑**
- 历史消息插入
- 消息修改/撤回
- 批量移动/删除
- 分页管理

✅ **文件传输**
- 文件上传/下载
- 进度回调
- 自动发送文件消息

✅ **智能存储**
- 消息月度分库
- GameObject智能外部化
- 覆盖更新机制
- 统一Models架构
- 用户中心库（会员管理）

✅ **房间管理**
- 创建/删除/更新房间
- 房间列表查询
- 房间设置（名称/描述/密码）
- 数据导出功能

✅ **成员管理**
- 获取成员列表
- 更新成员信息
- 踢出/禁言功能
- 角色权限管理

✅ **社交功能**
- 房间邀请系统
- 加入请求处理
- @提及通知
- 消息搜索
- 消息已读回执

## 技术栈

- .NET 8.0
- WebSocket (System.Net.WebSockets)
- SQLite (Microsoft.Data.Sqlite)
- JSON序列化 (System.Text.Json)

## 文档

- [API文档](./API文档/Master_IM_Server_API文档_v1.0.md)
- [功能对比](./功能对比_MasterIM_vs_腾讯IM.md)

## License

MIT