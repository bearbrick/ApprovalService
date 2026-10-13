using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace ApprovalService.Contracts;

/// <summary>
/// 开放接口、回调和流程定义共用的 JSON 写法：属性名小驼峰，枚举写成字符串，空值不输出。
/// </summary>
public static class ApprovalJson
{
    /// <summary>
    /// 共用的序列化选项（只读，不要修改）。
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// 把共用写法应用到一份选项上（接口层用它配置 ASP.NET Core 的 JSON 选项，保证接口和回调写法一致）。
    /// </summary>
    /// <param name="options">要配置的选项，应基于 <see cref="JsonSerializerDefaults.Web"/>。</param>
    public static void Configure(JsonSerializerOptions options)
    {
        // 中文直接输出，不转成 \uXXXX，用 curl、日志看错误说明时能直接读；HTML 敏感字符仍会转义。
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <summary>
    /// 创建序列化选项。
    /// </summary>
    /// <returns>配置好并冻结的选项。</returns>
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
