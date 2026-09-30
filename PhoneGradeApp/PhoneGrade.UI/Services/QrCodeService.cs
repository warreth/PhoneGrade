using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Media.Imaging;
using Net.Codecrete.QrCodeGenerator;

namespace PhoneGrade.UI.Services;

/// <summary>
/// Service to determine local network IP addresses and generate QR code bitmaps for mobile pairing.
/// </summary>
public static class QrCodeService
{
    public static string GetLocalIpAddress()
    {
        try
        {
            // First check operational non-loopback network interfaces
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = iface.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        var ipStr = addr.Address.ToString();
                        // Prefer standard private network ranges: 192.168.x.x, 10.x.x.x, 172.16-31.x.x
                        if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.") || ipStr.StartsWith("172."))
                        {
                            return ipStr;
                        }
                    }
                }
            }

            // Fallback: connect UDP socket to public DNS to find default route IP
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address))
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
            // Ignore network discovery exceptions
        }

        return "127.0.0.1";
    }

    /// <summary>The address the phone reaches on the local network.</summary>
    public static string NetworkAddress(int port) => $"http://{GetLocalIpAddress()}:{port}";

    /// <summary>The address adb reverse maps onto the phone. Browsers trust it on http.</summary>
    public static string LoopbackAddress(int port) => $"http://localhost:{port}";

    /// <summary>
    /// Builds the address the phone should open from a plain network host and port.
    /// Kept separate from the overload below because it is the only one that hard
    /// codes the http scheme, which is exactly what a LAN address is.
    /// </summary>
    public static string GenerateSessionUrl(string host, int port, string deviceUdid, bool isDebug = false, string? testPhoneNumber = null)
    {
        string cleanHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        return GenerateSessionUrl($"http://{cleanHost}:{port}", deviceUdid, isDebug, testPhoneNumber);
    }

    /// <summary>
    /// Appends the session to any base address, whether it is a loopback http one or
    /// a public https one handed out by the tunnel connector. The scheme is never
    /// invented here: it is the whole reason the address was chosen.
    /// </summary>
    public static string GenerateSessionUrl(string baseUrl, string deviceUdid, bool isDebug = false, string? testPhoneNumber = null)
    {
        var cleanUdid = Uri.EscapeDataString(deviceUdid ?? "UNKNOWN");
        string cleanBase = (baseUrl ?? "").Trim().TrimEnd('/');
        if (cleanBase.Length == 0) cleanBase = "http://127.0.0.1:5055";

        string url = $"{cleanBase}/?sessionId={cleanUdid}";
        if (isDebug) url += "&debug=true";
        if (!string.IsNullOrWhiteSpace(testPhoneNumber)) url += $"&testPhoneNumber={Uri.EscapeDataString(testPhoneNumber)}";
        return url;
    }

    /// <summary>True for an address a browser treats as trustworthy without https.</summary>
    public static bool IsLoopbackAddress(string? baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    /// <summary>
    /// True for an address that carries camera, microphone, motion and wake lock.
    /// That is https, plus plain http on a loopback host, which browsers decided to
    /// trust a long time ago and never moved on.
    /// </summary>
    public static bool IsSecureAddress(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback;
    }

    public static Bitmap GenerateQrCodeBitmap(string text, int scale = 6, int border = 2)
    {
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        // Net.Codecrete.QrCodeGenerator ToPngBitmap(border, scale, foreground, background)
        // Foreground: 0x000000 (Black), Background: 0xffffff (White)
        byte[] pngBytes = qr.ToPngBitmap(border: border, scale: scale, foreground: 0x000000, background: 0xffffff);
        using var stream = new MemoryStream(pngBytes);
        return new Bitmap(stream);
    }
}
