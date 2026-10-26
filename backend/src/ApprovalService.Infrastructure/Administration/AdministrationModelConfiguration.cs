using Microsoft.EntityFrameworkCore;

namespace ApprovalService.Infrastructure.Administration;

/// <summary>
/// 管理模块的独立模型登记入口，避免统一上下文超过文件规模上限。
/// </summary>
internal static class AdministrationModelConfiguration
{
    /// <summary>
    /// 登记品牌信息与在线日历模型。
    /// </summary>
    /// <param name="modelBuilder">三库共用模型构造器。</param>
    public static void Configure(ModelBuilder modelBuilder)
    {
        SystemBrandingModelConfiguration.Configure(modelBuilder);
        WorkCalendarModelConfiguration.Configure(modelBuilder);
    }
}
