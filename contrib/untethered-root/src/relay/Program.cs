using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Root relay: started by the signed archive's launch.sh as uid 0.
// Listens on 0.0.0.0:<port>, requires the 32-char token file contents as the
// first bytes, then executes one shell command per line and streams the result.
//
// Hardening (2026-10-28):
//  * single instance via an exclusive lock file (duplicate boot triggers are harmless)
//  * every socket write is guarded: a client disconnecting mid-response can no
//    longer kill the process with IOException "Broken pipe"
//  * bounded concurrency, command timeout, output cap
//  * constant-time token comparison; a token file that is not exactly 32 bytes
//    is a hard error instead of being zero-padded
//
// Arg 3 (optional) overrides the lock path, so a new build can be smoke-tested
// on a second port while the live relay keeps running.
internal static class Program
{
    private const int TokenLength = 32;
    private const int MaxClients = 4;
    private const int MaxCommandBytes = 8192;
    private const int MaxOutputChars = 1 << 20;   // 1 MiB per stream
    private const int CommandTimeoutMs = 30000;
    private const long MaxLogBytes = 1 << 20;     // rotate relay.log at 1 MiB

    private static string _token = "";
    private static int _clients;
    private static readonly object LogLock = new object();
    private static FileStream _lockFile;   // held for the process lifetime

    // Keep relay.log bounded: one rolled generation (`relay.log.1`), then start
    // fresh. launch.sh also caps the other selfroot logs at boot.
    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxLogBytes)
            {
                var backup = path + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
            }
        }
        catch
        {
            // rotation is best effort; never break a command over it
        }
    }

    private static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                var path = "/home/owner/share/tmp/selfroot-relay/relay.log";
                RotateIfNeeded(path);
                File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + message + "\n");
            }
        }
        catch
        {
            // logging is best effort
        }
    }

    private static int Main(string[] args)
    {
        int port = 26103;
        if (args.Length > 0 && !int.TryParse(args[0], out port))
        {
            Console.Error.WriteLine("relay: bad port '" + args[0] + "'");
            return 2;
        }
        string tokenPath = args.Length > 1 ? args[1] : "/opt/usr/share/selfroot/token";
        string lockPath = args.Length > 2 ? args[2] : "/run/selfroot-relay.lock";

        // ---- single instance -------------------------------------------------
        try
        {
            var dir = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Log("another relay holds " + lockPath + "; exiting");
            return 0;
        }
        catch (Exception error)
        {
            Log("cannot take lock " + lockPath + ": " + error.GetType().Name + " " + error.Message);
            return 3;
        }

        // ---- token -----------------------------------------------------------
        try
        {
            if (!File.Exists(tokenPath))
            {
                _token = NewToken();
                File.WriteAllText(tokenPath, _token);
                TryChmod("600", tokenPath);
                Log("created token " + tokenPath);
            }
            else
            {
                _token = File.ReadAllText(tokenPath).Trim();
            }
        }
        catch (Exception error)
        {
            Log("cannot read token " + tokenPath + ": " + error.Message);
            return 4;
        }
        if (_token.Length != TokenLength)
        {
            Log("token " + tokenPath + " is " + _token.Length + " chars, expected " + TokenLength + "; refusing to start");
            return 5;
        }

        // ---- listener --------------------------------------------------------
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                listener.Start();
                break;
            }
            catch (SocketException error)
            {
                if (attempt >= 30)
                {
                    Log("cannot bind port " + port + ": " + error.SocketErrorCode);
                    return 6;
                }
                Thread.Sleep(2000);
            }
        }
        Log("relay listening on " + port + " (pid " + Process.GetCurrentProcess().Id + ")");

        while (true)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception error)
            {
                Log("accept failed: " + error.GetType().Name);
                Thread.Sleep(1000);
                continue;
            }
            var thread = new Thread(() => Serve(client)) { IsBackground = true };
            thread.Start();
        }
    }

    private static string NewToken()
    {
        var bytes = new byte[16];
        using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
        {
            rng.GetBytes(bytes);
        }
        var text = new StringBuilder(TokenLength);
        foreach (var b in bytes) text.Append(b.ToString("x2"));
        return text.ToString();
    }

    private static void TryChmod(string mode, string path)
    {
        try
        {
            var info = new ProcessStartInfo("/bin/chmod") { UseShellExecute = false };
            info.ArgumentList.Add(mode);
            info.ArgumentList.Add(path);
            using (var chmod = Process.Start(info))
            {
                if (chmod != null) chmod.WaitForExit(3000);
            }
        }
        catch
        {
            // permissions are also enforced by launch.sh
        }
    }

    private static void Serve(TcpClient client)
    {
        int active = Interlocked.Increment(ref _clients);
        try
        {
            if (active > MaxClients)
            {
                Log("rejecting client: " + active + " connected (max " + MaxClients + ")");
                return;
            }
            client.NoDelay = true;
            using (client)
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 15000;
                stream.WriteTimeout = 30000;

                var supplied = new byte[TokenLength];
                if (!ReadExact(stream, supplied, TokenLength)) return;
                if (!TokenMatches(supplied))
                {
                    Log("auth failure");
                    return;
                }

                var buffer = new byte[4096];
                var line = new StringBuilder();
                while (true)
                {
                    int read;
                    try { read = stream.Read(buffer, 0, buffer.Length); }
                    catch (Exception) { return; }
                    if (read <= 0) return;

                    for (int i = 0; i < read; i++)
                    {
                        var value = (char)buffer[i];
                        if (value == '\n')
                        {
                            var output = Execute(line.ToString());
                            line.Clear();
                            if (!WriteReply(stream, output)) return;
                        }
                        else
                        {
                            line.Append(value);
                            if (line.Length > MaxCommandBytes)
                            {
                                line.Clear();
                                if (!WriteReply(stream, "relay error: command line too long\n[exit:1]")) return;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception error)
        {
            Log("serve error: " + error.GetType().Name + " " + error.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _clients);
            try { client.Close(); } catch { }
        }
    }

    private static bool TokenMatches(byte[] supplied)
    {
        int diff = 0;
        for (int i = 0; i < TokenLength; i++)
        {
            diff |= supplied[i] ^ (byte)_token[i];
        }
        return diff == 0;
    }

    // Returns false only when the reply could not be delivered; the caller then
    // drops that connection. It never throws into the accept loop.
    // The payload already ends with the "\n[exit:N]" terminator produced by
    // Execute(), which is exactly what the original relay protocol used.
    private static bool WriteReply(NetworkStream stream, string output)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(output + "\n");
            stream.Write(payload, 0, payload.Length);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
    {
        int got = 0;
        while (got < count)
        {
            int read;
            try { read = stream.Read(buffer, got, count - got); }
            catch (Exception) { return false; }
            if (read <= 0) return false;
            got += read;
        }
        return true;
    }

    private static string Execute(string command)
    {
        try
        {
            var start = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
            using (var process = Process.Start(start))
            {
                if (process == null) return "relay error: could not start /bin/sh";
                process.StandardInput.Close();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                int exitCode = -1;
                if (process.WaitForExit(CommandTimeoutMs))
                {
                    exitCode = process.ExitCode;
                }
                else
                {
                    try { process.Kill(); } catch { }
                    process.WaitForExit(2000);
                    exitCode = 124;   // timeout convention
                }

                var output = Drain(stdoutTask);
                var error = Drain(stderrTask);
                var text = output + error;
                if (text.Length > MaxOutputChars)
                {
                    text = text.Substring(0, MaxOutputChars) + "\n[relay: output truncated]\n";
                }
                return text + "\n[exit:" + exitCode + "]";
            }
        }
        catch (Exception error)
        {
            return "relay error: " + error.Message + "\n[exit:1]";
        }
    }

    private static string Drain(System.Threading.Tasks.Task<string> task)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch
        {
            return "";
        }
    }
}
