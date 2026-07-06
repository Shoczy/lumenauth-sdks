using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LumenAuth
{
    /// <summary>
    /// A stable, SHA-256-hashed hardware id for this machine. LumenAuth only
    /// ever stores and compares the hash — the raw machine id never leaves the
    /// device un-hashed.
    ///
    /// <list type="bullet">
    ///   <item><description>Windows — <c>MachineGuid</c> from the registry</description></item>
    ///   <item><description>Linux — <c>/etc/machine-id</c></description></item>
    ///   <item><description>macOS — <c>IOPlatformUUID</c></description></item>
    /// </list>
    /// </summary>
    public static class Hwid
    {
        public static string Get()
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(RawMachineId()));
                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(digest[i].ToString("X2"));
                return sb.ToString();
            }
        }

        private static string RawMachineId()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var guid = ReadWindowsMachineGuid();
                    if (!string.IsNullOrEmpty(guid)) return guid;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                    {
                        try
                        {
                            if (File.Exists(path))
                            {
                                var text = File.ReadAllText(path).Trim();
                                if (!string.IsNullOrEmpty(text)) return text;
                            }
                        }
                        catch
                        {
                            // Try the next path.
                        }
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var uuid = ReadMacUuid();
                    if (!string.IsNullOrEmpty(uuid)) return uuid;
                }
            }
            catch
            {
                // Fall through to the machine-name fallback below.
            }

            return Environment.MachineName + "-" + (int)Environment.OSVersion.Platform;
        }

        // Shell out to reg.exe so we don't take a hard Microsoft.Win32.Registry
        // dependency (keeps the SDK to a single NuGet package).
        private static string ReadWindowsMachineGuid()
        {
            var output = RunCommand("reg",
                @"query HKLM\SOFTWARE\Microsoft\Cryptography /v MachineGuid");
            if (output == null) return null;
            foreach (var line in output.Split('\n'))
            {
                if (line.IndexOf("MachineGuid", StringComparison.Ordinal) >= 0)
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0) return parts[parts.Length - 1].Trim();
                }
            }
            return null;
        }

        private static string ReadMacUuid()
        {
            var output = RunCommand("ioreg", "-rd1 -c IOPlatformExpertDevice");
            if (output == null) return null;
            foreach (var line in output.Split('\n'))
            {
                if (line.IndexOf("IOPlatformUUID", StringComparison.Ordinal) >= 0)
                {
                    var parts = line.Split('"');
                    if (parts.Length >= 2) return parts[parts.Length - 2];
                }
            }
            return null;
        }

        private static string RunCommand(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
