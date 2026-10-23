using System.Text;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Identity;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 在部署服务器控制台执行首位管理员初始化，不公开 HTTP 路由。
/// </summary>
internal static class BootstrapAdminCommand
{
    /// <summary>
    /// 交互式收集首位管理员信息并写入已配置的服务数据库。
    /// </summary>
    /// <param name="services">已完成服务配置的容器。</param>
    /// <returns>成功为零，输入或业务冲突为一。</returns>
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("请在服务器本机交互式控制台运行初始化命令，不要通过管道传入密码。");
            return 1;
        }

        Console.Write("内部人员 ID：");
        var personId = Console.ReadLine() ?? string.Empty;
        Console.Write("工号（已有人员可留空）：");
        var employeeNumber = Console.ReadLine();
        Console.Write("姓名（已有人员可留空）：");
        var personName = Console.ReadLine();
        Console.Write("临时密码（至少 10 位）：");
        var password = ReadSecret();
        Console.Write("再次输入临时密码：");
        var confirmation = ReadSecret();
        if (password != confirmation)
        {
            Console.Error.WriteLine("两次输入的临时密码不一致。");
            return 1;
        }

        try
        {
            using var scope = services.CreateScope();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<FirstAdminBootstrapper>();
            await bootstrapper.PrepareDatabaseAsync();
            await bootstrapper.BootstrapAsync(personId, employeeNumber, personName, password);
            Console.WriteLine("首位组织管理员已开通。首次登录后必须修改临时密码。");
            return 0;
        }
        catch (UserFriendlyException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    /// <summary>
    /// 从本机终端读取不回显的密码，退格时只修改内存中的输入。
    /// </summary>
    /// <returns>输入的密码。</returns>
    private static string ReadSecret()
    {
        var result = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return result.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (result.Length > 0)
                {
                    result.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar) && result.Length < 129)
            {
                result.Append(key.KeyChar);
            }
        }
    }
}
