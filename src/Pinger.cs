using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PingTray
{
    /// <summary>The outcome of pinging one host.</summary>
    public class HostResult
    {
        public string Host;
        public bool Ok;
        public long RoundtripMs;  // when Ok
        public string Error;      // a short reason when not Ok: "timeout", "DNS error", ...
    }

    public static class Pinger
    {
        static readonly byte[] Payload = new byte[32];  // the size ping.exe sends

        /// <summary>
        /// Resolves the host and sends it one ICMP echo request. Waits at most timeoutMs for the
        /// DNS answer and as long again for the reply. Never throws.
        /// </summary>
        public static async Task<HostResult> CheckAsync(string host, int timeoutMs)
        {
            var result = new HostResult { Host = host };
            try
            {
                IPAddress address = await ResolveAsync(host, timeoutMs).ConfigureAwait(false);
                if (address == null)
                {
                    result.Error = "DNS timeout";
                    return result;
                }
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
            catch (SocketException)
            {
                result.Error = "DNS error";
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
    }
}
