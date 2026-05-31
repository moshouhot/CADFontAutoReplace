using Autodesk.AutoCAD.Runtime;
using AFR.Abstractions;
using AFR.FontMapping;
using AFR.Hosting;

[assembly: ExtensionApplication(typeof(AFR.PluginEntry))]
[assembly: CommandClass(typeof(AFR.Commands.AfrCommands))]

namespace AFR;

/// <summary>
/// Merged AutoCAD 2013-2024 plugin entry point.
/// </summary>
public class PluginEntry : PluginEntryBase
{
    protected override ICadPlatform CreatePlatform()
        => RuntimeAutoCadPlatform.CreateForCurrentHost(2013, 2024);

    protected override IFontHook CreateFontHook() => new AutoCadFontHook();

    protected override ICadHost CreateHost() => new AutoCadHost();
}
