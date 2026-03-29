# Master IM 实现

## 项目结构

```
Master.IM.SDK/          # 客户端SDK
├── Models/
│   ├── Message.cs
│   ├── StreamData.cs
│   └── UpdateNotification.cs
├── Protocol/
│   └── Packet.cs
└── IMClient.cs

Master.IM.Server/       # 服务端
├── Models/
│   ├── Message.cs
│   └── Packet.cs
├── Storage/
│   └── MessageStore.cs
├── WebSocket/
│   ├── Connection.cs
│   ├── ConnectionManager.cs
│   └── IMServer.cs
└── Program.cs
```

## 存储结构

**按真实月分库 + 频道分表 + 时间戳主键**:
```
/rooms/room_001/
  ├── data/messages/
  │   ├── 2026-01.db
  │   ├── 2026-02.db
  │   └── ...
  └── objects/
      ├── objects.db      # 游戏对象数据库
      └── files/          # 大文件存储
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

## 运行

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

await client.SendMessageAsync(new Message {
    Content = "Hello",
    SendTime = DateTime.Now,
    SenderId = "user1",
    PageNumber = 0,
    InPageSeq = 0
});

// 查询最新100条
var messages = await client.QueryPageAsync(
    lastPage: int.MaxValue,
    lastSeq: int.MaxValue,
    limit: 100
);

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

✅ WebSocket 连接管理
✅ 消息发送和接收
✅ 分页查询
✅ 流式同步(鼠标/绘制)
✅ 心跳检测
✅ 自动重连(客户端)
✅ 按真实月分库(YYYY-MM)
✅ 频道分表(库内多表)
✅ 页号主键(page_number, in_page_seq)
✅ 顺序查询(主键即排序)
✅ 时间戳索引(回复导航)
✅ 超时空编辑(插入历史消息)
✅ 回复溯源(时间戳导航)
✅ 离线同步(分页修改时间)
✅ 分页管理(创建/删除空白分页)
✅ 更新通知机制
✅ 游戏对象存储
✅ 单聊功能(临时通信)
✅ 高级单聊(可选存储/漫游/编辑)

## 待实现功能

- 客户端本地缓存

