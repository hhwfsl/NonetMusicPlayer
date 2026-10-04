// 离线更新验收用的无界面替身，只在自己的隔离安装目录写标记；不创建主窗口或访问用户数据。
if (args is ["wait"])
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "parent-ready.txt"), Environment.ProcessId.ToString());
    var until = DateTimeOffset.UtcNow.AddSeconds(20);
    while (!File.Exists(Path.Combine(AppContext.BaseDirectory, "parent-exit.txt")) && DateTimeOffset.UtcNow < until) Thread.Sleep(50);
}
else File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restarted.txt"), "updated application started");
