# TRPGMaster IM SDK API 文档

**版本**: 1.0
**日期**: 2026-03-29

---

## 1. 群聊客户端 (IMClient)

### 1.1 连接管理

#### ConnectAsync
```csharp
Task ConnectAsync(string url, string userId, string roomId, string channelId)
```
**用途**: 连接到群聊服务器

**参数**:
- `url`: WebSocket 服务器地址 (如 `ws://localhost:5000/ws`)
- `userId`: 用户ID
- `roomId`: 房间ID
- `channelId`: 频道ID

**用法**:
```csharp
var client = new IMClient();
await client.ConnectAsync("ws://localhost:5000/ws", "user1", "room1", "lobby");
```

**特性**:
- 自动重连
- 心跳检测
- 触发 `OnConnected` 事件

---

### 1.2 消息操作

#### SendMessageAsync
```csharp
Task SendMessageAsync(Message msg)
```
**用途**: 发送消息到群聊

**参数**:
- `msg`: 消息对象，包含内容、发送者、时间等

**用法**:
```csharp
await client.SendMessageAsync(new Message {
    Content = "Hello",
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
**用途**: 查询历史消息（分页）

**参数**:
- `lastPage`: 起始页号
- `lastSeq`: 起始页内序号
- `limit`: 查询数量（默认100）

**用法**:
```csharp
// 查询最新100条
var messages = await client.QueryPageAsync(int.MaxValue, int.MaxValue, 100);

// 向上翻页
var older = await client.QueryPageAsync(lastPage, lastSeq, 100);
```

#### InsertHistoryMessageAsync
```csharp
Task InsertHistoryMessageAsync(Message msg, DateTime historicalTime)
```
**用途**: 插入历史消息（超时空编辑）

**参数**:
- `msg`: 消息对象
- `historicalTime`: 历史时间点

**用法**:
```csharp
await client.InsertHistoryMessageAsync(new Message {
    Content = "补充的历史消息",
    SenderId = "user1"
}, DateTime.Parse("2026-01-01 10:00:00"));
```

#### ModifyMessageAsync
```csharp
Task ModifyMessageAsync(int page, int seq, string newContent)
```
**用途**: 修改已发送的消息

**参数**:
- `page`: 消息所在页号
- `seq`: 消息页内序号
- `newContent`: 新内容

**用法**:
```csharp
await client.ModifyMessageAsync(0, 5, "修改后的内容");
```

---

### 1.3 分页管理

#### CheckPageModifiedAsync
```csharp
Task<long> CheckPageModifiedAsync(int pageNumber)
```
**用途**: 检查分页最后修改时间（用于离线同步）

**参数**:
- `pageNumber`: 页号

**返回**: Unix 毫秒时间戳

**用法**:
```csharp
var modifiedTime = await client.CheckPageModifiedAsync(5);
if (modifiedTime > cachedTime) {
    // 需要重新拉取该页
}
```

#### CreateEmptyPageAsync
```csharp
Task CreateEmptyPageAsync(int pageNumber)
```
**用途**: 创建空白分页（为超时空编辑预留空间）

**参数**:
- `pageNumber`: 页号

**用法**:
```csharp
await client.CreateEmptyPageAsync(10);
```

#### DeleteEmptyPageAsync
```csharp
Task DeleteEmptyPageAsync(int pageNumber)
```
**用途**: 删除空白分页

**参数**:
- `pageNumber`: 页号

**用法**:
```csharp
await client.DeleteEmptyPageAsync(10);
```

#### BatchMoveMessagesAsync
```csharp
Task BatchMoveMessagesAsync(List<(int page, int seq)> messages, int targetPage, int targetSeq)
```
**用途**: 批量平移消息到其他分页

**参数**:
- `messages`: 消息列表（页号、序号）
- `targetPage`: 目标页号
- `targetSeq`: 目标起始序号

**用法**:
```csharp
var messages = new List<(int, int)> { (5, 10), (5, 11), (5, 12) };
await client.BatchMoveMessagesAsync(messages, targetPage: 6, targetSeq: 0);
```

#### BatchDeleteMessagesAsync
```csharp
Task BatchDeleteMessagesAsync(List<(int page, int seq)> messages)
```
**用途**: 批量删除消息

**参数**:
- `messages`: 消息列表（页号、序号）

**用法**:
```csharp
var messages = new List<(int, int)> { (5, 10), (5, 11) };
await client.BatchDeleteMessagesAsync(messages);
```

---

### 1.4 流式数据

#### SendStreamAsync
```csharp
Task SendStreamAsync(StreamData data)
```
**用途**: 发送流式数据（鼠标位置、绘制数据等）

**参数**:
- `data`: 流式数据对象

**用法**:
```csharp
await client.SendStreamAsync(new StreamData {
    Type = "mouse",
    Data = new { X = 100, Y = 200 }
});
```

---

### 1.5 游戏对象操作

#### CreateObjectAsync
```csharp
Task<string> CreateObjectAsync(GameObject obj)
```
**用途**: 创建游戏对象

**参数**:
- `obj`: 游戏对象

**返回**: 对象ID

**用法**:
```csharp
var character = new GameObject {
    Type = "Character",
    Name = "勇者",
    Properties = new() { { "Level", 5 }, { "HP", 100 } }
};
var id = await client.CreateObjectAsync(character);
```

#### UpdateObjectAsync
```csharp
Task UpdateObjectAsync(GameObject obj)
```
**用途**: 更新游戏对象

**参数**:
- `obj`: 游戏对象

**用法**:
```csharp
character.Properties["HP"] = 80;
await client.UpdateObjectAsync(character);
```

#### DeleteObjectAsync
```csharp
Task DeleteObjectAsync(string objectId)
```
**用途**: 删除游戏对象

**参数**:
- `objectId`: 对象ID

**用法**:
```csharp
await client.DeleteObjectAsync(id);
```

#### QueryObjectsByTypeAsync
```csharp
Task<List<GameObject>> QueryObjectsByTypeAsync(string type)
```
**用途**: 按类型查询游戏对象

**参数**:
- `type`: 对象类型（如 "Character", "Map", "Token"）

**用法**:
```csharp
var characters = await client.QueryObjectsByTypeAsync("Character");
```

#### QueryObjectsBySequenceAsync
```csharp
Task<List<GameObject>> QueryObjectsBySequenceAsync(long startSeq, long endSeq)
```
**用途**: 按序列号范围查询（用于同步）

**参数**:
- `startSeq`: 起始序列号
- `endSeq`: 结束序列号

**用法**:
```csharp
var objects = await client.QueryObjectsBySequenceAsync(100, 200);
```

---

### 1.6 事件

#### OnMessageReceived
```csharp
event Action<Message> OnMessageReceived
```
**用途**: 接收到新消息时触发

**用法**:
```csharp
client.OnMessageReceived += msg => {
    Console.WriteLine($"{msg.SenderId}: {msg.Content}");
};
```

#### OnUpdateNotification
```csharp
event Action<UpdateNotification> OnUpdateNotification
```
**用途**: 接收到更新通知时触发（消息修改、分页变更等）

**用法**:
```csharp
client.OnUpdateNotification += ntf => {
    Console.WriteLine($"更新类型: {ntf.Type}");
};
```

#### OnStreamReceived
```csharp
event Action<StreamData> OnStreamReceived
```
**用途**: 接收到流式数据时触发

**用法**:
```csharp
client.OnStreamReceived += data => {
    Console.WriteLine($"流式数据: {data.Type}");
};
```

#### OnObjectSync
```csharp
event Action<ObjectSyncData> OnObjectSync
```
**用途**: 接收到对象同步时触发

**用法**:
```csharp
client.OnObjectSync += sync => {
    Console.WriteLine($"对象同步: {sync.Action} {sync.Object?.Name}");
};
```

#### OnConnected / OnDisconnected
```csharp
event Action OnConnected
event Action OnDisconnected
```
**用途**: 连接/断开时触发

**用法**:
```csharp
client.OnConnected += () => Console.WriteLine("已连接");
client.OnDisconnected += () => Console.WriteLine("已断开");
```

---

## 2. 简易单聊客户端 (DMClient)

### 2.1 连接管理

#### ConnectAsync
```csharp
Task ConnectAsync(string url, string userId, string targetUserId)
```
**用途**: 连接到单聊服务器（临时通信）

**参数**:
- `url`: WebSocket 服务器地址 (如 `ws://localhost:5000/dm`)
- `userId`: 当前用户ID
- `targetUserId`: 目标用户ID

**用法**:
```csharp
var dmClient = new DMClient();
await dmClient.ConnectAsync("ws://localhost:5000/dm", "user1", "user2");
```

**特性**: 验证双方在线，消息不存储

---

### 2.2 消息发送

#### SendTextAsync
```csharp
Task SendTextAsync(string content)
```
**用途**: 发送文本消息

**用法**:
```csharp
await dmClient.SendTextAsync("你好");
```

#### SendImageAsync
```csharp
Task SendImageAsync(string url, int width, int height)
```
**用途**: 发送图片消息

**用法**:
```csharp
await dmClient.SendImageAsync("https://example.com/image.jpg", 800, 600);
```

#### SendFileAsync
```csharp
Task SendFileAsync(string fileName, long fileSize, string url)
```
**用途**: 发送文件消息

**用法**:
```csharp
await dmClient.SendFileAsync("文档.pdf", 1024000, "https://example.com/file.pdf");
```

#### SendCustomAsync
```csharp
Task SendCustomAsync(object data)
```
**用途**: 发送自定义消息

**用法**:
```csharp
await dmClient.SendCustomAsync(new { Type = "dice", Result = 18 });
```

---

### 2.3 事件

#### OnMessageReceived
```csharp
event Action<DMMessage> OnMessageReceived
```
**用途**: 接收到消息时触发

**用法**:
```csharp
dmClient.OnMessageReceived += msg => Console.WriteLine($"{msg.SenderId}: {msg.Content}");
```

#### OnTargetOffline
```csharp
event Action OnTargetOffline
```
**用途**: 对方离线时触发

**用法**:
```csharp
dmClient.OnTargetOffline += () => Console.WriteLine("对方离线，单聊结束");
```

---
