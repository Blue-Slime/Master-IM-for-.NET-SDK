using MasterIM.SDK;
using MasterIM.SDK.Models;

Console.WriteLine("=== MasterIM 简化集成测试 ===\n");

var passed = 0;
var failed = 0;

void Pass(string test) { Console.WriteLine($"✅ {test}"); passed++; }
void Fail(string test, string error) { Console.WriteLine($"❌ {test}: {error}"); failed++; }

Console.WriteLine("请先手动启动服务端: cd Master.IM.Server && dotnet run");
Console.WriteLine("按任意键继续测试...");
Console.ReadKey();
Console.WriteLine();

// === 群聊测试 ===
Console.WriteLine("【群聊测试】");
var client1 = new IMClient();
var client2 = new IMClient();
var msgReceived = false;

client2.OnMessageReceived += msg =>
{
    if (msg.Content == "Hello") msgReceived = true;
};

try
{
    await client1.ConnectAsync("ws://localhost:5000/ws", "user1", "room1", "lobby");
    await client2.ConnectAsync("ws://localhost:5000/ws", "user2", "room1", "lobby");
    await Task.Delay(1000);
    Pass("群聊连接成功");

    await client1.SendMessageAsync(new Message
    {
        Content = "Hello",
        SenderId = "user1",
        SendTime = DateTime.Now
    });
    await Task.Delay(2000);

    if (msgReceived) Pass("群聊消息收发");
    else Fail("群聊消息收发", "未收到");
}
catch (Exception ex) { Fail("群聊测试", ex.Message); }

// === GameObject 测试 ===
Console.WriteLine("\n【GameObject 测试】");
try
{
    var obj = new GameObject { Type = "Character", Name = "测试" };
    await client1.CreateObjectAsync(obj);
    await Task.Delay(1000);
    
    var objects = await client2.QueryObjectsByTypeAsync("Character");
    if (objects.Any(o => o.Name == "测试"))
        Pass("GameObject 同步");
    else
        Fail("GameObject 同步", "未找到");
}
catch (Exception ex) { Fail("GameObject", ex.Message); }

client1.Dispose();
client2.Dispose();

Console.WriteLine($"\n通过: {passed}, 失败: {failed}");
