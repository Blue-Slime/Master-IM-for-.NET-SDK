## 3. 高级单聊客户端 (DMAdvancedClient)

### 3.1 连接管理

#### ConnectAsync
```csharp
Task ConnectAsync(string url, string userId, string targetUserId, bool enableStorage = true, int retentionDays = -1)
```
**用途**: 连接到为高级单聊开放的服务器端点（可选存储/漫游/编辑）

**参数**:
- `url`: WebSocket 服务器地址 (如 `ws://localhost:5000/dm_advanced`)
- `userId`: 当前用户ID
- `targetUserId`: 目标用户ID
- `enableStorage`: 是否存储消息（默认 true）
- `retentionDays`: 保留天数（-1=无限期, 0=不存储, >0=保留天数）

**用法**:
```csharp
var dmAdvClient = new DMAdvancedClient();

// 无限期存储
await dmAdvClient.ConnectAsync("ws://localhost:5000/dm_advanced", "user1", "user2");

// 保留30天
await dmAdvClient.ConnectAsync("ws://localhost:5000/dm_advanced", "user1", "user2",
    enableStorage: true, retentionDays: 30);

// 不存储（临时）
await dmAdvClient.ConnectAsync("ws://localhost:5000/dm_advanced", "user1", "user2",
    enableStorage: false);
```

---

### 3.2 消息操作

#### SendMessageAsync
```csharp
Task SendMessageAsync(Message msg)
```
**用途**: 发送消息

**用法**:
```csharp
await dmAdvClient.SendMessageAsync(new Message {
    Content = "你好",
    SenderId = "user1",
    SendTime = DateTime.Now,
    PageNumber = 0,
    InPageSeq = 0
});
```

#### QueryPageAsync
```csharp
Task<List<Message>> QueryPageAsync(int lastPage, int lastSeq, int limit = 100)
```
**用途**: 查询历史消息（需开启存储）

**用法**:
```csharp
var history = await dmAdvClient.QueryPageAsync(int.MaxValue, int.MaxValue, 100);
```

#### ModifyMessageAsync
```csharp
Task ModifyMessageAsync(int page, int seq, string newContent)
```
**用途**: 修改消息（需开启存储）

**用法**:
```csharp
await dmAdvClient.ModifyMessageAsync(0, 5, "修改后的内容");
```

#### CheckPageModifiedAsync
```csharp
Task<long> CheckPageModifiedAsync(int pageNumber)
```
**用途**: 检查分页修改时间（用于离线同步）

**用法**:
```csharp
var modifiedTime = await dmAdvClient.CheckPageModifiedAsync(5);
```

---

### 3.3 事件

#### OnMessageReceived
```csharp
event Action<Message> OnMessageReceived
```
**用途**: 接收到消息时触发

**用法**:
```csharp
dmAdvClient.OnMessageReceived += msg => {
    Console.WriteLine($"{msg.SenderId}: {msg.Content}");
};
```

---

## 4. 数据模型

### Message
```csharp
public class Message
{
    public int PageNumber { get; set; }
    public int InPageSeq { get; set; }
    public string Content { get; set; }
    public DateTime SendTime { get; set; }
    public string SenderId { get; set; }
    public DateTime? ReplyToTime { get; set; }
    public string? QuotedContent { get; set; }
}
```

### GameObject
```csharp
public class GameObject
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Name { get; set; }
    public long SequenceNumber { get; set; }
    public Dictionary<string, object> Properties { get; set; }
}
```

### DMMessage
```csharp
public class DMMessage
{
    public string SenderId { get; set; }
    public string Type { get; set; }
    public string? Content { get; set; }
    public object? Data { get; set; }
    public long Timestamp { get; set; }
}
```

---

**文档版本**: 1.0
**最后更新**: 2026-03-29
