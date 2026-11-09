using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NamakeKaigi.Services;

/// <summary>
/// 二重起動の防止。名前つきの Mutex で「もう起動しているか」を調べ、起動済みなら、その窓を前に出す。
/// 2つ開くと、リモートの入口が取り合いになり、自動実行の予約が2重に走るため。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>二重起動を許す起動引数(開発中に、Debug版と Release版を並べるときなど)。</summary>
    public const string AllowMultipleArgument = "--allow-multiple";

    private readonly Mutex? _mutex;

    /// <summary>このプロセスが、最初の1つ目か(2つ目なら false)。</summary>
    public bool IsFirst { get; }

    private SingleInstance(Mutex? mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirst = isFirst;
    }

    /// <summary>1つ目として名前を取れるか試す。</summary>
    /// <param name="name">Mutex の名前(同じ名前どうしで、二重起動かを判定する)</param>
    /// <returns>1つ目なら IsFirst が true。2つ目なら false</returns>
    public static SingleInstance TryAcquire(string name)
    {
        var mutex = new Mutex(true, name, out var createdNew);
        return new SingleInstance(mutex, createdNew);
    }

    /// <summary>起動引数に、二重起動を許す指定があるか。</summary>
    /// <param name="args">起動引数</param>
    /// <returns>許すなら true</returns>
    public static bool AllowsMultiple(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, AllowMultipleArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>すでに起動している自分と同じ名前の窓を、前に出す(最小化されていれば元に戻す)。</summary>
    /// <returns>前に出せたら true</returns>
    public static bool BringExistingToFront()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(self.ProcessName))
        {
            using (p)
            {
                if (p.Id == self.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, SwRestore);
                return SetForegroundWindow(p.MainWindowHandle);
            }
        }
        return false;
    }

    /// <summary>名前を手放す(1つ目のときだけ意味がある)。</summary>
    public void Dispose()
    {
        if (_mutex is null) return;
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* 別のスレッドで取った場合は、終了時に任せる */ }
        }
        _mutex.Dispose();
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
