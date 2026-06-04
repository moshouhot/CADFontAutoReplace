using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AFR.Deployer.Services;

/// <summary>
/// 让 Windows GUI 子系统程序在 CLI 模式下复用调用方控制台。
/// </summary>
internal static class ConsoleBridge
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    internal static void AttachParentConsole()
    {
        _ = AttachConsole(AttachParentProcess);
        RedirectStandardStream(StdOutputHandle, isError: false);
        RedirectStandardStream(StdErrorHandle, isError: true);
    }

    internal static void DetachConsole()
    {
        _ = FreeConsole();
    }

    private static void RedirectStandardStream(int handleKind, bool isError)
    {
        var handle = GetStdHandle(handleKind);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

        var safeHandle = new SafeFileHandle(handle, ownsHandle: false);
        var stream = new FileStream(safeHandle, FileAccess.Write);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        if (isError)
            Console.SetError(writer);
        else
            Console.SetOut(writer);
    }
}
