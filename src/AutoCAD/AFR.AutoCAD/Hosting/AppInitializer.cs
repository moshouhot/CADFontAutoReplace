using Microsoft.Win32;
using System.Reflection;
using System.Text.RegularExpressions;
using AFR.Platform;
using AFR.Services;

namespace AFR.Hosting;

internal enum PluginInitializationState
{
    NormalRun = 0,
    CompletingStagedInstall = 1,
    Updated = 2,
    FirstInstall = 3,
}

internal sealed class PluginInitializationResult(
    PluginInitializationState state,
    bool awsSuppressionWarningShown)
{
    public PluginInitializationState State { get; } = state;

    public bool AwsSuppressionWarningShown { get; } = awsSuppressionWarningShown;

    public bool IsFirstInstall => State == PluginInitializationState.FirstInstall;

    public bool IsInstallOrUpdate => State is PluginInitializationState.FirstInstall
                                           or PluginInitializationState.Updated;

    public bool ShouldCheckAwsSuppression => IsInstallOrUpdate && !AwsSuppressionWarningShown;

    public bool ShouldSkipRuntimeStartup => State == PluginInitializationState.FirstInstall;
}

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
    private const string ConfigSchemaVersionValueName = "ConfigSchemaVersion";
    private const string AwsSuppressionWarningShownValueName = "AwsSuppressionWarningShown";

    /// <summary>
    /// 执行注册表初始化：为所有匹配的 CAD 配置文件创建/更新自动加载条目。
    /// </summary>
    /// <returns>本次初始化聚合状态。</returns>
    public static PluginInitializationResult Initialize()
    {
        var log = LogService.Instance;
        var state = PluginInitializationState.NormalRun;
        var awsSuppressionWarningShownForAllProfiles = true;
        try
        {
            var dllPath = GetCurrentDllPath();

            var profiles = GetAcadProfiles();
            if (profiles.Count == 0)
            {
                var versionTag = AutoCadBasePath[(AutoCadBasePath.LastIndexOf('\\') + 1)..];
                DiagnosticLogger.Skip(
                    "AppInitializer",
                    "GetAcadProfiles",
                    "未找到有效的 AutoCAD 配置文件",
                    new Dictionary<string, object?> { ["versionTag"] = versionTag });
                return new PluginInitializationResult(state, awsSuppressionWarningShownForAllProfiles);
            }

            foreach (var profile in profiles)
            {
                var appPath = $@"{AutoCadBasePath}\{profile}\Applications\{AppName}";
                var profileResult = InitializeProfile(appPath, dllPath);
                state = MaxState(state, profileResult.State);
                awsSuppressionWarningShownForAllProfiles &= profileResult.AwsSuppressionWarningShown;
            }

            #if AFR_EXTERNAL_REGISTRY
            // 应用 [assembly: RegistryDefaultDwordAt(...)] 声明的外部默认值（默认禁用）。
            // 定义 AFR_EXTERNAL_REGISTRY 则 NETLOAD 与部署工具共用同一份声明。
            ExternalRegistryDefaultsApplier.Apply();
            #endif
        }
        catch (Exception ex)
        {
            log.Error("初始化失败", ex);
        }
        return new PluginInitializationResult(state, awsSuppressionWarningShownForAllProfiles);
    }

    /// <summary>
    /// 初始化单个 CAD 配置文件的注册表项。
    /// 写入自动加载所需的键值（LOADER、LOADCTRLS 等），首次创建时还会写入默认配置。
    /// </summary>
    /// <param name="appPath">该配置文件对应的完整注册表路径。</param>
    /// <param name="dllPath">插件 DLL 的完整文件路径。</param>
    /// <returns>该配置文件的初始化状态。</returns>
    private static ProfileInitializationResult InitializeProfile(string appPath, string dllPath)
    {
        bool isNewKey = !RegistryService.KeyExists(Registry.CurrentUser, appPath);
        string currentPluginVersion = PluginVersionService.GetDisplayVersion();
        string currentBuildId = PluginVersionService.GetBuildId();
        string? installedPluginVersion = RegistryService.ReadString(Registry.CurrentUser, appPath, PluginVersionValueName);
        string? installedBuildId = RegistryService.ReadString(Registry.CurrentUser, appPath, PluginBuildIdValueName);
        bool awsSuppressionWarningShown = RegistryService.ReadDword(Registry.CurrentUser, appPath, AwsSuppressionWarningShownValueName) == 1;
        bool versionChanged = !isNewKey
                           && (!string.Equals(installedPluginVersion, currentPluginVersion, StringComparison.Ordinal)
                            || !string.Equals(installedBuildId, currentBuildId, StringComparison.Ordinal));
        var state = isNewKey
            ? PluginInitializationState.FirstInstall
            : versionChanged
                ? PluginInitializationState.Updated
                : PluginInitializationState.NormalRun;

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

        DiagnosticLogger.Ok(
            "AppInitializer",
            "InitializeProfile",
            "配置文件初始化状态已判定",
            new Dictionary<string, object?>
            {
                ["appPath"] = appPath,
                ["state"] = state.ToString(),
                ["awsSuppressionWarningShown"] = awsSuppressionWarningShown
            });
        return new ProfileInitializationResult(state, awsSuppressionWarningShown);
    }

    private static PluginInitializationState MaxState(PluginInitializationState left, PluginInitializationState right)
        => (PluginInitializationState)Math.Max((int)left, (int)right);

    /// <summary>标记缺失 SHX 弹窗抑制提示已经输出过。</summary>
    public static void MarkAwsSuppressionWarningShown()
    {
        foreach (var appPath in GetAppPaths())
        {
            RegistryService.WriteDword(Registry.CurrentUser, appPath, AwsSuppressionWarningShownValueName, 1);
        }
    }

    private static IEnumerable<string> GetAppPaths()
    {
        foreach (var profile in GetAcadProfiles())
            yield return $@"{AutoCadBasePath}\{profile}\Applications\{AppName}";
    }

    private readonly struct ProfileInitializationResult(
        PluginInitializationState state,
        bool awsSuppressionWarningShown)
    {
        public PluginInitializationState State { get; } = state;
        public bool AwsSuppressionWarningShown { get; } = awsSuppressionWarningShown;
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
