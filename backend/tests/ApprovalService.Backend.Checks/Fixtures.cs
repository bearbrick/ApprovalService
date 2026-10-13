namespace ApprovalService.Backend.Checks;

/// <summary>
/// 检查共用的数据和工具。
/// </summary>
internal static class Fixtures
{
    /// <summary>
    /// 从输出目录逐级向上找到后端根目录（有 ApprovalService.Backend.slnx 的目录）。
    /// </summary>
    /// <returns>backend 目录的完整路径。</returns>
    /// <exception cref="DirectoryNotFoundException">不在仓库里运行。</exception>
    public static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ApprovalService.Backend.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("未找到 ApprovalService.Backend.slnx，请在仓库内运行检查程序。");
    }
}
