using System.IO;
using System.Text;

namespace AFR.Deployer.Services;

internal static class DebugLogService
{
    private const string LogFileName = "AFR-Deployer.debug.log";
    private static readonly object Gate = new();

    internal static string LogPath => Path.Combine(AppContext.BaseDirectory, LogFileName);

    internal static void Info(string message) => Write("INFO", message, null);

    internal static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppContext.BaseDirectory);
            var sb = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(' ')
                .Append(level)
                .Append(' ')
                .AppendLine(message);

            if (exception is not null)
                sb.AppendLine(exception.ToString());

            lock (Gate)
            {
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 调试日志不能影响部署器主流程。
        }
    }
}
