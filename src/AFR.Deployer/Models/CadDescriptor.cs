namespace AFR.Deployer.Models;

/// <summary>
/// 编译期已知的 CAD 版本静态描述符，与 ICadPlatform 对应但不依赖插件程序集。
/// </summary>
/// <param name="Brand">CAD 品牌，如 "AutoCAD"。</param>
/// <param name="Version">CAD 版本年份，如 "2025"。</param>
/// <param name="DisplayName">UI 显示名称，如 "AutoCAD 2025"。</param>
/// <param name="RegistryBasePath">注册表基路径，如 <c>Software\Autodesk\AutoCAD\R25.0</c>。</param>
/// <param name="AppName">注册表 Applications 子键名，如 "AFR-ACAD2025"。</param>
/// <param name="PluginFileName">绿色目录中的插件 DLL 文件名。</param>
internal sealed record CadDescriptor(
    string Brand,
    string Version,
    string DisplayName,
    string RegistryBasePath,
    string AppName,
    string PluginFileName);

/// <summary>
/// 所有受支持 CAD 版本的元数据表。
/// <para>
/// 注册表配置文件子键模式固定为 <c>^ACAD-[A-Za-z0-9]+:[A-Za-z0-9]+$</c>，
/// 由 <see cref="Services.CadRegistryScanner"/> 直接使用常量，不在此处重复声明。
/// 新增 AutoCAD 版本时在这里追加注册表基路径和对应绿色 DLL 文件名。
/// </para>
/// </summary>
internal static class CadDescriptors
{
    /// <summary>按 (品牌, 版本) 升序排列的所有支持版本。</summary>
    internal static readonly IReadOnlyList<CadDescriptor> All =
    [
        AutoCad("2013", "R19.0", "AFR-ACAD2013-2017"),
        AutoCad("2014", "R19.1", "AFR-ACAD2013-2017"),
        AutoCad("2015", "R20.0", "AFR-ACAD2013-2017"),
        AutoCad("2016", "R20.1", "AFR-ACAD2013-2017"),
        AutoCad("2017", "R21.0", "AFR-ACAD2013-2017"),
        AutoCad("2018", "R22.0", "AFR-ACAD2018-2024"),
        AutoCad("2019", "R23.0", "AFR-ACAD2018-2024"),
        AutoCad("2020", "R23.1", "AFR-ACAD2018-2024"),
        AutoCad("2021", "R24.0", "AFR-ACAD2018-2024"),
        AutoCad("2022", "R24.1", "AFR-ACAD2018-2024"),
        AutoCad("2023", "R24.2", "AFR-ACAD2018-2024"),
        AutoCad("2024", "R24.3", "AFR-ACAD2018-2024"),
        AutoCad("2025", "R25.0", "AFR-ACAD2025-2026"),
        AutoCad("2026", "R25.1", "AFR-ACAD2025-2026"),
        AutoCad("2027", "R26.0", "AFR-ACAD2027"),
    ];

    private static CadDescriptor AutoCad(string version, string registryVersion, string appName)
        => new(
            Brand: "AutoCAD",
            Version: version,
            DisplayName: "AutoCAD " + version,
            RegistryBasePath: @"Software\Autodesk\AutoCAD\" + registryVersion,
            AppName: appName,
            PluginFileName: appName + ".dll");
}
