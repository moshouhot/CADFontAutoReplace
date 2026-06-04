using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AFR.Platform;

namespace AFR.Services;

/// <summary>
/// 业务配置服务。绿色版优先把配置保存到插件 DLL 所在目录的 <c>AFR.config.json</c>；
/// 若目录不可写，则回退到用户 AppData。注册表仅作为旧版本配置的一次性读取来源。
/// </summary>
public sealed class ConfigService
{
    private const string ConfigFileName = "AFR.config.json";

    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
    public static ConfigService Instance => _instance.Value;

    private static string AutoCadBasePath => PlatformManager.Platform.RegistryBasePath;
    private static string AppName => PlatformManager.Platform.AppName;
    private static string KeyPattern => PlatformManager.Platform.RegistryKeyPattern;

    private Regex? _keyPatternRegex;
    private Regex KeyPatternRegex => _keyPatternRegex ??= new Regex(KeyPattern, RegexOptions.Compiled);

    private string? _mainFont;
    private string? _bigFont;
    private string? _trueTypeFont;
    private int? _isInitialized;
    private string? _configPath;
    private volatile bool _cacheLoaded;
#if NET9_0_OR_GREATER
    private readonly System.Threading.Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    private List<string>? _resolvedAppPaths;

    private ConfigService() { }

    /// <summary>当前实际使用的配置文件路径。</summary>
    public string ConfigPath
    {
        get
        {
            EnsureCacheLoaded();
            return _configPath ?? ResolveConfigPath(preferExisting: true);
        }
    }

    public IReadOnlyList<string> GetAllApplicationPaths()
    {
        var cached = _resolvedAppPaths;
        if (cached != null) return cached;

        lock (_lock)
        {
            if (_resolvedAppPaths != null) return _resolvedAppPaths;

            var results = new List<string>();
            var subKeyNames = RegistryService.GetSubKeyNames(Registry.CurrentUser, AutoCadBasePath);
            foreach (var name in subKeyNames)
            {
                if (KeyPatternRegex.IsMatch(name))
                {
                    results.Add($@"{AutoCadBasePath}\{name}\Applications\{AppName}");
                }
            }

            _resolvedAppPaths = results;
            return results;
        }
    }

    public string? GetPrimaryApplicationPath()
    {
        var paths = GetAllApplicationPaths();
        return paths.Count > 0 ? paths[0] : null;
    }

    private void EnsureCacheLoaded()
    {
        if (_cacheLoaded) return;
        lock (_lock)
        {
            if (_cacheLoaded) return;

            _configPath = ResolveConfigPath(preferExisting: true);
            if (TryLoadFromFile(_configPath))
            {
                _cacheLoaded = true;
                return;
            }

            LoadLegacyRegistryConfig();
            _cacheLoaded = true;
        }
    }

    public string MainFont
    {
        get
        {
            EnsureCacheLoaded();
            return _mainFont ?? string.Empty;
        }
        set
        {
            EnsureCacheLoaded();
            lock (_lock) { _mainFont = value ?? string.Empty; SaveLocked(); }
        }
    }

    public string BigFont
    {
        get
        {
            EnsureCacheLoaded();
            return _bigFont ?? string.Empty;
        }
        set
        {
            EnsureCacheLoaded();
            lock (_lock) { _bigFont = value ?? string.Empty; SaveLocked(); }
        }
    }

    public string TrueTypeFont
    {
        get
        {
            EnsureCacheLoaded();
            return _trueTypeFont ?? string.Empty;
        }
        set
        {
            EnsureCacheLoaded();
            lock (_lock) { _trueTypeFont = value ?? string.Empty; SaveLocked(); }
        }
    }

    public bool IsInitialized
    {
        get
        {
            EnsureCacheLoaded();
            return (_isInitialized ?? 0) == 1;
        }
        set
        {
            EnsureCacheLoaded();
            lock (_lock) { _isInitialized = value ? 1 : 0; SaveLocked(); }
        }
    }

    public void InvalidateCache()
    {
        lock (_lock)
        {
            _cacheLoaded = false;
            _mainFont = null;
            _bigFont = null;
            _trueTypeFont = null;
            _isInitialized = null;
            _configPath = null;
            _resolvedAppPaths = null;
        }
    }

    public int DeleteAllApplicationKeys()
    {
        int deletedCount = 0;
        if (string.IsNullOrWhiteSpace(AppName) || AppName.Contains('\\'))
            return deletedCount;

        var subKeyNames = RegistryService.GetSubKeyNames(Registry.CurrentUser, AutoCadBasePath);
        foreach (var name in subKeyNames)
        {
            if (!KeyPatternRegex.IsMatch(name)) continue;

            var appKeyPath = $@"{AutoCadBasePath}\{name}\Applications\{AppName}";
            if (!RegistryService.KeyExists(Registry.CurrentUser, appKeyPath)) continue;
            if (RegistryService.DeleteSubKeyTree(Registry.CurrentUser, appKeyPath))
                deletedCount++;
        }

        InvalidateCache();
        return deletedCount;
    }

    private bool TryLoadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;

            var text = File.ReadAllText(path, Encoding.UTF8);
            _mainFont = ReadJsonString(text, "mainFont");
            _bigFont = ReadJsonString(text, "bigFont");
            _trueTypeFont = ReadJsonString(text, "trueTypeFont");
            _isInitialized = ReadJsonBool(text, "isInitialized") ? 1 : 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadLegacyRegistryConfig()
    {
        var path = GetPrimaryApplicationPath();
        if (path == null) return;

        _mainFont = RegistryService.ReadString(Registry.CurrentUser, path, "MainFont");
        _bigFont = RegistryService.ReadString(Registry.CurrentUser, path, "BigFont");
        _trueTypeFont = RegistryService.ReadString(Registry.CurrentUser, path, "TrueTypeFont");
        _isInitialized = RegistryService.ReadDword(Registry.CurrentUser, path, "IsInitialized");
    }

    private void SaveLocked()
    {
        var path = _configPath ?? ResolveConfigPath(preferExisting: false);
        if (TrySave(path))
        {
            _configPath = path;
            return;
        }

        var fallback = GetAppDataConfigPath();
        if (TrySave(fallback))
        {
            _configPath = fallback;
        }
    }

    private bool TrySave(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, BuildJson(), Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildJson()
        => "{\n"
           + $"  \"mainFont\": \"{EscapeJson(_mainFont ?? string.Empty)}\",\n"
           + $"  \"bigFont\": \"{EscapeJson(_bigFont ?? string.Empty)}\",\n"
           + $"  \"trueTypeFont\": \"{EscapeJson(_trueTypeFont ?? string.Empty)}\",\n"
           + $"  \"isInitialized\": {((_isInitialized ?? 0) == 1 ? "true" : "false")}\n"
           + "}\n";

    private static string ResolveConfigPath(bool preferExisting)
    {
        var dllDir = GetAssemblyDirectory();
        var dllPath = Path.Combine(dllDir, ConfigFileName);
        if (preferExisting && File.Exists(dllPath))
            return dllPath;

        var appDataPath = GetAppDataConfigPath();
        if (preferExisting && File.Exists(appDataPath))
            return appDataPath;

        return IsDirectoryWritable(dllDir) ? dllPath : appDataPath;
    }

    private static string GetAssemblyDirectory()
    {
        try
        {
            var location = Assembly.GetExecutingAssembly().Location;
            var dir = string.IsNullOrWhiteSpace(location) ? null : Path.GetDirectoryName(location);
            if (!string.IsNullOrWhiteSpace(dir))
                return dir!;
        }
        catch { }

        return AppContext.BaseDirectory;
    }

    private static string GetAppDataConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "AFR-CADFontAutoReplace", ConfigFileName);
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var testFile = Path.Combine(directory, ".afr-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(testFile, string.Empty);
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadJsonString(string text, string name)
    {
        var match = Regex.Match(
            text,
            $"\"{Regex.Escape(name)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
            RegexOptions.IgnoreCase);

        return match.Success ? UnescapeJson(match.Groups["value"].Value) : string.Empty;
    }

    private static bool ReadJsonBool(string text, string name)
    {
        var match = Regex.Match(
            text,
            $"\"{Regex.Escape(name)}\"\\s*:\\s*(?<value>true|false|1|0)",
            RegexOptions.IgnoreCase);

        if (!match.Success) return false;
        var value = match.Groups["value"].Value;
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeJson(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append(@"\r"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\t': sb.Append(@"\t"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string UnescapeJson(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch != '\\' || i + 1 >= value.Length)
            {
                sb.Append(ch);
                continue;
            }

            var next = value[++i];
            if (next == 'u' && i + 4 < value.Length)
            {
                var hex = value.Substring(i + 1, 4);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out var codePoint))
                {
                    sb.Append((char)codePoint);
                    i += 4;
                    continue;
                }
            }

            sb.Append(next switch
            {
                '\\' => '\\',
                '"' => '"',
                'r' => '\r',
                'n' => '\n',
                't' => '\t',
                'b' => '\b',
                'f' => '\f',
                _ => next
            });
        }
        return sb.ToString();
    }
}
