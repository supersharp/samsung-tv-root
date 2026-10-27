using System;
using System.Globalization;
using System.Runtime.InteropServices;

// Generic UEP/SFD state-word control for Tizen 6.0.
//
//   uepctl status  --page <physical-page> --offset <word-offset>
//   uepctl disable --page <physical-page> --offset <word-offset>
//   uepctl enable  --page <physical-page> --offset <word-offset>
//
// The physical page and word offset are platform/firmware-specific and are not
// baked into this source. The tool maps exactly one page through /dev/sdp_hwmem
// and reads or writes one 32-bit word. Wrong values can crash or corrupt the TV.
// Requires uid 0 and only works while the kernel exposes that device.

internal static class Program
{
    private const string Device = "/dev/sdp_hwmem";

    private const int O_RDWR = 2;
    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int MAP_SHARED = 1;
    private const int SYS_MMAP2_ARM = 192;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", EntryPoint = "syscall", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    private static extern IntPtr syscall(int number, IntPtr address, UIntPtr length, int protection, int flags, int fd, uint pageOffset);

    [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
    private static extern int munmap(IntPtr address, UIntPtr length);

    private static void Say(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush();
    }

    private static void Usage()
    {
        Console.Error.WriteLine(
            "usage: uepctl status|disable|enable --page 0x<physical-page> --offset 0x<word-offset>\n" +
            "       the page must be 4 KB aligned; the offset must be 4-byte aligned (0..0xffc)");
    }

    private static long ParseNumber(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static bool ParseOptions(string[] args, int start, out long page, out int offset)
    {
        page = -1;
        offset = -1;
        try
        {
            for (int i = start; i < args.Length; i++)
            {
                string option = args[i];
                if (option == "--page" || option == "-p")
                {
                    if (++i >= args.Length) return false;
                    page = ParseNumber(args[i]);
                }
                else if (option == "--offset" || option == "-o")
                {
                    if (++i >= args.Length) return false;
                    long value = ParseNumber(args[i]);
                    if (value < 0 || value > 0xffc) return false;
                    offset = (int)value;
                }
                else
                {
                    return false;
                }
            }
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        return page >= 0 && offset >= 0;
    }

    private static (IntPtr map, int fd) MapPage(long page)
    {
        int fd = open(Device, O_RDWR);
        if (fd < 0)
        {
            Say($"open {Device} failed errno={Marshal.GetLastWin32Error()}");
            return (IntPtr.Zero, -1);
        }

        uint pgoff = (uint)(page >> 12);
        IntPtr map = syscall(SYS_MMAP2_ARM, IntPtr.Zero, new UIntPtr(0x1000),
            PROT_READ | PROT_WRITE, MAP_SHARED, fd, pgoff);
        if (map == new IntPtr(-1) || map == IntPtr.Zero)
        {
            Say($"mmap2 failed errno={Marshal.GetLastWin32Error()}");
            close(fd);
            return (IntPtr.Zero, -1);
        }
        return (map, fd);
    }

    private static uint ReadWord(IntPtr map, int offset)
    {
        return (uint)Marshal.ReadInt32(map, offset);
    }

    private static string Describe(uint value)
    {
        if (value == 0) return "disabled (patched)";
        if (value == 1) return "enabled (stock)";
        return $"unknown (0x{value:x8})";
    }

    private static int Status(long page, int offset)
    {
        var (map, fd) = MapPage(page);
        if (fd < 0) return 1;
        uint value = ReadWord(map, offset);
        Say($"UEP state word @ phys 0x{page + offset:x} = 0x{value:x8} -> {Describe(value)}");
        munmap(map, new UIntPtr(0x1000));
        close(fd);
        return 0;
    }

    private static int Set(long page, int offset, uint target, string label)
    {
        var (map, fd) = MapPage(page);
        if (fd < 0) return 1;
        uint before = ReadWord(map, offset);
        Marshal.WriteInt32(map, offset, unchecked((int)target));
        uint after = ReadWord(map, offset);
        Say($"{label}: before=0x{before:x8} ({Describe(before)}) -> after=0x{after:x8} ({Describe(after)})");
        bool ok = after == target;
        Say(ok ? $"OK: {label} applied and verified" : $"FAILED: {label} did not stick");
        munmap(map, new UIntPtr(0x1000));
        close(fd);
        return ok ? 0 : 2;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return 2;
        }

        string command = args[0].ToLowerInvariant();
        if (command == "help" || command == "--help" || command == "-h")
        {
            Usage();
            return 0;
        }

        if (!ParseOptions(args, 1, out long page, out int offset))
        {
            Usage();
            return 2;
        }
        if (page <= 0 || page > 0xfffff000L || page % 0x1000 != 0)
        {
            Console.Error.WriteLine("page must be a positive, 4 KB aligned 32-bit physical address");
            return 2;
        }
        if (offset < 0 || offset > 0xffc || offset % 4 != 0)
        {
            Console.Error.WriteLine("offset must be 4-byte aligned and between 0 and 0xffc");
            return 2;
        }

        switch (command)
        {
            case "status":
            case "s":
                return Status(page, offset);
            case "disable":
            case "off":
            case "patch":
                return Set(page, offset, 0, "disable UEP");
            case "enable":
            case "on":
            case "revert":
                return Set(page, offset, 1, "enable UEP (revert)");
            default:
                Usage();
                return 2;
        }
    }
}
