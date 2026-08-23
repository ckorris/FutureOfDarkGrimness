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

    /// <summary>What one book's refresh changed. <see cref="Priced"/> options were flagged unpriced and now
    /// carry a real cost recovered from OPR's per-unit costs[] (the core #219 fix); <see cref="Repriced"/>
    /// already-priced options whose number moved; <see cref="Unmatched"/> options had a (unit, option) key
    /// absent from the live book (OPR renamed/removed it) and were left untouched. <see cref="Samples"/> shows
    /// a few concrete before/after lines.</summary>
    public sealed record BookCostRefreshReport(
        string BookName, int Priced, int Repriced, int Unmatched, IReadOnlyList<string> Samples);

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

    /// <summary>Pure, network-free core: re-import <paramref name="rawBookJson"/> (now cost-aware) and copy the
    /// resolved per-unit Cost + costUnpriced onto <paramref name="bundled"/>, matched by (unit Id, option Id) -
    /// the SAME option Id costs differently on different units, so a flat option-Id match would smear one price
    /// across all of them. Mutates <paramref name="bundled"/> in place and returns what changed.</summary>
    public static BookCostRefreshReport RefreshCosts(BookFile bundled, string rawBookJson)
    {
        BookFile fresh = OprBookImporter.Import(rawBookJson, bundled.Source, bundled.License);

        var freshByKey = new Dictionary<(string Unit, string Option), UpgradeOption>();
        foreach (RosterUnit u in fresh.Units)
            foreach (UpgradeOption o in u.Sections.SelectMany(s => s.Options))
                freshByKey[(u.Id, o.Id)] = o;

        int priced = 0, repriced = 0, unmatched = 0;
        var samples = new List<string>();
        foreach (RosterUnit u in bundled.Units)
            foreach (UpgradeOption o in u.Sections.SelectMany(s => s.Options))
            {
                if (!freshByKey.TryGetValue((u.Id, o.Id), out UpgradeOption? f)) { unmatched++; continue; }

                bool wasUnpriced = o.CostUnpriced;
                int oldCost = o.Cost;
                o.Cost = f.Cost;
                o.CostUnpriced = f.CostUnpriced;

                if (wasUnpriced && !f.CostUnpriced)
                {
                    priced++;
                    if (samples.Count < 8) samples.Add($"{u.Name} / {o.Label}: was unpriced -> {f.Cost} pts");
                }
                else if (!wasUnpriced && oldCost != f.Cost)
                {
                    repriced++;
                    if (samples.Count < 8) samples.Add($"{u.Name} / {o.Label}: {oldCost} -> {f.Cost} pts");
                }
            }
        return new BookCostRefreshReport(bundled.Name, priced, repriced, unmatched, samples);
    }

    /// <summary>What one book's #383 per-model section refresh changed: <see cref="Stamped"/> sections
    /// were re-classified (Affects -> Any, PerModelBudget set); <see cref="Unmatched"/> bundled sections
    /// had no (unit, section) key in the live book and were left untouched.</summary>
    public sealed record SectionShapeRefreshReport(
        string BookName, int Stamped, int Unmatched, IReadOnlyList<string> Samples);

    /// <summary>Pure, network-free core (#383): re-import <paramref name="rawBookJson"/> with the
    /// per-model-aware importer and copy each per-model section's classified shape (Affects +
    /// PerModelBudget) onto <paramref name="bundled"/>, matched by (unit Id, section LABEL) - the
    /// classification needs the raw `select`/`model` fields the snapshot never stored, and section
    /// `id`s regenerate on every OPR publish (unit and option ids are stable, section ids are not),
    /// so the label is the only cross-publish section key. A (unit, label) that appears twice in the
    /// live book stamps only when every duplicate classifies identically. Only sections the live book
    /// classifies per-model are touched; all other curation is preserved (the <see cref="RefreshCosts"/>
    /// discipline). Mutates <paramref name="bundled"/> in place.</summary>
    public static SectionShapeRefreshReport RefreshPerModelSections(BookFile bundled, string rawBookJson)
    {
        BookFile fresh = OprBookImporter.Import(rawBookJson, bundled.Source, bundled.License);

        var freshByKey = new Dictionary<(string Unit, string Label), List<UpgradeSection>>();
        foreach (RosterUnit u in fresh.Units)
            foreach (UpgradeSection s in u.Sections)
            {
                if (!freshByKey.TryGetValue((u.Id, s.Label), out List<UpgradeSection>? group))
                    freshByKey[(u.Id, s.Label)] = group = new List<UpgradeSection>();
                group.Add(s);
            }

        int stamped = 0, unmatched = 0;
        var samples = new List<string>();
        foreach (RosterUnit u in bundled.Units)
            foreach (UpgradeSection s in u.Sections)
            {
                if (!freshByKey.TryGetValue((u.Id, s.Label), out List<UpgradeSection>? group))
                {
                    unmatched++;
                    continue;
                }

                UpgradeSection f = group[0];
                if (group.Any(g => g.PerModelBudget != f.PerModelBudget || g.Affects != f.Affects)) continue;
                if (!f.PerModelBudget || (s.PerModelBudget && s.Affects == f.Affects)) continue;

                s.Affects = f.Affects;
                s.PerModelBudget = true;
                stamped++;
                if (samples.Count < 12) samples.Add($"{u.Name} / {s.Label}");
            }
        return new SectionShapeRefreshReport(bundled.Name, stamped, unmatched, samples);
    }

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
