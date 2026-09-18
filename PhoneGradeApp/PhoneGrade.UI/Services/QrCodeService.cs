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

    public static string GenerateSessionUrl(string localIp, int port, string deviceUdid)
    {
        var cleanUdid = Uri.EscapeDataString(deviceUdid ?? "UNKNOWN");
        return $"http://{localIp}:{port}/?sessionId={cleanUdid}";
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
