using MasterIM.SDK;
using MasterIM.SDK.Models;
using System.Diagnostics;

Console.WriteLine("=== MasterIM 集成测试 ===\n");

var passed = 0;
var failed = 0;
Process? serverProcess = null;

void Pass(string test) { Console.WriteLine($"✅ {test}"); passed++; }
void Fail(string test, string error) { Console.WriteLine($"❌ {test}: {error}"); failed++; }

try
{
    // 启动服务端
    Console.WriteLine("【启动服务端】");
    var serverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Master.IM.Server", "bin", "Debug", "net8.0", "MasterIM.Server.exe");
    serverProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = serverPath.Replace(".exe", ".dll"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };
    serverProcess.Start();
    Console.WriteLine("等待服务端启动...");
    await Task.Delay(3000);
    Pass("服务端启动");

    // === 1. 群聊连接测试 ===
    Console.WriteLine("\n【1. 群聊连接测试】");
    var client1 = new IMClient();
    var client2 = new IMClient();

    // 先订阅事件
    var received = false;
    client2.OnMessageReceived += msg =>
    {
        if (msg.Content == "测试消息" && msg.SenderId == "user1")
            received = true;
    };

    try
    {
        await client1.ConnectAsync("ws://localhost:5000/ws", "user1", "room1", "lobby");
        await Task.Delay(500);
        Pass("客户端1连接成功");
    }
    catch (Exception ex) { Fail("客户端1连接", ex.Message); }

    try
    {
        await client2.ConnectAsync("ws://localhost:5000/ws", "user2", "room1", "lobby");
        await Task.Delay(500);
        Pass("客户端2连接成功");
    }
    catch (Exception ex) { Fail("客户端2连接", ex.Message); }


    // === 2. 消息收发测试 ===
    Console.WriteLine("\n【2. 消息收发测试】");
    
    try
    {
        await client1.SendMessageAsync(new Message
        {
            Content = "测试消息",
            SenderId = "user1",
            SendTime = DateTime.Now
        });
        await Task.Delay(1000);
        if (received) Pass("消息收发成功");
        else Fail("消息收发", "未收到消息");
    }
    catch (Exception ex) { Fail("消息收发", ex.Message); }


    // === 3. GameObject 同步测试 ===
    Console.WriteLine("\n【3. GameObject 同步测试】");
    try
    {
        var obj = new GameObject { Type = "Character", Name = "测试角色" };
        var objId = await client1.CreateObjectAsync(obj);
        await Task.Delay(500);
        
        var objects = await client2.QueryObjectsByTypeAsync("Character");
        if (objects.Any(o => o.Name == "测试角色"))
            Pass("GameObject 同步成功");
        else
            Fail("GameObject 同步", "未找到同步对象");
    }
    catch (Exception ex) { Fail("GameObject 同步", ex.Message); }


    // === 4. 简单DM测试 ===
    Console.WriteLine("\n【4. 简单DM测试】");
    var dmClient1 = new DMClient();
    var dmClient2 = new DMClient();
    var dmReceived = false;
    
    dmClient2.OnMessageReceived += msg =>
    {
        if (msg.Content == "DM消息") dmReceived = true;
    };
    
    try
    {
        await dmClient1.ConnectAsync("ws://localhost:5000/dm", "user1", "user2");
        await dmClient2.ConnectAsync("ws://localhost:5000/dm", "user2", "user1");
        await Task.Delay(1000);

        await dmClient1.SendTextAsync("DM消息");
        await Task.Delay(2000);
        
        if (dmReceived) Pass("简单DM收发成功");
        else Fail("简单DM收发", "未收到消息");
    }
    catch (Exception ex) { Fail("简单DM", ex.Message); }


    // === 5. 高级DM测试 ===
    Console.WriteLine("\n【5. 高级DM测试】");
    var advClient1 = new DMAdvancedClient();
    var advClient2 = new DMAdvancedClient();
    var advReceived = false;
    
    advClient2.OnMessageReceived += msg =>
    {
        if (msg.Content == "高级DM") advReceived = true;
    };
    
    try
    {
        await advClient1.ConnectAsync("ws://localhost:5000/dm_advanced", "user3", "user4", true, 30);
        await advClient2.ConnectAsync("ws://localhost:5000/dm_advanced", "user4", "user3", true, 30);
        await Task.Delay(500);
        
        await advClient1.SendMessageAsync(new Message
        {
            Content = "高级DM",
            SenderId = "user3",
            SendTime = DateTime.Now
        });
        await Task.Delay(1000);
        
        if (advReceived) Pass("高级DM收发成功");
        else Fail("高级DM收发", "未收到消息");
    }
    catch (Exception ex) { Fail("高级DM", ex.Message); }


    // === 6. 消息查询测试 ===
    Console.WriteLine("\n【6. 消息查询测试】");
    try
    {
        var messages = await advClient1.QueryPageAsync(0, 0, 10);
        if (messages.Any(m => m.Content == "高级DM"))
            Pass("消息持久化查询成功");
        else
            Fail("消息查询", "未找到已存储消息");
    }
    catch (Exception ex) { Fail("消息查询", ex.Message); }

    // 清理
    client1.Dispose();
    client2.Dispose();
    dmClient1.Dispose();
    dmClient2.Dispose();
    advClient1.Dispose();
    advClient2.Dispose();
}
finally
{
    if (serverProcess != null && !serverProcess.HasExited)
    {
        serverProcess.Kill();
        serverProcess.Dispose();
        Console.WriteLine("\n服务端已停止");
    }
}

Console.WriteLine("\n=== 集成测试完成 ===");
Console.WriteLine($"通过: {passed}");
Console.WriteLine($"失败: {failed}");
Console.WriteLine($"总计: {passed + failed}");
Console.WriteLine($"通过率: {(passed * 100.0 / (passed + failed)):F1}%");

return failed > 0 ? 1 : 0;
