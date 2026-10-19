using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PingTray
{
    /// <summary>The outcome of checking one host.</summary>
    public class HostResult
    {
        public string Host;
        public bool Ok;
        public long RoundtripMs;  // when Ok
        public string Error;      // a short reason when not Ok: "timeout", "DNS error", "refused", ...
    }

    public static class Pinger
    {
        static readonly byte[] Payload = new byte[32];  // the size ping.exe sends

        /// <summary>
        /// Resolves the host, then pings it or connects to its TCP port, as the settings say. Waits
        /// at most TimeoutMs for the DNS answer and as long again for the reply. Never throws.
        /// </summary>
        public static async Task<HostResult> CheckAsync(string host, Settings settings)
        {
            var result = new HostResult { Host = host };
            IPAddress address;
            try
            {
                address = await ResolveAsync(host, settings.TimeoutMs).ConfigureAwait(false);
            }
            catch (Exception)
            {
                result.Error = "DNS error";
                return result;
            }
            if (address == null)
            {
                result.Error = "DNS timeout";
                return result;
            }
            try
            {
                if (settings.Method == CheckMethod.Tcp)
                    await ConnectAsync(address, settings.TcpPort, settings.TimeoutMs, result).ConfigureAwait(false);
                else
                    await PingAsync(address, settings.TimeoutMs, result).ConfigureAwait(false);
            }
            catch (SocketException e)
            {
                result.Error = Describe(e.SocketErrorCode);
            }
            catch (Exception)
            {
                result.Error = "failed";
            }
            return result;
        }

        /// <summary>The host's address, IPv4 if it has one; null if DNS does not answer in time.</summary>
        static async Task<IPAddress> ResolveAsync(string host, int timeoutMs)
        {
            IPAddress address;
            if (IPAddress.TryParse(host, out address))
                return address;
            Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(HostNames.ToAscii(host));
            if (await Task.WhenAny(lookup, Task.Delay(timeoutMs)).ConfigureAwait(false) != lookup)
            {
                IgnoreFailure(lookup);
                return null;
            }
            IPAddress[] addresses = await lookup.ConfigureAwait(false);
            // Many connections have no working IPv6 even though DNS returns IPv6 addresses.
            address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
            if (address == null)
                throw new SocketException((int)SocketError.HostNotFound);
            return address;
        }

        static async Task PingAsync(IPAddress address, int timeoutMs, HostResult result)
        {
            using (var ping = new Ping())
            {
                PingReply reply = await ping.SendPingAsync(address, timeoutMs, Payload).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    result.Ok = true;
                    result.RoundtripMs = reply.RoundtripTime;
                }
                else
                    result.Error = Describe(reply.Status);
            }
        }

        /// <summary>
        /// Opens a TCP connection and closes it again; the time is that of the handshake. A refused
        /// connection counts as a failure: the port may be closed, or a firewall may refuse it.
        /// </summary>
        static async Task ConnectAsync(IPAddress address, int port, int timeoutMs, HostResult result)
        {
            using (var client = new TcpClient(address.AddressFamily))
            {
                Stopwatch watch = Stopwatch.StartNew();
                Task connect = client.ConnectAsync(address, port);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false) != connect)
                {
                    IgnoreFailure(connect);  // disposing the client aborts it
                    result.Error = "timeout";
                    return;
                }
                await connect.ConfigureAwait(false);  // throws SocketException if refused or unreachable
                result.Ok = true;
                result.RoundtripMs = watch.ElapsedMilliseconds;
            }
        }

        /// <summary>Marks a late failure as seen, so it is not reported as an unobserved exception.</summary>
        static void IgnoreFailure(Task task)
        {
            task.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        static string Describe(IPStatus status)
        {
            switch (status)
            {
                case IPStatus.TimedOut:
                    return "timeout";
                case IPStatus.DestinationHostUnreachable:
                case IPStatus.DestinationNetworkUnreachable:
                case IPStatus.DestinationUnreachable:
                    return "unreachable";
                case IPStatus.TtlExpired:
                case IPStatus.TimeExceeded:
                    return "TTL expired";
                default:
                    return "failed";
            }
        }

        static string Describe(SocketError error)
        {
            switch (error)
            {
                case SocketError.ConnectionRefused:
                    return "refused";
                case SocketError.TimedOut:
                    return "timeout";
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.HostDown:
                case SocketError.NetworkDown:
                    return "unreachable";
                default:
                    return "failed";
            }
        }
    }
}
