using Microsoft.Win32;
using System.Reflection;
using System.Text.RegularExpressions;
using AFR.Platform;
using AFR.Services;

namespace AFR.Hosting;

/// <summary>
/// 处理插件的首次注册表初始化、自动加载键值设置以及默认文件配置创建。
/// <para>
/// 在 AutoCAD 注册表中为每个匹配的 CAD 配置文件创建自动加载条目，
/// 使插件在 CAD 启动时自动加载。所有写入操作均为幂等的，不会重复覆盖已有值。
/// </para>
/// </summary>
internal static class AppInitializer
{
    // 从 PlatformManager 获取当前平台的注册表路径信息
    private static string AutoCadBasePath => PlatformManager.Platform.RegistryBasePath;
    private static string AppName => PlatformManager.Platform.AppName;

    // 注册表中自动加载条目的固定值
    private const string Description = "AFR Auto Replace Font Plugin";
    private const int LoadCtrls = 2;   // 2 = 随 AutoCAD 启动自动加载
    private const int Managed = 1;     // 1 = 标识为托管 .NET 插件
    private const string PluginVersionValueName = "PluginVersion";
    private const string PluginBuildIdValueName = "PluginBuildId";

    /// <summary>
    /// 执行注册表初始化：为所有匹配的 CAD 配置文件创建/更新自动加载条目。
    /// </summary>
    /// <returns>true 表示首次安装（至少一个配置文件是新建的），false 表示更新已有配置。</returns>
    public static bool Initialize()
    {
        var log = LogService.Instance;
        int initializedProfiles = 0;
        int newProfiles = 0;
        try
        {
            var dllPath = GetCurrentDllPath();

            var profiles = GetAcadProfiles();
            if (profiles.Count == 0)
            {
                var versionTag = AutoCadBasePath.Substring(AutoCadBasePath.LastIndexOf('\\') + 1);
                DiagnosticLogger.Skip(
                    "AppInitializer",
                    "GetAcadProfiles",
                    "未找到有效的 AutoCAD 配置文件",
                    new Dictionary<string, object?> { ["versionTag"] = versionTag });
                return false;
            }

            foreach (var profile in profiles)
            {
                var appPath = $@"{AutoCadBasePath}\{profile}\Applications\{AppName}";
                initializedProfiles++;
                if (InitializeProfile(appPath, dllPath))
                    newProfiles++;
            }

            #if AFR_EXTERNAL_REGISTRY
            // 应用 [assembly: RegistryDefaultDwordAt(...)] 声明的外部默认值（默认禁用）。
            // 定义 AFR_EXTERNAL_REGISTRY 则 NETLOAD 与部署工具共用同一份声明。
            ExternalRegistryDefaultsApplier.Apply();
            #endif

            // 抑制 AutoCAD“缺少 SHX 文件”弹窗：写入 FixedProfile.aws。
            // 仅在 AutoCAD 未运行时生效；NETLOAD 现场加载会被 Apply 内部进程检查拒绝。
            try { Diagnostics.AwsHideableDialogPatcher.Apply(); } catch { }
        }
        catch (Exception ex)
        {
            log.Error("初始化失败", ex);
        }

        return initializedProfiles > 0 && newProfiles == initializedProfiles;
    }

    /// <summary>
    /// 初始化单个 CAD 配置文件的注册表项。
    /// 写入自动加载所需的键值（LOADER、LOADCTRLS 等），首次创建时还会写入默认配置。
    /// </summary>
    /// <param name="appPath">该配置文件对应的完整注册表路径。</param>
    /// <param name="dllPath">插件 DLL 的完整文件路径。</param>
    /// <returns>true 表示是首次创建（之前不存在该注册表键）。</returns>
    private static bool InitializeProfile(string appPath, string dllPath)
    {
        bool isNewKey = !RegistryService.KeyExists(Registry.CurrentUser, appPath);
        string currentPluginVersion = PluginVersionService.GetDisplayVersion();
        string currentBuildId = PluginVersionService.GetBuildId();
        string? installedPluginVersion = RegistryService.ReadString(Registry.CurrentUser, appPath, PluginVersionValueName);
        string? installedBuildId = RegistryService.ReadString(Registry.CurrentUser, appPath, PluginBuildIdValueName);

        // 自动加载协议键值（幂等写入，仅在值与预期不同时才写入注册表）。
        // 字体配置属于绿色目录中的 AFR.config.json，不再写入 Applications 注册表键。
        WriteIfChanged(appPath, "LOADER", dllPath);
        WriteIfChanged(appPath, "LOADCTRLS", LoadCtrls);
        WriteIfChanged(appPath, "MANAGED", Managed);
        WriteIfChanged(appPath, "DESCRIPTION", Description);
        WriteIfChanged(appPath, PluginVersionValueName, currentPluginVersion);
        WriteIfChanged(appPath, PluginBuildIdValueName, currentBuildId);

        EnsureDefaultFileConfiguration(appPath);

        if (!string.Equals(installedPluginVersion, currentPluginVersion, StringComparison.Ordinal)
              || !string.Equals(installedBuildId, currentBuildId, StringComparison.Ordinal))
        {
            DiagnosticLogger.Ok(
                "AppInitializer",
                "InitializeProfile",
                "插件版本已更新",
                new Dictionary<string, object?>
                {
                    ["appPath"] = appPath,
                    ["fromPluginVersion"] = installedPluginVersion,
                    ["fromBuildId"] = installedBuildId,
                    ["toPluginVersion"] = currentPluginVersion,
                    ["toBuildId"] = currentBuildId
                });
        }
        return isNewKey;
    }

    /// <summary>
    /// 确保默认 SHX 字体和绿色配置文件可用；注册表仅作为旧版本迁移来源读取。
    /// </summary>
    private static void EnsureDefaultFileConfiguration(string appPath)
    {
        bool deployed = EmbeddedFontDeployer.Deploy();
        var config = ConfigService.Instance;
        if (string.IsNullOrWhiteSpace(config.MainFont))
            config.MainFont = EmbeddedFontDeployer.DefaultMainFont;
        if (string.IsNullOrWhiteSpace(config.BigFont))
            config.BigFont = EmbeddedFontDeployer.DefaultBigFont;
        if (string.IsNullOrWhiteSpace(config.TrueTypeFont))
            config.TrueTypeFont = EmbeddedFontDeployer.DefaultTrueTypeFont;
        if (deployed && !config.IsInitialized)
            config.IsInitialized = true;

        DiagnosticLogger.Ok(
            "AppInitializer",
            "EnsureDefaultFileConfiguration",
            "绿色配置文件已就绪",
            new Dictionary<string, object?>
            {
                ["appPath"] = appPath,
                ["configPath"] = config.ConfigPath,
                ["fontsDeployed"] = deployed
            });
    }

    /// <summary>仅在注册表中的当前值与目标值不同时才写入（字符串版本）。</summary>
    private static void WriteIfChanged(string appPath, string name, string value)
    {
        var current = RegistryService.ReadString(Registry.CurrentUser, appPath, name);
        if (!string.Equals(current, value, StringComparison.Ordinal))
        {
            RegistryService.WriteString(Registry.CurrentUser, appPath, name, value);
        }
    }

    /// <summary>仅在注册表中的当前值与目标值不同时才写入（DWORD 版本）。</summary>
    private static void WriteIfChanged(string appPath, string name, int value)
    {
        var current = RegistryService.ReadDword(Registry.CurrentUser, appPath, name);
        if (current != value)
        {
            RegistryService.WriteDword(Registry.CurrentUser, appPath, name, value);
        }
    }

    /// <summary>
    /// 枚举注册表中与当前 CAD 版本匹配的所有配置文件子键名。
    /// </summary>
    private static List<string> GetAcadProfiles()
    {
        var results = new List<string>();
        var pattern = new Regex(PlatformManager.Platform.RegistryKeyPattern, RegexOptions.Compiled);
        var subKeyNames = RegistryService.GetSubKeyNames(Registry.CurrentUser, AutoCadBasePath);
        foreach (var name in subKeyNames)
        {
            if (pattern.IsMatch(name))
            {
                results.Add(name);
            }
        }
        return results;
    }

    /// <summary>获取当前正在执行的插件 DLL 的完整文件路径。</summary>
    private static string GetCurrentDllPath()
    {
        return Assembly.GetExecutingAssembly().Location;
    }
}
