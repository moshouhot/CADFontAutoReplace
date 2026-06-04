using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using AFR.Deployer.Models;
using AFR.HostIntegration;

namespace AFR.Deployer.Services;

internal static class DeployerCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static int Run(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return 0;

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "--help" or "-h" or "help" => WriteHelp(),
                "--version" or "version" => WriteJson(new
                {
                    ok = true,
                    version = DeployerVersionService.GetDisplayVersion(),
                    buildId = DeployerVersionService.GetBuildId(),
                }),
                "detect" => Detect(),
                "status" => Detect(),
                "doctor" => Doctor(),
                "fonts" => Fonts(rest),
                "config" => Config(rest),
                "install" => Install(rest),
                "uninstall" => Uninstall(rest),
                _ => Fail($"未知命令：{args[0]}", exitCode: 2),
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, exitCode: 1);
        }
    }

    private static int Detect()
    {
        var installations = CadRegistryScanner.Scan();
        return WriteJson(new
        {
            ok = true,
            deployPath = AppContext.BaseDirectory,
            supported = CadDescriptors.All.Select(ToDescriptorDto),
            detected = installations.Select(ToInstallationDto),
        });
    }

    private static int Doctor()
    {
        ProcessGuardService.IsAnyCadRunning(out var runningNames);
        var pluginDlls = CadDescriptors.All
            .Select(d => new { d.PluginFileName, path = Path.Combine(AppContext.BaseDirectory, d.PluginFileName) })
            .DistinctBy(d => d.PluginFileName, StringComparer.OrdinalIgnoreCase)
            .Select(d => new { fileName = d.PluginFileName, d.path, exists = File.Exists(d.path) })
            .ToList();

        var config = DeployerFontConfigService.Load();
        var mainFontPath = DeployerFontConfigService.FindGreenFontFile(config.MainFont);
        var bigFontPath = DeployerFontConfigService.FindGreenFontFile(config.BigFont);

        var ok = runningNames.Count == 0
            && pluginDlls.All(d => d.exists)
            && File.Exists(DeployerFontConfigService.ConfigPath)
            && (mainFontPath is not null || IsBundledDefault(config.MainFont))
            && (bigFontPath is not null || IsBundledDefault(config.BigFont));

        return WriteJson(new
        {
            ok,
            deployPath = AppContext.BaseDirectory,
            version = DeployerVersionService.GetDisplayVersion(),
            buildId = DeployerVersionService.GetBuildId(),
            cadRunning = runningNames.Count > 0,
            runningProcesses = runningNames,
            plugins = pluginDlls,
            configPath = DeployerFontConfigService.ConfigPath,
            configExists = File.Exists(DeployerFontConfigService.ConfigPath),
            fontsDirectory = DeployerFontConfigService.FontsDirectory,
            fontConfig = config,
            configuredFonts = new
            {
                mainFont = new { config.MainFont, path = mainFontPath, exists = mainFontPath is not null, bundledFallback = IsBundledDefault(config.MainFont) },
                bigFont = new { config.BigFont, path = bigFontPath, exists = bigFontPath is not null, bundledFallback = IsBundledDefault(config.BigFont) },
                trueTypeFont = config.TrueTypeFont,
            },
            detected = CadRegistryScanner.Scan().Select(ToInstallationDto),
        }, ok ? 0 : 1);
    }

    private static int Fonts(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsList(args[0]))
        {
            return WriteJson(new
            {
                ok = true,
                fontsDirectory = DeployerFontConfigService.FontsDirectory,
                shx = DeployerFontConfigService.ScanShxFonts(),
                trueType = DeployerFontConfigService.ScanTrueTypeFonts(),
            });
        }

        return Fail($"未知 fonts 子命令：{args[0]}", exitCode: 2);
    }

    private static int Config(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.Equals(args[0], "get", StringComparison.OrdinalIgnoreCase))
        {
            return WriteJson(new
            {
                ok = true,
                path = DeployerFontConfigService.ConfigPath,
                config = DeployerFontConfigService.Load(),
            });
        }

        if (!string.Equals(args[0], "set", StringComparison.OrdinalIgnoreCase))
            return Fail($"未知 config 子命令：{args[0]}", exitCode: 2);

        var options = ParseOptions(args.Skip(1));
        var current = DeployerFontConfigService.Load();
        var updated = current with
        {
            MainFont = GetOption(options, "main", current.MainFont),
            BigFont = GetOption(options, "big", current.BigFont),
            TrueTypeFont = GetOption(options, "ttf", current.TrueTypeFont),
            IsInitialized = true,
        };

        DeployerFontConfigService.Save(updated);
        return WriteJson(new
        {
            ok = true,
            path = DeployerFontConfigService.ConfigPath,
            config = DeployerFontConfigService.Load(),
        });
    }

    private static int Install(IReadOnlyList<string> args)
    {
        if (ProcessGuardService.IsAnyCadRunning(out var runningNames))
        {
            return WriteJson(new
            {
                ok = false,
                error = "检测到 CAD 正在运行，请关闭后再安装。",
                runningProcesses = runningNames,
            }, 1);
        }

        var selected = SelectInstallations(args);
        if (selected.Count == 0)
            return Fail("没有匹配的已安装 CAD 版本。使用 --versions 2014,2024 或 --all。", exitCode: 1);

        var results = new List<object>();
        var successes = 0;
        foreach (var installation in selected)
        {
            var ok = PluginDeployer.TryInstall(installation, out var error, out var warning);
            var fontOk = false;
            if (ok)
            {
                successes++;
                try { fontOk = EmbeddedFontPatcher.Apply(installation); } catch { fontOk = false; }
                try { AwsHideableDialogPatcher.Apply(installation.Descriptor); } catch { }
            }

            results.Add(new
            {
                version = installation.Descriptor.Version,
                displayName = installation.Descriptor.DisplayName,
                ok,
                warning,
                error,
                fontCopyOk = ok && fontOk,
            });
        }

        return WriteJson(new
        {
            ok = successes == selected.Count,
            successes,
            total = selected.Count,
            results,
        }, successes == selected.Count ? 0 : 1);
    }

    private static int Uninstall(IReadOnlyList<string> args)
    {
        if (ProcessGuardService.IsAnyCadRunning(out var runningNames))
        {
            return WriteJson(new
            {
                ok = false,
                error = "检测到 CAD 正在运行，请关闭后再卸载。",
                runningProcesses = runningNames,
            }, 1);
        }

        var selected = SelectInstallations(args)
            .Where(i => i.Status != PluginDeployStatus.NotInstalled)
            .ToList();
        if (selected.Count == 0)
            return Fail("没有匹配的已部署 CAD 版本。使用 --versions 2014,2024 或 --all。", exitCode: 1);

        var results = new List<object>();
        var successes = 0;
        foreach (var installation in selected)
        {
            var ok = PluginUninstaller.TryUninstall(installation, out var warning);
            if (ok)
            {
                successes++;
                try { AwsHideableDialogPatcher.Cleanup(installation.Descriptor); } catch { }
            }

            results.Add(new
            {
                version = installation.Descriptor.Version,
                displayName = installation.Descriptor.DisplayName,
                ok,
                warning,
            });
        }

        return WriteJson(new
        {
            ok = successes == selected.Count,
            successes,
            total = selected.Count,
            results,
        }, successes == selected.Count ? 0 : 1);
    }

    private static List<CadInstallation> SelectInstallations(IReadOnlyList<string> args)
    {
        var options = ParseOptions(args);
        var installations = CadRegistryScanner.Scan().Where(i => i.IsCadInstalled).ToList();
        if (options.ContainsKey("all")) return installations;

        var versions = GetOption(options, "versions", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (versions.Count == 0) return [];

        return installations
            .Where(i => versions.Contains(i.Descriptor.Version))
            .ToList();
    }

    private static int WriteHelp()
    {
        Console.WriteLine("AFR-Deployer CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  AFR-Deployer.exe detect --json");
        Console.WriteLine("  AFR-Deployer.exe status --json");
        Console.WriteLine("  AFR-Deployer.exe doctor --json");
        Console.WriteLine("  AFR-Deployer.exe fonts list --json");
        Console.WriteLine("  AFR-Deployer.exe config get --json");
        Console.WriteLine("  AFR-Deployer.exe config set --main ming.shx --big tssdchn.shx --ttf 宋体");
        Console.WriteLine("  AFR-Deployer.exe install --versions 2014,2024 --json");
        Console.WriteLine("  AFR-Deployer.exe install --all --json");
        Console.WriteLine("  AFR-Deployer.exe uninstall --versions 2014 --json");
        Console.WriteLine();
        Console.WriteLine("No arguments starts the GUI.");
        return 0;
    }

    private static int Fail(string message, int exitCode)
        => WriteJson(new { ok = false, error = message }, exitCode);

    private static int WriteJson(object payload, int exitCode = 0)
    {
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        return exitCode;
    }

    private static object ToDescriptorDto(CadDescriptor descriptor)
        => new
        {
            descriptor.Brand,
            descriptor.Version,
            descriptor.DisplayName,
            descriptor.RegistryBasePath,
            descriptor.AppName,
            descriptor.PluginFileName,
        };

    private static object ToInstallationDto(CadInstallation installation)
        => new
        {
            descriptor = ToDescriptorDto(installation.Descriptor),
            installation.IsCadInstalled,
            status = installation.Status.ToString(),
            installation.InstalledVersion,
            installation.InstalledBuildId,
            installation.InstalledDllPath,
            profileSubKeys = installation.ProfileSubKeys,
        };

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = args.ToArray();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!item.StartsWith("--", StringComparison.Ordinal)) continue;

            var key = item[2..];
            if (string.Equals(key, "json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < items.Length && !items[i + 1].StartsWith("--", StringComparison.Ordinal))
                result[key] = items[++i];
            else
                result[key] = "true";
        }

        return result;
    }

    private static string GetOption(Dictionary<string, string> options, string name, string fallback)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool IsList(string value)
        => string.Equals(value, "list", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "ls", StringComparison.OrdinalIgnoreCase);

    private static bool IsBundledDefault(string fileName)
        => EmbeddedFontExtractor.EmbeddedFontFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase);
}
