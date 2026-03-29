# TRPGMaster IM Server API 文档

**版本**: 1.0
**日期**: 2026-03-29

---

## 1. 群聊服务端 (IMServer)

### 1.1 WebSocket 端点

**端点**: `/ws`

**连接参数**:
```
ws://server:port/ws?userId={userId}&roomId={roomId}&channelId={channelId}
```

**参数说明**:
- `userId`: 用户ID
- `roomId`: 房间ID
- `channelId`: 频道ID

---

### 1.2 协议格式

**Packet 结构**:
```csharp
public class Packet
{
    public string T { get; set; }      // 消息类型
    public object? P { get; set; }     // 消息载荷
    public string? Id { get; set; }    // 请求ID（用于响应匹配）
}
```

---

### 1.3 消息操作

#### 发送消息 (msg)
**客户端 → 服务端**:
```json
{
    "T": "msg",
    "P": {
        "Content": "消息内容",
        "SenderId": "user1",
        "SendTime": "2026-03-29T10:00:00Z",
        "PageNumber": 0,
        "InPageSeq": 0
    }
}
```

**服务端 → 客户端（广播）**:
```json
{
    "T": "msg",
    "P": { /* Message 对象 */ }
}
```

**功能**: 发送消息到频道，服务端存储并广播给所有在线用户

---

#### 查询分页 (qry)
**客户端 → 服务端**:
```json
{
    "T": "qry",
    "Id": "request-id-123",
    "P": {
        "LastPage": 999999,
        "LastSeq": 999999,
        "Limit": 100
    }
}
```

**服务端 → 客户端**:
```json
{
    "T": "qry",
    "Id": "request-id-123",
    "P": [ /* Message 数组 */ ]
}
```

**功能**: 查询历史消息，按页号和序号倒序返回

---

#### 检查分页修改时间 (chk)
**客户端 → 服务端**:
```json
{
    "T": "chk",
    "Id": "request-id-456",
    "P": { "PageNumber": 5 }
}
```

**服务端 → 客户端**:
```json
{
    "T": "chk",
    "Id": "request-id-456",
    "P": { "PageNumber": 5, "LastModified": 1711699200000 }
}
```

**功能**: 返回分页最后修改时间（Unix 毫秒），用于离线同步

---

#### 插入历史消息 (msg - insert)
**客户端 → 服务端**:
```json
{
    "T": "msg",
    "P": {
        "Type": "insert",
        "Message": { /* Message 对象 */ }
    }
}
```

**服务端 → 客户端（广播通知）**:
```json
{
    "T": "ntf",
    "P": {
        "Type": "message_inserted",
        "TimeMs": 1711699200000
    }
}
```

**功能**: 插入消息到历史时间点（超时空编辑）

---

#### 修改消息 (msg - modify)
**客户端 → 服务端**:
```json
{
    "T": "msg",
    "P": {
        "Type": "modify",
        "Page": 0,
        "Seq": 5,
        "Content": "修改后的内容"
    }
}
```

**服务端 → 客户端（广播通知）**:
```json
{
    "T": "ntf",
    "P": {
        "Type": "message_modified",
        "Page": 0,
        "Seq": 5
    }
}
```

**功能**: 修改已发送的消息内容

---

### 1.4 分页管理

#### 创建空白分页 (crt)
**客户端 → 服务端**:
```json
{
    "T": "crt",
    "P": { "PageNumber": 10 }
}
```

**服务端 → 客户端（广播通知）**:
```json
{
    "T": "ntf",
    "P": {
        "Type": "page_created",
        "PageNumber": 10
    }
}
```

**功能**: 创建空白分页，为超时空编辑预留空间

---

#### 删除空白分页 (del)
**客户端 → 服务端**:
```json
{
    "T": "del",
    "P": { "PageNumber": 10 }
}
```

**服务端 → 客户端（广播通知）**:
```json
{
    "T": "ntf",
    "P": {
        "Type": "page_deleted",
        "PageNumber": 10
    }
}
```

**功能**: 删除无消息的空白分页

---

#### 批量平移消息 (bmv)
**客户端 → 服务端**:
```json
{
    "T": "bmv",
    "P": {
        "Messages": [
            { "Page": 5, "Seq": 10 },
            { "Page": 5, "Seq": 11 }
        ],
        "TargetPage": 6,
        "TargetSeq": 0
    }
}
```

**服务端 → 客户端（广播通知）**:
```json
{
    "T": "ntf",
    "P": {
        "Type": "batch_moved",
        "AffectedPages": [5, 6]
    }
}
```

**功能**: 批量移动消息到其他分页

---

#### 批量删除消息 (bdl)
**客户端 → 服务端**:
```json
{
    "T": "bdl",
    "P": {
        "Messages": [
            { "Page": 5, "Seq": 10 },
            { "Page": 5, "Seq": 11 }
        ]
    }
}
```

**服务端 → 客户端（广播通知）**:
```json
{
    "T": "ntf",
    "P": {
        "Type": "batch_deleted",
        "AffectedPages": [5]
    }
}
```

**功能**: 批量删除消息

---

### 1.5 流式数据

#### 发送流式数据 (stm)
**客户端 → 服务端**:
```json
{
    "T": "stm",
    "P": {
        "Type": "mouse",
        "Data": { "X": 100, "Y": 200 }
    }
}
```

**服务端 → 其他客户端（转发）**:
```json
{
    "T": "stm",
    "P": { /* 原始数据 */ }
}
```

**功能**: 转发流式数据（鼠标位置、绘制等），不存储

---

### 1.6 游戏对象操作

#### 创建对象 (obj_create)
**客户端 → 服务端**:
```json
{
    "T": "obj_create",
    "P": {
        "Type": "Character",
        "Name": "勇者",
        "Properties": { "Level": 5, "HP": 100 }
    }
}
```

**服务端 → 客户端（广播）**:
```json
{
    "T": "obj_sync",
    "P": {
        "Action": "create",
        "Object": { /* GameObject */ },
        "SequenceNumber": 123
    }
}
```

---

#### 更新对象 (obj_update)
**客户端 → 服务端**:
```json
{
    "T": "obj_update",
    "P": { /* GameObject */ }
}
```

**服务端 → 客户端（广播）**:
```json
{
    "T": "obj_sync",
    "P": {
        "Action": "update",
        "Object": { /* GameObject */ },
        "SequenceNumber": 124
    }
}
```

---

#### 删除对象 (obj_delete)
**客户端 → 服务端**:
```json
{
    "T": "obj_delete",
    "P": { "ObjectId": "obj-123" }
}
```

**服务端 → 客户端（广播）**:
```json
{
    "T": "obj_sync",
    "P": {
        "Action": "delete",
        "ObjectId": "obj-123"
    }
}
```

---

#### 查询对象 (obj_query)
**按类型查询**:
```json
{
    "T": "obj_query",
    "Id": "req-789",
    "P": { "Type": "Character" }
}
```

**按序列号查询**:
```json
{
    "T": "obj_query",
    "Id": "req-790",
    "P": { "StartSeq": 100, "EndSeq": 200 }
}
```

**服务端响应**:
```json
{
    "T": "obj_query",
    "Id": "req-789",
    "P": [ /* GameObject 数组 */ ]
}
```

---

### 1.7 心跳

#### Ping/Pong
**客户端 → 服务端**:
```json
{ "T": "ping" }
```

**服务端 → 客户端**:
```json
{ "T": "pong" }
```

**功能**: 保持连接活跃，每30秒一次

---

## 2. 简易单聊服务端 (DMServer)

### 2.1 WebSocket 端点

**端点**: `/dm`

**连接参数**:
```
ws://server:port/dm?userId={userId}&targetUserId={targetUserId}
```

**连接验证**: 目标用户必须在线，否则拒绝连接

---

### 2.2 协议

#### 发送消息 (dm_msg)
**客户端 → 服务端**:
```json
{
    "T": "dm_msg",
    "P": {
        "Type": "text",
        "Content": "你好"
    }
}
```

**服务端 → 目标用户**:
```json
{
    "T": "dm_msg",
    "P": {
        "SenderId": "user1",
        "Content": { "Type": "text", "Content": "你好" },
        "Timestamp": 1711699200000
    }
}
```

---

#### 在线状态通知
**上线通知 (dm_online)**:
```json
{
    "T": "dm_online",
    "P": { "UserId": "user2" }
}
```

**离线通知 (dm_offline)**:
```json
{
    "T": "dm_offline",
    "P": { "UserId": "user2" }
}
```

---

## 3. 高级单聊服务端 (DMAdvancedServer)

### 3.1 WebSocket 端点

**端点**: `/dm_advanced`

**连接参数**:
```
ws://server:port/dm_advanced?userId={userId}&targetUserId={targetUserId}&enableStorage={bool}&retentionDays={int}
```

**参数说明**:
- `enableStorage`: 是否存储消息（true/false）
- `retentionDays`: 保留天数（-1=无限期, 0=不存储, >0=保留天数）

---

### 3.2 协议

#### 发送消息 (msg)
**客户端 → 服务端**:
```json
{
    "T": "msg",
    "P": {
        "Content": "你好",
        "SenderId": "user1",
        "PageNumber": 0,
        "InPageSeq": 0
    }
}
```

#### 查询分页 (qry)
**客户端 → 服务端**:
```json
{
    "T": "qry",
    "Id": "req-123",
    "P": { "LastPage": 999999, "LastSeq": 999999, "Limit": 100 }
}
```

#### 修改消息 (msg - modify)
**客户端 → 服务端**:
```json
{
    "T": "msg",
    "P": { "Type": "modify", "Page": 0, "Seq": 5, "Content": "修改后的内容" }
}
```

#### 检查分页修改时间 (chk)
**客户端 → 服务端**:
```json
{
    "T": "chk",
    "Id": "req-456",
    "P": { "PageNumber": 5 }
}
```

---

## 4. 存储结构

### 4.1 群聊存储
```
/rooms/{roomId}/
  ├── messages/
  │   ├── 2026-01.db
  │   └── 2026-02.db
  └── objects/
      └── objects.db
```

### 4.2 单聊存储
```
/dm/{user1_user2}/
  ├── messages/
  │   ├── 2026-01.db
  │   └── 2026-02.db
  └── config.json
```

---

**文档版本**: 1.0
**最后更新**: 2026-03-29





