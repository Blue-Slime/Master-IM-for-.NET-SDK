using System;
using System.Threading.Tasks;
using MasterIM.SDK;

namespace MasterIM.Test;

public class ConnectionTest
{
    public static async Task TestConnectionCallbacks()
    {
        Console.WriteLine("=== 连接状态回调测试 ===\n");

        var client = new IMClient();

        // 注册所有连接状态回调
        client.OnConnected += () => Console.WriteLine("✓ 已连接");
        client.OnDisconnected += () => Console.WriteLine("✗ 已断开");
        client.OnReconnecting += (count) => Console.WriteLine($"⟳ 重连中... 第{count}次尝试");
        client.OnReconnected += () => Console.WriteLine("✓ 重连成功");
        client.OnConnectionError += (err) => Console.WriteLine($"✗ 连接错误: {err}");

        try
        {
            // 测试正常连接
            Console.WriteLine("1. 测试正常连接...");
            await client.ConnectAsync("ws://localhost:5000/ws", "test_user", "test_room", "lobby");
            await Task.Delay(2000);

            Console.WriteLine("\n2. 模拟断开连接（关闭服务端测试重连）...");
            Console.WriteLine("   等待自动重连...");
            await Task.Delay(20000);

            Console.WriteLine("\n✓ 连接状态回调测试完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ 测试失败: {ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    }
}
