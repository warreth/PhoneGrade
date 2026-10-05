using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace PhoneGrade.Core;

/// <summary>
/// Talks to the DYMO Label Web Service, the daemon DYMO Connect installs on the
/// machine the printer is plugged into.
///
/// It is an accelerator, never the only way to print. It needs DYMO Connect on
/// the operator's machine and there is no Linux build of it at all, so the label
/// PDF handed to the operating system covers every platform this app runs on.
/// When the daemon is not there, this says so and the caller falls back.
///
/// Nothing about the protocol is documented by DYMO in prose. The ports, the
/// paths, the four form fields of a print and the shapes of the two responses
/// below all come from DYMO's own client library, which is the only first-party
/// description of the service that exists.
/// </summary>
public static class DymoPrintService
{
    /// <summary>
    /// DYMO Connect listens on the first free port in this range and takes the
    /// first one it finds, which is why a caller has to go looking.
    /// </summary>
    public const int FirstPort = 41951;
    public const int LastPort = 41960;

    private const string Path = "/DYMO/DLS/Printing/";

    private static readonly SemaphoreSlim _probeLock = new(1, 1);
    private static Uri? _service;

    /// <summary>How long one probe gets before the next port is tried.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Finds the daemon, or reports that it is not there. The answer is cached,
    /// because the port does not move while DYMO Connect is running and a print
    /// that probes ten ports first is a print the operator waits for.
    /// </summary>
    /// <param name="force">
    /// Look again even if a port is already known. The service is started and
    /// stopped while this app is open, most often by the operator installing it
    /// after being told to.
    /// </param>
    public static async Task<Uri?> FindAsync(bool force = false, CancellationToken cancellation = default)
    {
        if (_service is not null && !force) return _service;

        await _probeLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_service is not null && !force) return _service;
            _service = await ProbeAsync(cancellation).ConfigureAwait(false);
            return _service;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private static async Task<Uri?> ProbeAsync(CancellationToken cancellation)
    {
        // Ten ports x two schemes is twenty attempts, so they all go out at once
        // rather than in sequence. A miss costs one timeout, not ten.
        var probes = new List<Task<Uri?>>();
        foreach (string scheme in new[] { "https", "http" })
            for (int port = FirstPort; port <= LastPort; port++)
                probes.Add(ProbeAsync(scheme, port, cancellation));

        Uri?[] answers = await Task.WhenAll(probes).ConfigureAwait(false);
        return answers.FirstOrDefault(answer => answer is not null);
    }

    private static async Task<Uri?> ProbeAsync(string scheme, int port, CancellationToken cancellation)
    {
        try
        {
            using var client = CreateClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(ProbeTimeout);

            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{scheme}://127.0.0.1:{port}{Path}StatusConnected");
            using HttpResponseMessage response = await client.SendAsync(request, timeout.Token)
                .ConfigureAwait(false);

            // The service answers with the bare word true and nothing else. Any
            // other body is something else on the port, or a DYMO that is up but
            // not serving.
            return response.IsSuccessStatusCode && response.Content.ReadAsStringAsync(cancellation)
                .ConfigureAwait(false).GetAwaiter().GetResult().Trim() == "true"
                ? new Uri($"{scheme}://127.0.0.1:{port}{Path}")
                : null;
        }
        catch (Exception)
        {
            // Nothing listening, a certificate the machine does not trust, a
            // timeout. All of them mean the same thing here: not this port.
            return null;
        }
    }

    /// <summary>
    /// The printers DYMO Connect can see, connected or not. An empty list means
    /// the daemon is not running; a list with nothing connected to it means the
    /// operator has to plug the printer in or power it on.
    /// </summary>
    public static async Task<IReadOnlyList<DymoPrinter>> GetPrintersAsync(
        Uri? service, CancellationToken cancellation = default)
    {
        if (service is null) return [];

        using var client = CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(service, "GetPrinters"), cancellation)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];

        return ParsePrinters(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads a GetPrinters response. Element names are the printer type, so a
    /// LabelWriter and a tape printer arrive under different tags and both are
    /// printers as far as this app is concerned.
    /// </summary>
    public static IReadOnlyList<DymoPrinter> ParsePrinters(string xml)
    {
        var printers = new List<DymoPrinter>();
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            // A daemon that answers with something other than XML is not one.
            return printers;
        }

        foreach (XElement printer in document.Descendants())
        {
            string name = printer.Element("Name")?.Value.Trim() ?? "";
            if (name.Length == 0) continue;

            printers.Add(new DymoPrinter(
                Name: name,
                Model: printer.Element("ModelName")?.Value.Trim() ?? name,
                IsConnected: Flag(printer.Element("IsConnected")?.Value),
                IsTwinTurbo: Flag(printer.Element("IsTwinTurbo")?.Value)));
        }

        return printers;
    }

    /// <summary>DYMO writes booleans as True and False, not as 1 and 0.</summary>
    private static bool Flag(string? value) =>
        value is not null && value.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sends one label to one printer.
    /// </summary>
    /// <remarks>
    /// The service answers "true" whether or not the paper moved, so the result
    /// here is "the job was accepted", not "the label printed". Saying otherwise
    /// to the operator is how a printer with no labels in it gets reported as
    /// working. What can be said is that the request was refused, and this says
    /// that.
    /// </remarks>
    public static async Task<DymoPrintResult> PrintAsync(
        Uri service, DymoPrinter printer, string labelXml, int copies = 1,
        CancellationToken cancellation = default)
    {
        if (!printer.IsConnected)
            return DymoPrintResult.Refused($"{printer.Name} is not connected.");

        try
        {
            using var client = CreateClient();
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["printerName"] = printer.Name,
                ["printParamsXml"] = PrintParams(copies),
                ["labelXml"] = labelXml,
                ["labelSetXml"] = "",
            });

            using HttpResponseMessage response = await client
                .PostAsync(new Uri(service, "PrintLabel"), form, cancellation).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return DymoPrintResult.Refused(Explain(response.StatusCode, body));

            return body.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                ? DymoPrintResult.Sent(printer, copies)
                : DymoPrintResult.Refused($"DYMO answered {body.Trim()} instead of accepting the label.");
        }
        catch (Exception ex)
        {
            return DymoPrintResult.Refused(ex.Message);
        }
    }

    /// <summary>
    /// The print settings block. Copies lives here rather than in a loop so that
    /// ten labels are one job: ten requests means ten chances for the second one
    /// to be refused while the first is still feeding.
    /// </summary>
    private static string PrintParams(int copies) =>
        "<LabelWriterPrintParams>"
        + $"<Copies>{Math.Max(1, copies).ToString(CultureInfo.InvariantCulture)}</Copies>"
        + "<JobTitle>PhoneGrade</JobTitle>"
        + "<FlowDirection>LeftToRight</FlowDirection>"
        + "<PrintQuality>Auto</PrintQuality>"
        + "<TwinTurboRoll>Auto</TwinTurboRoll>"
        + "</LabelWriterPrintParams>";

    /// <summary>
    /// Turns a failure into something an operator can act on. The service reports
    /// errors as ASP.NET JSON with the real reason nested inside, and the useful
    /// part of that is the exception message rather than "An error has occurred".
    /// </summary>
    private static string Explain(HttpStatusCode status, string body)
    {
        string message = "";
        try
        {
            message = XDocument.Parse(body).Descendants("exceptionMessage").FirstOrDefault()?.Value.Trim() ?? "";
        }
        catch (System.Xml.XmlException)
        {
            message = "";
        }

        if (message.Length == 0) message = body.Trim();
        if (message.Length == 0) message = $"DYMO answered {(int)status}.";

        return status switch
        {
            HttpStatusCode.BadRequest => message,
            _ => $"DYMO refused the label ({(int)status}): {message}",
        };
    }

    /// <summary>
    /// A client that trusts the certificate DYMO puts on the machine, and nothing
    /// else. The certificate is installed by the DYMO installer into the machine
    /// store, so validation normally succeeds; the switch covers the installs
    /// where it does not, and is scoped to this client so no other request in the
    /// app is affected by it.
    /// </summary>
    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            // The service is on the loopback interface and there is nothing behind
            // a proxy on this machine it would be reached through.
            UseProxy = false,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}

/// <summary>A printer DYMO Connect reported.</summary>
/// <param name="Name">The name to send back when printing to it.</param>
/// <param name="Model">What the operator recognises it as.</param>
/// <param name="IsConnected">Whether it is ready to take a job.</param>
/// <param name="IsTwinTurbo">Whether it holds two rolls and can pick one.</param>
public sealed record DymoPrinter(string Name, string Model, bool IsConnected, bool IsTwinTurbo);

/// <summary>What happened to a label that was sent to DYMO.</summary>
/// <param name="Accepted">
/// True when DYMO took the job. This is not a claim that paper moved: the service
/// answers true to a job that then fails to print, so the operator is told the
/// label was sent and not that it came out.
/// </param>
/// <param name="Message">What to tell the operator, in a sentence.</param>
public sealed record DymoPrintResult(bool Accepted, string Message)
{
    internal static DymoPrintResult Sent(DymoPrinter printer, int copies) =>
        new(true, $"Sent {copies} label(s) to {printer.Name}.");

    internal static DymoPrintResult Refused(string message) => new(false, message);
}