using System.Net;
using System.Text.Json;
using OpenSkyAtc.Core.Model;

namespace OpenSkyAtc.SimBrief;

/// <summary>
/// Fetches the pilot's latest SimBrief flight plan, only when the user asks (never polled). Public endpoint, no key:
/// https://www.simbrief.com/api/xml.fetcher.php?userid=PILOT_ID&amp;json=v2 (docs/decisions.md, step 11).
/// SimBrief plans are for flight simulation only, never for real-world navigation.
/// </summary>
public sealed class SimBriefClient(HttpClient http)
{
    public const string Endpoint = "https://www.simbrief.com/api/xml.fetcher.php";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Raw response of the last successful fetch, for saving next to the flight if the host wants to.</summary>
    public string? LastResponse { get; private set; }

    public async Task<FlightPlan> FetchLatestAsync(string? pilotId, CancellationToken cancel = default)
    {
        string id = ValidPilotId(pilotId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Timeout);

        HttpResponseMessage response;
        string body;
        try
        {
            response = await http.GetAsync($"{Endpoint}?userid={id}&json=v2", timeout.Token);
            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException e) when (!cancel.IsCancellationRequested)
        {
            throw new FlightPlanImportException(FlightPlanImportError.Network, $"SimBrief did not answer within {Timeout.TotalSeconds:0} s. Check your internet connection and try again.", e);
        }
        catch (HttpRequestException e)
        {
            throw new FlightPlanImportException(FlightPlanImportError.Network, $"Could not reach SimBrief ({e.Message}). Check your internet connection and try again.", e);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest) throw ErrorFrom(body);
            if (!response.IsSuccessStatusCode)
                throw new FlightPlanImportException(FlightPlanImportError.Network, $"SimBrief answered with an error (HTTP {(int)response.StatusCode}). Try again later.");
        }

        var plan = OfpParser.Parse(body);
        LastResponse = body;
        return plan;
    }

    /// <summary>A Pilot ID is a number of up to 7 digits (SimBrief Account Settings).</summary>
    public static string ValidPilotId(string? pilotId)
    {
        string id = pilotId?.Trim() ?? "";
        if (id.Length == 0)
            throw new FlightPlanImportException(FlightPlanImportError.MissingPilotId, "No SimBrief Pilot ID is set. Find it on the SimBrief Account Settings page and enter it in the settings.");
        if (id.Length > 7 || !id.All(char.IsAsciiDigit))
            throw new FlightPlanImportException(FlightPlanImportError.MissingPilotId, $"'{id}' is not a SimBrief Pilot ID (a number of up to 7 digits, on the SimBrief Account Settings page).");
        return id;
    }

    /// <summary>HTTP 400: an unknown user or another error, as JSON (json=v2) or a small XML message.</summary>
    private static FlightPlanImportException ErrorFrom(string body)
    {
        string? status = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("fetch", out var fetch) && fetch.TryGetProperty("status", out var s)) status = s.GetString();
        }
        catch (JsonException)
        {
            int start = body.IndexOf("<status>", StringComparison.OrdinalIgnoreCase), end = body.IndexOf("</status>", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start) status = body[(start + 8)..end];
        }
        return OfpParser.FetchError(status ?? "Error");
    }
}
