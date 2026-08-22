using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;

namespace FdgRaylib.Import;

// #241 — the online half of the Army Forge share-link import: parse a share link (or bare list id),
// fetch the resolved list JSON (/api/tts) plus every referenced army book (/api/army-books, needed for
// the 3.5.x version gate), run the engine's OprListImporter, then attach the matching bundled book's
// rule/spell definitions so faction rules enforce like a Forge-built army. The /api/tts endpoint is the
// one OPR's own Tabletop Simulator mod uses — undocumented but long-stable; every failure path here
// surfaces as a plain-language InvalidOperationException for the UI/CLI to show.
public static class ArmyForgeShareService
{
    private const string BaseUrl = "https://army-forge.onepagerules.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Everything the preview shows: the import result plus which rule names will be inert in
    /// play (no definition after the bundled-book attach) and whether a bundled book matched at all.
    /// When it did, <see cref="ForgeSession"/> carries the editable reconstruction against that book
    /// ("Open in Forge") with its points reconciliation; null when no book matched or the rebuild threw
    /// (disclosed via a warning) — Save As keeps working either way.</summary>
    public sealed class ImportOutcome
    {
        public required OprListImportResult Result { get; init; }
        public required IReadOnlyList<string> InertRules { get; init; }
        public required bool FactionBookMatched { get; init; }
        public OprForgeSessionResult? ForgeSession { get; init; }
        public BookFile? BundledBook { get; init; }
    }

    /// <summary>Accepts a full share link (https://army-forge.onepagerules.com/share?id=XXX&amp;name=...),
    /// any army-forge URL carrying an id query parameter, or a bare list id.</summary>
    public static bool TryParseShareId(string input, out string id)
    {
        id = string.Empty;
        input = input.Trim();
        if (input.Length == 0) return false;

        if (input.Contains("://") || input.StartsWith("army-forge", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(input.Contains("://") ? input : "https://" + input, UriKind.Absolute, out Uri? uri))
                return false;
            foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || !pair[..eq].Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = Uri.UnescapeDataString(pair[(eq + 1)..]);
                if (!IsIdShaped(candidate)) return false;
                id = candidate;
                return true;
            }
            return false;
        }

        if (!IsIdShaped(input)) return false;
        id = input;
        return true;
    }

    private static bool IsIdShaped(string s) =>
        s.Length >= 6 && s.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    public static async Task<ImportOutcome> FetchAndImportAsync(string linkOrId, CancellationToken ct = default)
    {
        if (!TryParseShareId(linkOrId, out string id))
            throw new InvalidOperationException(
                "That does not look like an Army Forge share link or list id. " +
                "Expected e.g. https://army-forge.onepagerules.com/share?id=...");

        string listJson = await GetAsync($"{BaseUrl}/api/tts?id={Uri.EscapeDataString(id)}",
            "the shared list (is the link correct?)", ct);
        OprListHeader header = OprListImporter.Peek(listJson);

        var bookJsons = new Dictionary<string, string>();
        foreach (string armyId in header.ArmyIds)
        {
            bookJsons[armyId] = await GetAsync(
                $"{BaseUrl}/api/army-books/{Uri.EscapeDataString(armyId)}?gameSystem={Uri.EscapeDataString(header.GameSystem)}",
                $"army book '{armyId}'", ct);
        }

        OprListImportResult result = OprListImporter.Import(listJson, bookJsons);

        BookFile? bundled = MatchBundledBook(result.Army.Faction);
        if (bundled is not null)
            OprListImporter.AttachBookDefinitions(result.Army, bundled);
        else if (result.Army.Faction.Length > 0)
            result.Warnings.Add($"No bundled book matches faction '{result.Army.Faction}' - " +
                "faction-specific rules and spells will not be enforced.");

        // #241 v2: the editable session + pricing reconciliation. Best-effort — a failure here never
        // blocks the verbatim import, it just leaves the Open-in-Forge button disabled.
        OprForgeSessionResult? session = null;
        if (bundled is not null)
        {
            try
            {
                session = OprListImporter.ReconstructSelections(listJson, bundled);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Could not rebuild an editable Forge session: {ex.Message}");
            }
        }

        return new ImportOutcome
        {
            Result = result,
            InertRules = OprListImporter.UnresolvedRuleNames(result.Army),
            FactionBookMatched = bundled is not null,
            ForgeSession = session,
            BundledBook = bundled,
        };
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
            throw new InvalidOperationException(
                $"Army Forge returned HTTP {(int)response.StatusCode} fetching {what}.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    // The same bundled snapshots the Forge screen offers (Assets/Books). Name match is case-insensitive;
    // OPR book names and our imported book names come from the same source strings.
    private static BookFile? MatchBundledBook(string factionName)
    {
        if (string.IsNullOrWhiteSpace(factionName)) return null;
        string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        if (!Directory.Exists(dir)) return null;

        foreach (string path in Directory.EnumerateFiles(dir, "*" + BookFile.EXTENSION_WITH_PERIOD).OrderBy(p => p))
        {
            try
            {
                BookFile? book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options);
                if (book is not null && string.Equals(book.Name, factionName, StringComparison.OrdinalIgnoreCase))
                    return book;
            }
            catch { /* skip a malformed book, same tolerance as the Forge screen */ }
        }
        return null;
    }
}
