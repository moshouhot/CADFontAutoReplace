using System.IO;
using AFR.Deployer.Models;
using AFR.HostIntegration;
using Microsoft.Win32;

namespace AFR.Deployer.Services;

/// <summary>
/// 部署器侧的 SHX 字体释放器。
/// <para>
/// 通过注册表（先 HKLM 再 HKCU）解析每个 CAD 配置文件实例的 <c>AcadLocation</c>，
/// 拼出 <c>&lt;AcadLocation&gt;\Fonts</c> 后，优先从绿色目录 <c>Fonts\</c>
/// 复制当前配置的 SHX 字体；默认字体缺失时再回退到内嵌资源。
/// </para>
/// <para>
/// 与 NETLOAD 路径下 <c>AFR.Hosting.EmbeddedFontDeployer</c> 行为一致：
/// 已存在同名文件一律跳过，不覆盖、不删除任何文件，纯增量。
/// 调用方应在确认 CAD 已关闭、注册表写入完成后再触发。
/// </para>
/// </summary>
internal static class EmbeddedFontPatcher
{
    private const string AcadLocationValueName = "AcadLocation";
    private const string FontsSubDirectory     = "Fonts";

    /// <summary>
    /// 对指定 CAD 版本下所有配置文件实例释放内嵌字体。任何 IO/注册表异常一律视为本次跳过（不抛出）。
    /// </summary>
    /// <returns>true 表示字体已就绪（释放成功或全部已存在）；false 表示无法定位 Fonts 目录或至少一个文件释放失败。</returns>
    public static bool Apply(CadInstallation installation)
    {
        if (!installation.IsCadInstalled) return false;

        var fontDirs = installation.ProfileSubKeys
            .Select(profileSubKey => ResolveFontsDirectory(installation, profileSubKey))
            .Where(fontsDir => fontsDir is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fontDirs.Count == 0) return false;

        var config = DeployerFontConfigService.Load();
        var assembly = typeof(EmbeddedFontPatcher).Assembly;
        return fontDirs.All(fontsDir => CopyConfiguredFonts(config, assembly, fontsDir!, out _));
    }

    private static bool CopyConfiguredFonts(DeployerFontConfig config, System.Reflection.Assembly fallbackAssembly, string targetDirectory, out string? errorMessage)
    {
        errorMessage = null;
        var fontFiles = new[]
        {
            config.MainFont,
            config.BigFont,
        }
        .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
        .Select(fileName => Path.GetFileName(fileName.Trim()))
        .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        if (fontFiles.Count == 0)
            return true;

        bool allSuccess = true;
        foreach (var fileName in fontFiles)
        {
            var targetPath = Path.Combine(targetDirectory, fileName!);
            if (File.Exists(targetPath)) continue;

            var sourcePath = DeployerFontConfigService.FindGreenFontFile(fileName!);
            if (sourcePath is not null)
            {
                try
                {
                    File.Copy(sourcePath, targetPath, overwrite: false);
                    continue;
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    continue;
                }
                catch (Exception ex)
                {
                    allSuccess = false;
                    errorMessage ??= $"复制 {fileName} 到 {targetDirectory} 失败：{ex.Message}";
                    continue;
                }
            }

            if (IsEmbeddedDefaultFont(fileName!)
                && EmbeddedFontExtractor.ExtractOne(fallbackAssembly, EmbeddedFontExtractor.ResourcePrefix + fileName, targetPath, out var err))
            {
                continue;
            }

            allSuccess = false;
            errorMessage ??= sourcePath is null
                ? $"绿色目录 Fonts 中未找到配置字体：{fileName}"
                : $"释放 {fileName} 失败";
        }

        return allSuccess;
    }

    private static bool IsEmbeddedDefaultFont(string fileName)
        => EmbeddedFontExtractor.EmbeddedFontFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 从注册表解析指定配置文件实例的 <c>&lt;AcadLocation&gt;\Fonts</c>。
    /// AutoCAD 把安装路径写在每个配置文件子键里：先尝试 HKLM（标准安装），
    /// 不存在再回退 HKCU（少数版本或便携安装）。任何失败都返回 null。
    /// </summary>
    private static string? ResolveFontsDirectory(CadInstallation installation, string profileSubKey)
    {
        var subPath = installation.GetProfileRootPath(profileSubKey);

        var acadLocation = ReadString(Registry.LocalMachine, subPath, AcadLocationValueName)
                        ?? ReadString(Registry.CurrentUser, subPath, AcadLocationValueName);

        if (string.IsNullOrWhiteSpace(acadLocation)) return null;

        try
        {
            var fontsDir = Path.Combine(acadLocation, FontsSubDirectory);
            return Directory.Exists(fontsDir) ? fontsDir : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
