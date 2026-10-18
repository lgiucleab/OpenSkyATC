using System.Globalization;
using System.Net;
using System.Text;
using OpenSkyAtc.Core.Debrief;
using OpenSkyAtc.Core.Model;
using OpenSkyAtc.Host;

namespace OpenSkyAtc.App.ViewModels;

public sealed record DebriefLine(string Time, string Text);

/// <param name="Tone">"Critical", "Caution", "Info" or "Neutral": the colour of the section's marker.</param>
public sealed record DebriefSection(string Title, string Tone, IReadOnlyList<DebriefLine> Lines);

/// <summary>A saved debrief (step 16), worded in the app's language for the debrief window and the HTML export.</summary>
public sealed class DebriefViewModel
{
    public DebriefViewModel(SavedDebrief saved)
    {
        Path = saved.Path;
        var d = saved.Debrief;
        Debrief = d;
        Heading = $"{d.Origin} → {d.Destination} · {d.Callsign}";
        string when = d.EndedAt is { } at ? at.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : "";
        string how = d.Completed ? Loc.Get("Debrief.Completed") : Loc.Format("Debrief.EndedEarly", Loc.Get($"Phase.{d.LastPhase}"));
        Details = string.Join(" · ", new[] { when, Duration(d.Duration), how }.Where(s => s.Length > 0));
        Score = d.Score;
        ScoreTone = d.Score >= 85 ? "Success" : d.Score >= 60 ? "Caution" : "Critical";
        ReadbackSummary = Loc.Format("Debrief.ReadbackSummary", d.ReadbacksCorrect, d.Readbacks);
        var timed = d.Handoffs.Where(h => h.Seconds is not null).ToList();
        HandoffSummary = timed.Count == 0 ? Loc.Get("Debrief.NoHandoffs")
            : Loc.Format("Debrief.HandoffSummary", d.Handoffs.Count, Math.Round(timed.Average(h => h.Seconds!.Value)));

        Sections =
        [
            .. Section("Debrief.Serious", "Critical", d, FindingSeverity.Serious),
            .. Section("Debrief.Deviations", "Critical", d, FindingSeverity.Deviation),
            .. Section("Debrief.Warnings", "Caution", d, FindingSeverity.Warning),
            .. Section("Debrief.Readbacks", "Caution", d, FindingSeverity.Readback),
            .. Section("Debrief.Handoffs", "Caution", d, FindingSeverity.Handoff),
            .. Section("Debrief.Info", "Info", d, FindingSeverity.Info),
        ];
    }

    public FlightDebrief Debrief { get; }
    public string Path { get; }
    public string Heading { get; }
    public string Details { get; }
    public int Score { get; }

    /// <summary>"Success" (85+), "Caution" (60+) or "Critical": the score's colour.</summary>
    public string ScoreTone { get; }
    public string ReadbackSummary { get; }
    public string HandoffSummary { get; }
    public IReadOnlyList<DebriefSection> Sections { get; }
    public bool Clean => Sections.Count == 0;

    private static IEnumerable<DebriefSection> Section(string titleKey, string tone, FlightDebrief d, FindingSeverity severity)
    {
        var lines = d.Findings.Where(f => f.Severity == severity).Select(f => new DebriefLine(Time(f.Time), Finding(f))).ToList();
        if (lines.Count > 0) yield return new DebriefSection($"{Loc.Get(titleKey)} ({lines.Count})", tone, lines);
    }

    public static string Time(TimeSpan t) => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? Loc.Format("Debrief.HoursMinutes", (int)t.TotalHours, t.Minutes) : Loc.Format("Debrief.Minutes", Math.Max(1, (int)Math.Round(t.TotalMinutes)));

    /// <summary>One finding as a sentence in the app's language.</summary>
    public static string Finding(DebriefFinding f) => f.Kind switch
    {
        FindingKind.Conformance => Conformance(f),
        FindingKind.ReadbackWrong => Loc.Format("Debrief.ReadbackWrong", Instruction(f.Tag), Items(f.Wrong, f.Missing)),
        FindingKind.ReadbackMissed => Loc.Format("Debrief.ReadbackMissed", Instruction(f.Tag)),
        FindingKind.HandoffLate => Loc.Format("Debrief.HandoffLate", f.Station, Math.Round(f.Seconds ?? 0)),
        FindingKind.HandoffMissed => Loc.Format("Debrief.HandoffMissed", f.Station),
        FindingKind.HandoffSkipped => Loc.Format("Debrief.HandoffSkipped", f.Other, f.Station),
        FindingKind.GoAround => Loc.Get("Debrief.GoAround"),
        _ => f.Kind.ToString(),
    };

    private static string Conformance(DebriefFinding f)
    {
        string what = Loc.Get($"Check.{f.Check}");
        string worst = f.Worst is { } w && f.Unit is { Length: > 0 } unit
            ? Loc.Format(unit == "s" ? "Debrief.For" : "Debrief.UpTo", Math.Round(w).ToString("N0", CultureInfo.CurrentCulture), unit == "deg" ? "°" : " " + unit)
            : "";
        string level = f.Level is >= 1 and <= 3 && f.Severity != FindingSeverity.Serious ? Loc.Get($"Debrief.Level{f.Level}") : "";
        return string.Join(" · ", new[] { what + worst, level }.Where(s => s.Length > 0));
    }

    private static string Instruction(string? tag) =>
        tag is null ? Loc.Get("Tag.other") : Loc.Instance[$"Tag.{tag}"] is { } text && text != $"Tag.{tag}" ? text : tag;

    private static string Items(IReadOnlyList<Core.Conversation.ReadbackItem>? wrong, IReadOnlyList<Core.Conversation.ReadbackItem>? missing)
    {
        var parts = new List<string>();
        if (wrong is { Count: > 0 }) parts.Add(Loc.Format("Debrief.Wrong", string.Join(", ", wrong.Select(i => Loc.Get($"Item.{i}")))));
        if (missing is { Count: > 0 }) parts.Add(Loc.Format("Debrief.Missing", string.Join(", ", missing.Select(i => Loc.Get($"Item.{i}")))));
        return string.Join("; ", parts);
    }

    /// <summary>A standalone HTML page of the debrief (no scripts, no external files), for the Export button.</summary>
    public string ToHtml()
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        string scoreColour = ScoreTone switch { "Success" => "#1f8a4c", "Caution" => "#b7791f", _ => "#c53030" };
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html lang=\"").Append(E(Loc.Instance.Language)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(E($"{Loc.Get("Debrief.Title")}: {Heading}")).Append("</title>\n<style>\n")
            .Append("body{font-family:'Segoe UI',system-ui,sans-serif;max-width:760px;margin:24px auto;padding:0 16px;color:#1a202c;background:#fff}\n")
            .Append("h1{font-size:22px;margin:0}.details{color:#5a6474;margin:4px 0 16px}\n")
            .Append(".score{font-size:44px;font-weight:600;color:").Append(scoreColour).Append("}.score small{font-size:18px;color:#5a6474}\n")
            .Append(".summary{margin:4px 0}h2{font-size:16px;margin:22px 0 6px;border-bottom:1px solid #e2e8f0;padding-bottom:4px}\n")
            .Append("table{border-collapse:collapse;width:100%}td{padding:4px 8px 4px 0;vertical-align:top}td.t{color:#5a6474;white-space:nowrap;font-variant-numeric:tabular-nums;width:5.5em}\n")
            .Append("footer{margin-top:28px;color:#8a94a6;font-size:12px}\n")
            .Append("@media (prefers-color-scheme:dark){body{background:#14171c;color:#e8ecf1}.details,td.t,.score small{color:#9aa4b2}h2{border-color:#2d3440}}\n")
            .Append("</style>\n</head>\n<body>\n");
        html.Append("<h1>").Append(E(Heading)).Append("</h1>\n<div class=\"details\">").Append(E(Details)).Append("</div>\n");
        html.Append("<div class=\"score\">").Append(Score).Append(" <small>/ 100</small></div>\n");
        html.Append("<div class=\"summary\">").Append(E(ReadbackSummary)).Append("</div>\n<div class=\"summary\">").Append(E(HandoffSummary)).Append("</div>\n");
        if (Clean) html.Append("<h2>").Append(E(Loc.Get("Debrief.Clean"))).Append("</h2>\n");
        foreach (var s in Sections)
        {
            html.Append("<h2>").Append(E(s.Title)).Append("</h2>\n<table>\n");
            foreach (var l in s.Lines) html.Append("<tr><td class=\"t\">").Append(E(l.Time)).Append("</td><td>").Append(E(l.Text)).Append("</td></tr>\n");
            html.Append("</table>\n");
        }
        if (Debrief.Handoffs.Count > 0)
        {
            html.Append("<h2>").Append(E(Loc.Get("Debrief.HandoffTimes"))).Append("</h2>\n<table>\n");
            foreach (var h in Debrief.Handoffs)
                html.Append("<tr><td class=\"t\">").Append(E(Time(h.Time))).Append("</td><td>").Append(E($"{h.From} → {h.To}"))
                    .Append("</td><td class=\"t\">").Append(E(h.Seconds is { } sec ? $"{Math.Round(sec)} s" : "—")).Append("</td></tr>\n");
            html.Append("</table>\n");
        }
        html.Append("<footer>OpenSkyATC</footer>\n</body>\n</html>\n");
        return html.ToString();
    }
}
