using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using AFR.Abstractions;
using AFR.FontMapping;

namespace AFR.Hosting;

/// <summary>
/// Runtime-selected AutoCAD platform metadata for merged DLL builds.
/// </summary>
internal sealed class RuntimeAutoCadPlatform : ICadPlatform, INativeFontHookExportsProvider
{
    private static readonly Dictionary<string, RuntimeAutoCadPlatform> Platforms = BuildPlatforms();

    private RuntimeAutoCadPlatform(
        string versionName,
        string appName,
        string registryRelease,
        string acDbDllName,
        NativeFontHookProfile? nativeFontHookProfile)
    {
        VersionName = versionName;
        AppName = appName;
        RegistryBasePath = $@"Software\Autodesk\AutoCAD\{registryRelease}";
        AcDbDllName = acDbDllName;
        NativeFontHookProfile = nativeFontHookProfile ?? DisabledHookProfile();
        SupportsNativeFontHooks = nativeFontHookProfile != null;
    }

    public string BrandName => "AutoCAD";
    public string VersionName { get; }
    public string AppName { get; }
    public string DisplayName => $"AutoCAD {VersionName}";
    public string RegistryBasePath { get; }
    public string RegistryKeyPattern => @"^ACAD-[A-Za-z0-9]+:[A-Za-z0-9]+$";
    public string AcDbDllName { get; }
    public bool SupportsNativeFontHooks { get; }
    public NativeFontHookProfile NativeFontHookProfile { get; }

    internal static RuntimeAutoCadPlatform CreateForCurrentHost(int minimumYear, int maximumYear)
    {
        string release = ReadAcadRelease();
        string version = VersionFromAcadRelease(release);
        if (!int.TryParse(version, out int year) || year < minimumYear || year > maximumYear)
        {
            throw new NotSupportedException(
                $"当前 AutoCAD 版本不在此 DLL 支持范围内。ACADVER={release}, 识别年份={version}, 支持范围={minimumYear}-{maximumYear}。");
        }

        if (!Platforms.TryGetValue(version, out var platform))
        {
            throw new NotSupportedException($"未配置 AutoCAD {version} 的平台元数据。");
        }

        return platform;
    }

    private static string ReadAcadRelease()
    {
        try
        {
            return Application.GetSystemVariable("ACADVER")?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string VersionFromAcadRelease(string release)
    {
        string prefix = new string((release ?? string.Empty)
            .TakeWhile(ch => char.IsDigit(ch) || ch == '.')
            .ToArray());

        return prefix switch
        {
            "19.0" => "2013",
            "19.1" => "2014",
            "20.0" => "2015",
            "20.1" => "2016",
            "21.0" => "2017",
            "22.0" => "2018",
            "23.0" => "2019",
            "23.1" => "2020",
            "24.0" => "2021",
            "24.1" => "2022",
            "24.2" => "2023",
            "24.3" => "2024",
            "25.0" => "2025",
            "25.1" => "2026",
            "26.0" => "2027",
            _ => string.Empty
        };
    }

    private static Dictionary<string, RuntimeAutoCadPlatform> BuildPlatforms()
        => new(StringComparer.Ordinal)
        {
            ["2013"] = NoNativeHook("2013", "R19.0", "acdb19.dll"),
            ["2014"] = NoNativeHook("2014", "R19.1", "acdb19.dll"),
            ["2015"] = NoNativeHook("2015", "R20.0", "acdb20.dll"),
            ["2016"] = NoNativeHook("2016", "R20.1", "acdb20.dll"),
            ["2017"] = NoNativeHook("2017", "R21.0", "acdb21.dll"),
            ["2018"] = WithNativeHook("2018", "R22.0", "acdb22.dll", 0x4093C4, 0x3F2BFC, Prefix2018To2020(), Prefix2018To2021ShpLoad()),
            ["2019"] = WithNativeHook("2019", "R23.0", "acdb23.dll", 0x44518, 0x4981C, Prefix2018To2020(), Prefix2018To2021ShpLoad()),
            ["2020"] = WithNativeHook("2020", "R23.1", "acdb23.dll", 0x47BF0, 0x49DB0, Prefix2018To2020(), Prefix2018To2021ShpLoad()),
            ["2021"] = WithNativeHook("2021", "R24.0", "acdb24.dll", 0x527BC, 0x4F464, Prefix2021PlusLdFile(), Prefix2018To2021ShpLoad()),
            ["2022"] = WithNativeHook("2022", "R24.1", "acdb24.dll", 0x315E8, 0x2E7AC, Prefix2021PlusLdFile(), Prefix2022PlusShpLoad()),
            ["2023"] = WithNativeHook("2023", "R24.2", "acdb24.dll", 0x118B84, 0x81710, Prefix2021PlusLdFile(), Prefix2022PlusShpLoad()),
            ["2024"] = WithNativeHook("2024", "R24.3", "acdb24.dll", 0x4785C, 0x44F38, Prefix2021PlusLdFile(), Prefix2022PlusShpLoad()),
            ["2025"] = WithNativeHook("2025", "R25.0", "acdb25.dll", 0xD2988, 0x4F834, Prefix2021PlusLdFile(), Prefix2022PlusShpLoad()),
            ["2026"] = WithNativeHook("2026", "R25.1", "acdb25.dll", 0xD87AC, 0x5B124, Prefix2021PlusLdFile(), Prefix2022PlusShpLoad()),
            ["2027"] = new RuntimeAutoCadPlatform(
                "2027",
                "AFR-ACAD2027",
                "R26.0",
                "acdb26.dll",
                new NativeFontHookProfile(
                    NativeHookTarget.Export(
                        "ldfile",
                        "?ldfile@@YAHPEB_WHPEAVAcDbDatabase@@PEAVAcFontDescription@@@Z",
                        0xA375C,
                        Prefix2021PlusLdFile(),
                        maxPrologueSize: 64),
                    NativeHookTarget.Export(
                        "shpload",
                        "?shpload@@YAHPEB_WHPEAVAcDbDatabase@@_N0022W4Charset@@W4FontPitch@FontUtils@PAL@AutoCAD@Autodesk@@W4FontFamily@4567@@Z",
                        0xA07A0,
                        [0x48, 0x8B, 0xC4, 0x48, 0x89, 0x58, 0x20, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56],
                        maxPrologueSize: 64)))
        };

    private static RuntimeAutoCadPlatform NoNativeHook(string version, string registryRelease, string acDbDllName)
        => new(version, MergedAppName(version), registryRelease, acDbDllName, null);

    private static RuntimeAutoCadPlatform WithNativeHook(
        string version,
        string registryRelease,
        string acDbDllName,
        uint ldFileRva,
        uint shpLoadRva,
        byte[] ldFilePrefix,
        byte[] shpLoadPrefix)
        => new(
            version,
            MergedAppName(version),
            registryRelease,
            acDbDllName,
            new NativeFontHookProfile(
                NativeHookTarget.Export(
                    "ldfile",
                    "?ldfile@@YAHPEB_WHPEAVAcDbDatabase@@PEAVAcFontDescription@@@Z",
                    ldFileRva,
                    ldFilePrefix,
                    maxPrologueSize: 64),
                NativeHookTarget.Export(
                    "shpload",
                    "?shpload@@YAHPEB_WHPEAVAcDbDatabase@@_N00HHW4Charset@@W4FontPitch@FontUtils@PAL@AutoCAD@Autodesk@@W4FontFamily@4567@@Z",
                    shpLoadRva,
                    shpLoadPrefix,
                    maxPrologueSize: 64)));

    private static string MergedAppName(string version)
        => version switch
        {
            "2013" or "2014" or "2015" or "2016" or "2017" => "AFR-ACAD2013-2017",
            "2018" or "2019" or "2020" or "2021" or "2022" or "2023" or "2024" => "AFR-ACAD2018-2024",
            "2025" or "2026" => "AFR-ACAD2025-2026",
            "2027" => "AFR-ACAD2027",
            _ => $"AFR-ACAD{version}"
        };

    private static NativeFontHookProfile DisabledHookProfile()
        => new(
            NativeHookTarget.Disabled("ldfile", "AutoCAD 2013-2017 尚未配置 native Hook profile"),
            NativeHookTarget.Disabled("shpload", "AutoCAD 2013-2017 尚未配置 native Hook profile"));

    private static byte[] Prefix2018To2020()
        => [0x48, 0x8B, 0xC4, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x8D];

    private static byte[] Prefix2018To2021ShpLoad()
        => [0x48, 0x8B, 0xC4, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x8D];

    private static byte[] Prefix2021PlusLdFile()
        => [0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x8D, 0xAC];

    private static byte[] Prefix2022PlusShpLoad()
        => [0x48, 0x89, 0x5C, 0x24, 0x20, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57];
}
