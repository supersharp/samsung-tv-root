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
internal static class Program
{
    private static string _token = "";

    private static int Main(string[] args)
    {
        int port = args.Length > 0 ? int.Parse(args[0]) : 26103;
        string tokenPath = args.Length > 1 ? args[1] : "/opt/usr/share/selfroot/token";

        if (!File.Exists(tokenPath))
        {
            _token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 0);
            File.WriteAllText(tokenPath, _token);
        }
        _token = File.ReadAllText(tokenPath).Trim();
        if (_token.Length != 32)
        {
            _token = _token.PadRight(32, '0').Substring(0, 32);
        }

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                listener.Start();
                break;
            }
            catch (SocketException)
            {
                if (attempt >= 30) throw;
                Thread.Sleep(2000);
            }
        }
        File.AppendAllText("/home/owner/share/tmp/selfroot-relay.log",
            DateTime.UtcNow.ToString("O") + " relay listening on " + port + "\n");

        while (true)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                new Thread(() => Serve(client)).Start();
            }
            catch (Exception)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private static void Serve(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            stream.ReadTimeout = 30000;
            var token = new byte[32];
            if (!ReadExact(stream, token, 32)) return;
            if (Encoding.ASCII.GetString(token) != _token) return;

            var buffer = new byte[4096];
            var line = new StringBuilder();
            while (true)
            {
                int read;
                try { read = stream.Read(buffer, 0, buffer.Length); }
                catch (IOException) { return; }
                if (read <= 0) return;
                for (int i = 0; i < read; i++)
                {
                    var value = (char)buffer[i];
                    if (value == '\n')
                    {
                        var output = Execute(line.ToString());
                        var payload = Encoding.UTF8.GetBytes(output + "\n[exit]\n");
                        stream.Write(payload, 0, payload.Length);
                        line.Clear();
                    }
                    else
                    {
                        line.Append(value);
                    }
                }
            }
        }
    }

    private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
    {
        int got = 0;
        while (got < count)
        {
            int read;
            try { read = stream.Read(buffer, got, count - got); }
            catch (IOException) { return false; }
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
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);
                return output + error + "\n[exit:" + process.ExitCode + "]";
            }
        }
        catch (Exception error)
        {
            return "relay error: " + error.Message;
        }
    }
}
