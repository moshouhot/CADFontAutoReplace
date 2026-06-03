using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFR.Deployer.Models;

/// <summary>
/// 编译期已知的 CAD 版本静态描述符，与 ICadPlatform 对应但不依赖插件程序集。
/// <para>
/// 描述符列表 <see cref="CadDescriptors.All"/> 由各插件项目（src\AutoCAD\AFR-ACAD20XX）
/// 在构建时通过 MSBuild Target <c>EmitCadDescriptorJson</c> 生成的 <c>*.cad.json</c>
/// 文件提供，AFR.Deployer 在编译期将这些 JSON 嵌入为程序集资源，运行时反序列化得到。
/// 新增 CAD 版本/品牌时只需在新插件 csproj 中声明
/// <c>CadBrand</c> / <c>CadVersion</c> / <c>CadRegistryBasePath</c>，无需在此处手工维护。
/// </para>
/// </summary>
/// <param name="Brand">CAD 品牌，如 "AutoCAD"。</param>
/// <param name="Version">CAD 版本年份，如 "2025"。</param>
/// <param name="DisplayName">UI 显示名称，如 "AutoCAD 2025"。</param>
/// <param name="RegistryBasePath">注册表基路径，如 <c>Software\Autodesk\AutoCAD\R25.0</c>。</param>
/// <param name="AppName">注册表 Applications 子键名，如 "AFR-ACAD2025"。</param>
/// <param name="EmbeddedResourceKey">插件 DLL 文件名；兼容旧字段名。</param>
internal sealed record CadDescriptor(
    string Brand,
    string Version,
    string DisplayName,
    string RegistryBasePath,
    string AppName,
    string EmbeddedResourceKey);

/// <summary>
/// 所有受支持 CAD 版本的元数据表（运行时由部署器同目录的 <c>*.cad.json</c> Sidecar 自动加载）。
/// <para>
/// 注册表配置文件子键模式固定为 <c>^ACAD-[A-Za-z0-9]+:[A-Za-z0-9]+$</c>，
/// 由 <see cref="Services.CadRegistryScanner"/> 直接使用常量，不在此处重复声明。
/// </para>
/// </summary>
internal static class CadDescriptors
{
    /// <summary>JSON Sidecar 文件后缀。</summary>
    private const string SidecarSuffix = ".cad.json";

    /// <summary>按 (品牌, 版本) 升序排列的所有支持版本。</summary>
    internal static readonly IReadOnlyList<CadDescriptor> All = LoadFromSidecarFiles();

    private static IReadOnlyList<CadDescriptor> LoadFromSidecarFiles()
    {
        var list = new List<CadDescriptor>();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling  = JsonCommentHandling.Skip,
            AllowTrailingCommas  = true,
        };

        var baseDirectory = AppContext.BaseDirectory;
        if (!Directory.Exists(baseDirectory))
        {
            return list;
        }

        foreach (var path in Directory.EnumerateFiles(baseDirectory, "*" + SidecarSuffix, SearchOption.TopDirectoryOnly))
        {
            CadDescriptorDto? dto;
            try
            {
                using var stream = File.OpenRead(path);
                dto = JsonSerializer.Deserialize<CadDescriptorDto>(stream, options);
            }
            catch
            {
                continue;
            }

            if (dto is null)
            {
                continue;
            }

            var appName = dto.AppName ?? string.Empty;
            var dllFileName = Path.GetFileName(dto.EmbeddedResourceKey ?? string.Empty);
            if (string.IsNullOrWhiteSpace(dllFileName))
            {
                dllFileName = appName + ".dll";
            }

            list.Add(new CadDescriptor(
                Brand:               dto.Brand               ?? string.Empty,
                Version:             dto.Version             ?? string.Empty,
                DisplayName:         dto.DisplayName         ?? $"{dto.Brand} {dto.Version}",
                RegistryBasePath:    dto.RegistryBasePath    ?? string.Empty,
                AppName:             appName,
                EmbeddedResourceKey: dllFileName));
        }

        return list
            .OrderBy(d => d.Brand,   StringComparer.OrdinalIgnoreCase)
            .ThenBy (d => d.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>JSON Sidecar 反序列化 DTO（与 EmitCadDescriptorJson Target 输出字段一致）。</summary>
    private sealed class CadDescriptorDto
    {
        [JsonPropertyName("brand")]               public string? Brand               { get; set; }
        [JsonPropertyName("version")]             public string? Version             { get; set; }
        [JsonPropertyName("displayName")]         public string? DisplayName         { get; set; }
        [JsonPropertyName("registryBasePath")]    public string? RegistryBasePath    { get; set; }
        [JsonPropertyName("appName")]             public string? AppName             { get; set; }
        [JsonPropertyName("embeddedResourceKey")] public string? EmbeddedResourceKey { get; set; }
    }
}
