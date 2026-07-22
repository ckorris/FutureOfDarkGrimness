using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FDG.ArmyBuilding;

namespace FdgRaylib.Import;

// #219 Slice 2 — refresh the bundled .fdgbook catalog's "unpriced upgrade" flags from OPR's army-book
// endpoint. The bundled snapshots record every option's cost as a plain number and carry costUnpriced=false
// everywhere (the flag was stamped by a later re-serialize, not a real import), so options OPR prices inside
// its own algorithm - it omits the `cost` key entirely on those - look identical to genuinely free ones.
// OPR never publishes the hidden numbers, but the /api/army-books/{uid} endpoint DOES still distinguish
// "cost present" from "cost absent", so we can recover the priced/unpriced DISTINCTION and set the flag.
//
// We do NOT rewrite the books wholesale (that would drop effect sets #239, embedded ruleDefinitions
// #153/#196/#197 and base retrofits #225). Instead we re-import to a throwaway BookFile and transfer ONLY
// the costUnpriced flags onto the existing book, matched by the OPR-stable option Id.
public static class ArmyForgeBookService
{
    private const string BaseUrl = "https://army-forge.onepagerules.com";

    // Every bundled book is a Grimdark Future faction (verified: all 47 names appear in the GF official list).
    private const string GameSystemSlug = "grimdark-future";
    private const int GameSystem = 2;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>What one book's refresh changed. <see cref="Unmatched"/> options had an Id absent from the
    /// live endpoint (OPR renamed/removed the option since the snapshot) and were left untouched;
    /// <see cref="Deltas"/> are informational base-cost differences on priced options - reported, not applied
    /// (that would be a separate #218-class change).</summary>
    public sealed record BookCostRefreshReport(
        string BookName, int Flagged, int Cleared, int Unmatched, IReadOnlyList<string> Deltas);

    /// <summary>OPR official-books listing for Grimdark Future, as a case-insensitive book-name -> uid map.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> FetchBookIndexAsync(CancellationToken ct = default)
    {
        string json = await GetAsync(
            $"{BaseUrl}/api/army-books?filters=official&gameSystemSlug={GameSystemSlug}", "the book index", ct);
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("name", out JsonElement name) && e.TryGetProperty("uid", out JsonElement uid)
                && name.GetString() is { Length: > 0 } n && uid.GetString() is { Length: > 0 } u)
                index[n] = u;
        }
        return index;
    }

    /// <summary>Fetch the full OPR army-book JSON for a uid (the endpoint that still carries per-option costs).</summary>
    public static Task<string> FetchBookJsonAsync(string uid, CancellationToken ct = default) =>
        GetAsync($"{BaseUrl}/api/army-books/{Uri.EscapeDataString(uid)}?gameSystem={GameSystem}",
            $"army book '{uid}'", ct);

    /// <summary>Pure, network-free core: re-import <paramref name="rawBookJson"/> and copy only the
    /// costUnpriced flags onto <paramref name="bundled"/>, matched by option Id. Mutates <paramref name="bundled"/>
    /// in place and returns what changed. Priced options keep their existing cost (any drift is reported, not
    /// applied).</summary>
    public static BookCostRefreshReport RefreshCostFlags(BookFile bundled, string rawBookJson)
    {
        BookFile fresh = OprBookImporter.Import(rawBookJson, bundled.Source, bundled.License);

        var freshUnpriced = new HashSet<string>();
        var freshCost = new Dictionary<string, int>();
        foreach (UpgradeOption o in Options(fresh))
        {
            if (o.CostUnpriced) freshUnpriced.Add(o.Id);
            else freshCost[o.Id] = o.Cost;
        }

        int flagged = 0, cleared = 0, unmatched = 0;
        var deltas = new List<string>();
        foreach (UpgradeOption o in Options(bundled))
        {
            bool shouldBeUnpriced = freshUnpriced.Contains(o.Id);
            if (!shouldBeUnpriced && !freshCost.ContainsKey(o.Id)) { unmatched++; continue; }

            if (shouldBeUnpriced && !o.CostUnpriced) { o.CostUnpriced = true; flagged++; }
            else if (!shouldBeUnpriced && o.CostUnpriced) { o.CostUnpriced = false; cleared++; }

            if (!shouldBeUnpriced && freshCost.TryGetValue(o.Id, out int fc) && fc != o.Cost)
                deltas.Add($"{o.Label}: bundled {o.Cost} pts, OPR {fc} pts");
        }
        return new BookCostRefreshReport(bundled.Name, flagged, cleared, unmatched, deltas);
    }

    private static IEnumerable<UpgradeOption> Options(BookFile book) =>
        book.Units.SelectMany(u => u.Sections).SelectMany(s => s.Options);

    private static async Task<string> GetAsync(string url, string what, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach Army Forge: {ex.Message}", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Army Forge did not respond (timed out).");
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Army Forge returned HTTP {(int)response.StatusCode} fetching {what}.");
        return await response.Content.ReadAsStringAsync(ct);
    }
}
