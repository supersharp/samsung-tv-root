using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// Boot stub v2: sits in the com.samsung.tv.ghservice slot and launches the clean,
// signed TPK agent com.selfroot.boot via the app framework (aul_launch_app) instead
// of Process.Start (which the app SMACK label denies).
internal static class Program
{
    private const string LogPath = "/home/owner/share/tmp/selfroot-stub.log";
    private const string TargetApp = "com.selfroot.boot";

    // int aul_launch_app(const char *appid, bundle *kb)   (libaul.so.0)
    [DllImport("libaul.so.0", EntryPoint = "aul_launch_app", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aul_launch_app([MarshalAs(UnmanagedType.LPStr)] string appid, IntPtr bundle);

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + line + "\n"); }
        catch { /* best effort */ }
    }

    private static void Main()
    {
        Log("aul stub start");
        // Give app-boot-manager / sdbd a few seconds to settle after boot.
        Thread.Sleep(6000);

        // Retry a few times in case the app framework (aul/launchpad) is not
        // fully ready at the very start of boot.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                int rc = aul_launch_app(TargetApp, IntPtr.Zero);
                Log("aul_launch_app attempt=" + attempt + " rc=" + rc);
                if (rc >= 0) break;   // success: returns the launched pid
            }
            catch (Exception error)
            {
                Log("attempt " + attempt + " err " + error.GetType().Name + ": " + error.Message);
            }
            Thread.Sleep(5000);
        }

        // Give the launched TPK agent time to finish its root chain before this
        // svcapp exits (the launched app is managed by aul, so it survives; the
        // detached relay it starts survives via sdbd-tarlauncher).
        Thread.Sleep(15000);
        Log("aul stub done");
    }
}
