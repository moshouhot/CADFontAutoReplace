using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using AFR.Services;

namespace AFR.Deployer.Services;

internal sealed record DeployerFontConfig(
    string MainFont,
    string BigFont,
    string TrueTypeFont,
    bool IsInitialized);

/// <summary>
/// 绿色部署器的字体配置与同目录 Fonts 文件夹访问服务。
/// </summary>
internal static class DeployerFontConfigService
{
    internal const string ConfigFileName = "AFR.config.json";
    internal const string FontsDirectoryName = "Fonts";

    internal const string DefaultMainFont = "ming.shx";
    internal const string DefaultBigFont = "tssdchn.shx";
    internal const string DefaultTrueTypeFont = "宋体";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static string GreenDirectory => AppContext.BaseDirectory;

    internal static string FontsDirectory => Path.Combine(GreenDirectory, FontsDirectoryName);

    internal static string ConfigPath => Path.Combine(GreenDirectory, ConfigFileName);

    internal static DeployerFontConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<ConfigDto>(File.ReadAllText(ConfigPath), JsonOptions);
                if (config is not null)
                {
                    return Normalize(new DeployerFontConfig(
                        config.MainFont ?? string.Empty,
                        config.BigFont ?? string.Empty,
                        config.TrueTypeFont ?? string.Empty,
                        config.IsInitialized));
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值，保存时会重写为有效 JSON。
        }

        return Defaults();
    }

    internal static void Save(DeployerFontConfig config)
    {
        Directory.CreateDirectory(GreenDirectory);
        Directory.CreateDirectory(FontsDirectory);

        var normalized = Normalize(config) with { IsInitialized = true };
        var dto = new ConfigDto
        {
            MainFont = normalized.MainFont,
            BigFont = normalized.BigFont,
            TrueTypeFont = normalized.TrueTypeFont,
            IsInitialized = normalized.IsInitialized,
        };

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(dto, JsonOptions) + Environment.NewLine);
    }

    internal static IReadOnlyList<string> ScanShxFonts()
    {
        try
        {
            if (!Directory.Exists(FontsDirectory))
                return [DefaultMainFont, DefaultBigFont];

            var fonts = Directory.EnumerateFiles(FontsDirectory, "*.shx", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AddIfMissing(fonts, DefaultMainFont);
            AddIfMissing(fonts, DefaultBigFont);
            return fonts;
        }
        catch
        {
            return [DefaultMainFont, DefaultBigFont];
        }
    }

    internal static IReadOnlyList<string> ScanMainShxFonts()
    {
        try
        {
            var fonts = ScanShxFontsByKind(isBigFont: false);
            AddIfMissing(fonts, DefaultMainFont);
            return fonts;
        }
        catch
        {
            return [DefaultMainFont];
        }
    }

    internal static IReadOnlyList<string> ScanBigShxFonts()
    {
        try
        {
            var fonts = ScanShxFontsByKind(isBigFont: true);
            AddIfMissing(fonts, DefaultBigFont);
            return fonts;
        }
        catch
        {
            return [DefaultBigFont];
        }
    }

    internal static IReadOnlyList<string> ScanTrueTypeFonts()
    {
        try
        {
            var fonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            AddIfMissing(fonts, DefaultTrueTypeFont);
            return fonts;
        }
        catch
        {
            return [DefaultTrueTypeFont];
        }
    }

    internal static string? FindGreenFontFile(string fontFileName)
    {
        if (string.IsNullOrWhiteSpace(fontFileName)) return null;

        var fileName = Path.GetFileName(fontFileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        try
        {
            var path = Path.Combine(FontsDirectory, fileName);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static DeployerFontConfig Defaults()
        => new(DefaultMainFont, DefaultBigFont, DefaultTrueTypeFont, true);

    private static List<string> ScanShxFontsByKind(bool isBigFont)
    {
        if (!Directory.Exists(FontsDirectory))
            return [];

        return Directory.EnumerateFiles(FontsDirectory, "*.shx", SearchOption.TopDirectoryOnly)
            .Where(path => ShxFontAnalyzer.IsBigFont(path) == isBigFont)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DeployerFontConfig Normalize(DeployerFontConfig config)
        => new(
            NormalizeFileName(config.MainFont, DefaultMainFont),
            NormalizeFileName(config.BigFont, DefaultBigFont),
            string.IsNullOrWhiteSpace(config.TrueTypeFont) ? DefaultTrueTypeFont : config.TrueTypeFont.Trim(),
            config.IsInitialized);

    private static string NormalizeFileName(string value, string fallback)
    {
        var fileName = Path.GetFileName((value ?? string.Empty).Trim());
        return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
    }

    private static void AddIfMissing(List<string> list, string value)
    {
        if (!list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            list.Insert(0, value);
    }

    private sealed class ConfigDto
    {
        [JsonPropertyName("mainFont")]
        public string? MainFont { get; set; }

        [JsonPropertyName("bigFont")]
        public string? BigFont { get; set; }

        [JsonPropertyName("trueTypeFont")]
        public string? TrueTypeFont { get; set; }

        [JsonPropertyName("isInitialized")]
        public bool IsInitialized { get; set; }
    }
}
