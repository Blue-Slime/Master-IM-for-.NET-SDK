using MasterIM.SDK;
using MasterIM.SDK.Models;

Console.WriteLine("=== MasterIM 自动化测试 ===\n");

var passed = 0;
var failed = 0;

void Pass(string test) { Console.WriteLine($"✅ {test}"); passed++; }
void Fail(string test, string error) { Console.WriteLine($"❌ {test}: {error}"); failed++; }

// === 1. SDK 实例化测试 ===
Console.WriteLine("【1. SDK 实例化测试】");
try { var c = new IMClient(); c.Dispose(); Pass("IMClient 实例化"); }
catch (Exception ex) { Fail("IMClient 实例化", ex.Message); }

try { var c = new DMClient(); c.Dispose(); Pass("DMClient 实例化"); }
catch (Exception ex) { Fail("DMClient 实例化", ex.Message); }

try { var c = new DMAdvancedClient(); c.Dispose(); Pass("DMAdvancedClient 实例化"); }
catch (Exception ex) { Fail("DMAdvancedClient 实例化", ex.Message); }

// === 2. 消息模型测试 ===
Console.WriteLine("\n【2. 消息模型测试】");
try
{
    var msg = new Message
    {
        Content = "测试消息",
        SenderId = "user1",
        SendTime = DateTime.Now,
        PageNumber = 0,
        InPageSeq = 0
    };
    if (msg.Content == "测试消息" && msg.SenderId == "user1")
        Pass("Message 基本属性");
    else
        Fail("Message 基本属性", "属性值不匹配");
}
catch (Exception ex) { Fail("Message 基本属性", ex.Message); }

try
{
    var msg = new Message
    {
        Content = "回复消息",
        SenderId = "user2",
        SendTime = DateTime.Now,
        ReplyToTime = DateTime.Now.AddMinutes(-5),
        QuotedContent = "原始消息"
    };
    if (msg.ReplyToTime != null && msg.QuotedContent == "原始消息")
        Pass("Message 回复引用");
    else
        Fail("Message 回复引用", "回复属性不匹配");
}
catch (Exception ex) { Fail("Message 回复引用", ex.Message); }


// === 3. GameObject 模型测试 ===
Console.WriteLine("\n【3. GameObject 模型测试】");
try
{
    var obj = new GameObject
    {
        Id = "obj1",
        Type = "Character",
        Name = "角色1",
        Properties = new() { ["hp"] = 100, ["level"] = 5 }
    };
    if (obj.Id == "obj1" && obj.Type == "Character" && obj.Properties.Count == 2)
        Pass("GameObject 基本属性");
    else
        Fail("GameObject 基本属性", "属性值不匹配");
}
catch (Exception ex) { Fail("GameObject 基本属性", ex.Message); }


// === 4. 事件处理测试 ===
Console.WriteLine("\n【4. 事件处理测试】");
try
{
    var client = new IMClient();
    var fired = false;
    client.OnMessageReceived += msg => { fired = true; };
    Pass("IMClient OnMessageReceived 订阅");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient OnMessageReceived", ex.Message); }

try
{
    var client = new IMClient();
    var fired = false;
    client.OnStreamReceived += data => { fired = true; };
    Pass("IMClient OnStreamReceived 订阅");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient OnStreamReceived", ex.Message); }

try
{
    var client = new DMClient();
    var fired = false;
    client.OnMessageReceived += msg => { fired = true; };
    Pass("DMClient OnMessageReceived 订阅");
    client.Dispose();
}
catch (Exception ex) { Fail("DMClient OnMessageReceived", ex.Message); }


// === 5. 连接方法测试 ===
Console.WriteLine("\n【5. 连接方法测试】");
try
{
    var client = new IMClient();
    var task = client.ConnectAsync("ws://invalid:9999/ws", "u1", "r1", "c1");
    Pass("IMClient ConnectAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient ConnectAsync", ex.Message); }

try
{
    var client = new DMClient();
    var task = client.ConnectAsync("ws://invalid:9999/dm", "u1", "u2");
    Pass("DMClient ConnectAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("DMClient ConnectAsync", ex.Message); }

try
{
    var client = new DMAdvancedClient();
    var task = client.ConnectAsync("ws://invalid:9999/dm_advanced", "u1", "u2", true);
    Pass("DMAdvancedClient ConnectAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("DMAdvancedClient ConnectAsync", ex.Message); }


// === 6. 消息操作测试 ===
Console.WriteLine("\n【6. 消息操作测试】");
try
{
    var client = new IMClient();
    var msg = new Message { Content = "test", SenderId = "u1", SendTime = DateTime.Now };
    var task = client.SendMessageAsync(msg);
    Pass("IMClient SendMessageAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient SendMessageAsync", ex.Message); }

try
{
    var client = new IMClient();
    var task = client.QueryPageAsync(0, 0, 100);
    Pass("IMClient QueryPageAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient QueryPageAsync", ex.Message); }

try
{
    var client = new IMClient();
    var task = client.ModifyMessageAsync(0, 5, "新内容");
    Pass("IMClient ModifyMessageAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient ModifyMessageAsync", ex.Message); }


// === 7. GameObject 操作测试 ===
Console.WriteLine("\n【7. GameObject 操作测试】");
try
{
    var client = new IMClient();
    var obj = new GameObject { Type = "Character", Name = "角色" };
    var task = client.CreateObjectAsync(obj);
    Pass("IMClient CreateObjectAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient CreateObjectAsync", ex.Message); }

try
{
    var client = new IMClient();
    var task = client.QueryObjectsByTypeAsync("Character");
    Pass("IMClient QueryObjectsByTypeAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient QueryObjectsByTypeAsync", ex.Message); }

try
{
    var client = new IMClient();
    var task = client.DeleteObjectAsync("obj1");
    Pass("IMClient DeleteObjectAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient DeleteObjectAsync", ex.Message); }


// === 8. DM 消息操作测试 ===
Console.WriteLine("\n【8. DM 消息操作测试】");
try
{
    var client = new DMClient();
    var task = client.SendTextAsync("hello");
    Pass("DMClient SendTextAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("DMClient SendTextAsync", ex.Message); }

try
{
    var client = new DMAdvancedClient();
    var task = client.QueryPageAsync(0, 0, 50);
    Pass("DMAdvancedClient QueryPageAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("DMAdvancedClient QueryPageAsync", ex.Message); }

try
{
    var client = new DMAdvancedClient();
    var task = client.ModifyMessageAsync(0, 3, "修改");
    Pass("DMAdvancedClient ModifyMessageAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("DMAdvancedClient ModifyMessageAsync", ex.Message); }


// === 9. 高级功能测试 ===
Console.WriteLine("\n【9. 高级功能测试】");
try
{
    var client = new IMClient();
    var task = client.CheckPageModifiedAsync(0);
    Pass("IMClient CheckPageModifiedAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient CheckPageModifiedAsync", ex.Message); }

try
{
    var client = new IMClient();
    var msg = new Message { Content = "历史", SenderId = "u1", SendTime = DateTime.Now };
    var task = client.InsertHistoryMessageAsync(msg, DateTime.Now.AddDays(-1));
    Pass("IMClient InsertHistoryMessageAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient InsertHistoryMessageAsync", ex.Message); }

try
{
    var client = new IMClient();
    var data = new StreamData { Type = "typing", Data = "user1" };
    var task = client.SendStreamAsync(data);
    Pass("IMClient SendStreamAsync 可调用");
    client.Dispose();
}
catch (Exception ex) { Fail("IMClient SendStreamAsync", ex.Message); }


// === 10. Dispose 测试 ===
Console.WriteLine("\n【10. Dispose 测试】");
try
{
    var client = new IMClient();
    client.Dispose();
    client.Dispose();
    Pass("IMClient 多次 Dispose");
}
catch (Exception ex) { Fail("IMClient 多次 Dispose", ex.Message); }

try
{
    var client = new DMClient();
    client.Dispose();
    client.Dispose();
    Pass("DMClient 多次 Dispose");
}
catch (Exception ex) { Fail("DMClient 多次 Dispose", ex.Message); }

try
{
    var client = new DMAdvancedClient();
    client.Dispose();
    client.Dispose();
    Pass("DMAdvancedClient 多次 Dispose");
}
catch (Exception ex) { Fail("DMAdvancedClient 多次 Dispose", ex.Message); }

// === 测试总结 ===
Console.WriteLine("\n=== 测试完成 ===");
Console.WriteLine($"通过: {passed}");
Console.WriteLine($"失败: {failed}");
Console.WriteLine($"总计: {passed + failed}");
Console.WriteLine($"通过率: {(passed * 100.0 / (passed + failed)):F1}%");

return failed > 0 ? 1 : 0;
