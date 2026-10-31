using System.Text.RegularExpressions;
using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FdgRaylib.Cli;

/// <summary>
/// #440 - rebuild a saved Forge army against the CURRENT bundled book (the `--reforge` dev flag). A curated
/// `.fdgarmy` embeds the book it was authored with, and its playable half is that book's stats, rules, spells
/// and points, so re-baking the bundled books never touches it. This re-derives the whole file from the bundled
/// book: the upgrade picks are recovered, then the army is recompiled from them, so every stat, rule, spell,
/// effect set and point cost is the current book's, and the file reopens in the Forge (the compiled army
/// carries its selections and the book it was compiled from).
///
/// <para>Picks are recovered per unit, in order of trust:</para>
/// <list type="number">
///   <item><see cref="SelectionSolver"/> against the new book - the exact match on size + weapon loadout the
///   `--retrofit-editable` tool uses (#357). Handles joined heroes and symmetric combined pairs.</item>
///   <item>Label translation of the file's own embedded selections: the same roster unit (by id, else name),
///   each pick mapped to the new book's section with the same label and the option with the same label, the
///   same name before its profile, or the same gains. Catches the units whose loadout the new edition
///   reprinted (a renamed or re-statted weapon defeats the solver's weapon-signature match, not the pick).</item>
///   <item>Nearest fit (<see cref="SelectionSolver.SolveNearest"/>): the same roster unit (by name, else the
///   embedded selection's roster id), with whichever pick-set compiles closest to the saved unit by
///   <see cref="Closeness"/> - size first, then weapon profiles (name, melee/ranged role, range, attacks, AP,
///   rules), then unit rules. This is the edition-restructure case: 3.6 moved an option into a unit's base
///   loadout or reprinted a profile, so no pick reproduces the old unit but the same unit still exists.</item>
///   <item>None: the unit is not in the new book at all. Reported UNSOLVED and left out, for a hand
///   substitution.</item>
/// </list>
///
/// <para>A file whose embedded book already IS the bundled edition is recompiled from its own selections
/// (the hand-edit loop: adjust the selections, re-run, and the playable half follows).</para>
/// </summary>
public static class ArmyReforge
{
    public sealed record UnitLine(string OldName, int OldModels, int OldPoints, string Method)
    {
        public string? NewName { get; init; }
        public int NewModels { get; init; }
        public int NewPoints { get; init; }
        public string? Failure { get; init; }

        /// <summary>Weapon copies the rebuilt unit lost / gained against the saved one ("-Rifle x2, +Battle Rifle x2").
        /// Empty when the loadout is the same by name.</summary>
        public string Diff { get; init; } = string.Empty;
    }

    public sealed class Report
    {
        public required string FileName { get; init; }
        public string? BookName { get; set; }
        public string? OldVersion { get; set; }
        public string? NewVersion { get; set; }
        public bool Recompiled { get; set; }
        public List<UnitLine> Units { get; } = new();
        public int OldTotal { get; set; }
        public int RecoveredTotal { get; set; }
        public int NewTotal { get; set; }
        public int? Bracket { get; set; }

        /// <summary>What the bracket fit did, in order (each with its points effect).</summary>
        public List<string> FitActions { get; } = new();
        public List<string> Issues { get; } = new();
        public BuiltArmyFile? Rebuilt { get; set; }
        public string? Error { get; set; }

        public int Unsolved => Units.Count(u => u.Failure != null);
        public bool OverBracket => Bracket is int b && NewTotal > b;
    }

    /// <summary>
    /// Units a 3.6 book no longer has, mapped by hand to the roster units that now fill their role (#440 OWNER
    /// REVIEW D entries). The nearest fit then picks among these the closest unit and loadout, exactly as it does
    /// for a unit that kept its name, so the substitution is the judgement and the loadout follows mechanically.
    /// Keyed by (current book name, saved unit name).
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Book, string Unit), string[]> Substitutions =
        new Dictionary<(string, string), string[]>
        {
            // 3.5's five-model plasma squad (Relentless): 3.6 folds it into Support Prime Brothers' plasma swaps.
            [("Prime Brothers", "Blaster Brothers")] = new[] { "Support Prime Brothers" },
            // 3.5's winged grunts: 3.6 makes Wings an all-model upgrade on the grunt squads.
            [("Alien Hives", "Winged Grunts")] = new[] { "Assault Grunts", "Shooter Grunts" },
        };

    /// <summary>The points bracket a catalog file name declares ("2k - Orks", "Daemons 1k"), or null.</summary>
    public static int? BracketFromFileName(string fileName)
    {
        Match m = Regex.Match(Path.GetFileNameWithoutExtension(fileName), @"(?<![0-9])([1-9])k(?![a-z])",
            RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) * 1000 : null;
    }

    public static Report Reforge(string fileName, BuiltArmyFile old, IReadOnlyList<BookFile> books,
        bool fitBracket = true)
    {
        var report = new Report
        {
            FileName = fileName,
            OldTotal = old.TotalPoints,
            OldVersion = old.Book?.Version,
            Bracket = BracketFromFileName(fileName),
        };

        BookFile? book = books.FirstOrDefault(b => OprBookNames.Matches(b, old.Faction, old.GameSystem));
        if (book is null)
        {
            report.Error = $"no bundled book matches faction '{old.Faction}'";
            return report;
        }
        report.BookName = book.Name;
        report.NewVersion = book.Version;

        BuilderList list;
        if (old.Book is not null && old.Selections is not null && old.Book.Version == book.Version)
        {
            // Already on this edition: the selections are the source of truth, recompile from them.
            report.Recompiled = true;
            list = old.Selections;
            list.Name = old.Name;
            list.BookName = book.Name;
            string? unknown = list.Units.Select(u => u.RosterUnitId)
                .FirstOrDefault(id => book.Units.All(r => r.Id != id));
            if (unknown != null)
            {
                report.Error = $"selections name roster unit '{unknown}', which book '{book.Name}' does not have";
                return report;
            }
        }
        else
        {
            list = new BuilderList { Name = old.Name, BookName = book.Name, PointsLimit = old.PointsLimit };
            ArmySolve solve = SelectionSolver.Solve(book, old);
            List<List<BuilderUnit>?> aligned = AlignSelections(old);

            for (int i = 0; i < old.Units.Count; i++)
            {
                UnitFileEntry entry = old.Units[i];
                (string method, List<BuilderUnit>? picks, string? why) = Recover(book, old, entry, solve.Units[i], aligned[i]);
                var line = new UnitLine(entry.Name, entry.ModelCount, entry.PointCost, method);
                if (picks is null)
                {
                    report.Units.Add(line with { Failure = why });
                    continue;
                }

                list.Units.AddRange(picks);
                UnitFileEntry rebuiltUnit = ListCompiler.Compile(book, new BuilderList
                {
                    BookName = book.Name,
                    Units = picks.Select(Copy).ToList(),
                }).Units[0];
                report.Units.Add(line with
                {
                    NewName = rebuiltUnit.Name, NewModels = rebuiltUnit.ModelCount, NewPoints = rebuiltUnit.PointCost,
                    Diff = WeaponDiff(entry, rebuiltUnit),
                });
            }

            ClearDanglingJoins(list);
        }

        report.RecoveredTotal = ListCompiler.Compile(book, list).TotalPoints;
        if (fitBracket && report.Bracket is int bracket && !report.Recompiled)
        {
            list.PointsLimit = bracket;
            FitBracket(book, list, bracket, report.FitActions);
        }

        BuiltArmyFile rebuilt = ListCompiler.Compile(book, list);
        rebuilt.Name = old.Name;
        report.Rebuilt = rebuilt;
        report.NewTotal = rebuilt.TotalPoints;
        if (report.Recompiled)
            foreach (UnitFileEntry u in rebuilt.Units)
                report.Units.Add(new UnitLine(u.Name, 0, 0, "recompiled")
                {
                    NewName = u.Name, NewModels = u.ModelCount, NewPoints = u.PointCost,
                });

        foreach (ListIssue issue in ListValidator.Validate(book, list, rebuilt))
            report.Issues.Add($"{issue.Severity}: {issue.Message}");
        return report;
    }

    /// <summary>One saved unit's picks against the new book, by the first method that works (see the class
    /// summary). Null picks with the reasons when none does.</summary>
    private static (string Method, List<BuilderUnit>? Picks, string? Why) Recover(BookFile book, BuiltArmyFile old,
        UnitFileEntry entry, UnitSolve solved, List<BuilderUnit>? source)
    {
        if (solved.Solved) return ("solved", solved.Selection.ToList(), null);

        string? why = solved.Failure;
        if (source is not null && old.Book is not null)
        {
            List<BuilderUnit>? translated = Translate(old.Book, source, book, out string? failure);
            if (translated != null && UnitErrors(book, translated) is { } errors)
                (translated, failure) = (null, $"translated picks are not legal ({errors})");
            if (translated != null) return ("translated", translated, null);
            why = $"{why}; label translation: {failure}";
        }

        string method = "nearest";
        List<RosterUnit> candidates = NearestCandidates(old, source, entry, book);
        if (candidates.Count == 0
            && Substitutions.TryGetValue((book.Name, EditableSession.NormalizeUnitName(entry.Name)), out string[]? subs))
        {
            candidates = book.Units.Where(u => subs.Contains(u.Name)).ToList();
            method = "SUBSTITUTED";
        }
        if (candidates.Count == 0) return ("UNSOLVED", null, why);

        UnitSolve nearest = SelectionSolver.SolveNearest(book, entry, candidates, c => Closeness(c, entry));
        if (nearest.Solved && UnitErrors(book, nearest.Selection.ToList()) is null)
            return (method, nearest.Selection.ToList(), null);
        return ("UNSOLVED", null, $"{why}; nearest fit: {nearest.Failure ?? "no legal pick-set"}");
    }

    private static BuilderUnit Copy(BuilderUnit u) => new()
    {
        RosterUnitId = u.RosterUnitId, ModelCount = u.ModelCount, Id = u.Id, JoinsUnitId = u.JoinsUnitId,
        CombinedWithId = u.CombinedWithId,
        Choices = u.Choices.Select(c => new UpgradeChoice { SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count }).ToList(),
    };

    private static void ClearDanglingJoins(BuilderList list)
    {
        // A hero whose host unit did not survive would join nothing.
        var ids = list.Units.Select(u => u.Id).Where(id => id != null).ToHashSet();
        foreach (BuilderUnit bu in list.Units)
            if (bu.JoinsUnitId != null && !ids.Contains(bu.JoinsUnitId)) bu.JoinsUnitId = null;
    }

    private static string WeaponDiff(UnitFileEntry saved, UnitFileEntry rebuilt)
    {
        Dictionary<string, int> Count(UnitFileEntry u) => u.Weapons.GroupBy(w => w.Name)
            .ToDictionary(g => g.Key, g => g.Sum(w => Math.Max(1, w.Quantity)));
        Dictionary<string, int> a = Count(saved), b = Count(rebuilt);
        var parts = new List<string>();
        foreach (string name in a.Keys.Union(b.Keys).OrderBy(n => n, StringComparer.Ordinal))
        {
            int delta = b.GetValueOrDefault(name) - a.GetValueOrDefault(name);
            if (delta != 0) parts.Add($"{(delta > 0 ? "+" : "-")}{name} x{Math.Abs(delta)}");
        }
        return string.Join(", ", parts);
    }

    // -- Bracket fit ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Bring the list inside its file-name bracket: at most <paramref name="bracket"/> points and no further below
    /// it than the lobby's Random Army band (<see cref="Rendering.BotArmyPicker.BandPercentUnderLimit"/>), so the
    /// rebuilt army is still a pick for a game of that size. Greedy and recorded: over the bracket, drop upgrade
    /// picks before units (the cut that lands closest under the bracket; if no single cut gets under, the largest
    /// non-hero unit goes); under the band, add upgrade picks before copies of existing non-hero units (the
    /// addition that lands closest to the bracket without passing it). No step may add a catalog Error the list
    /// did not already have. Stops when inside, or when nothing legal fits.
    /// </summary>
    public static void FitBracket(BookFile book, BuilderList list, int bracket, List<string> actions)
    {
        int lower = bracket - bracket * Rendering.BotArmyPicker.BandPercentUnderLimit / 100;
        HashSet<string> baseline = Errors(book, list);

        for (int step = 0; step < 80; step++)
        {
            int total = ListCompiler.Compile(book, list).TotalPoints;
            if (total <= bracket && total >= lower) return;

            bool over = total > bracket;
            var options = new List<(string Label, BuilderList Result, int Rank, int Total)>();
            foreach ((string label, BuilderList result, int rank) in over ? Cuts(book, list) : Additions(book, list))
            {
                if (!Errors(book, result).IsSubsetOf(baseline)) continue;
                options.Add((label, result, rank, ListCompiler.Compile(book, result).TotalPoints));
            }

            (string Label, BuilderList Result, int Rank, int Total)? pick = null;
            if (over)
            {
                var under = options.Where(o => o.Total <= bracket).ToList();
                pick = under.Count > 0
                    ? under.OrderBy(o => o.Total >= lower ? 0 : 1).ThenBy(o => o.Rank).ThenByDescending(o => o.Total).First()
                    : options.Where(o => o.Rank == 1).OrderBy(o => o.Total).Cast<(string, BuilderList, int, int)?>().FirstOrDefault();
            }
            else
            {
                var fits = options.Where(o => o.Total <= bracket).ToList();
                pick = fits.Count == 0 ? null
                    : fits.OrderBy(o => o.Total >= lower ? 0 : 1).ThenBy(o => o.Total >= lower ? o.Rank : 0)
                        .ThenByDescending(o => o.Total).First();
            }

            if (pick is null)
            {
                actions.Add($"stopped at {total} pts: no legal {(over ? "cut" : "addition")} fits the bracket");
                return;
            }
            list.Units = pick.Value.Result.Units;
            ClearDanglingJoins(list);
            actions.Add($"{pick.Value.Label} ({total} -> {pick.Value.Total} pts)");
        }
    }

    private static HashSet<string> Errors(BookFile book, BuilderList list) =>
        ListValidator.Validate(book, list, ListCompiler.Compile(book, list))
            .Where(i => i.Severity == ListIssueSeverity.Error && !i.Message.StartsWith("Over points limit", StringComparison.Ordinal))
            .Select(i => i.Message)
            .ToHashSet();

    private static BuilderList With(BuilderList list, List<BuilderUnit> units) => new()
    {
        Name = list.Name, BookName = list.BookName, PointsLimit = list.PointsLimit, Units = units,
    };

    private static string Describe(BookFile book, BuilderUnit unit) =>
        book.Units.FirstOrDefault(r => r.Id == unit.RosterUnitId)?.Name ?? unit.RosterUnitId;

    private static bool IsHero(BookFile book, BuilderUnit unit) =>
        book.Units.FirstOrDefault(r => r.Id == unit.RosterUnitId)?.Rules
            .Any(r => r.PrintableName is "Hero" or "Unique Hero") == true;

    private static IEnumerable<(string, BuilderList, int)> Cuts(BookFile book, BuilderList list)
    {
        for (int i = 0; i < list.Units.Count; i++)
        {
            BuilderUnit unit = list.Units[i];
            RosterUnit? roster = book.Units.FirstOrDefault(r => r.Id == unit.RosterUnitId);
            for (int c = 0; c < unit.Choices.Count; c++)
            {
                List<BuilderUnit> units = list.Units.Select(Copy).ToList();
                UpgradeChoice choice = units[i].Choices[c];
                string option = roster?.Sections.FirstOrDefault(s => s.Id == choice.SectionId)?.Options
                    .FirstOrDefault(o => o.Id == choice.OptionId)?.Label ?? choice.OptionId;
                if (choice.Count > 1) choice.Count--;
                else units[i].Choices.RemoveAt(c);
                yield return ($"cut upgrade '{option}' from {Describe(book, unit)}", With(list, units), 0);
            }

            // A combined pair's absorbed copy goes with its host; heroes are never cut by the fit.
            if (IsHero(book, unit) || unit.CombinedWithId != null) continue;
            List<BuilderUnit> without = list.Units.Where((u, k) => k != i
                && !(unit.Id != null && u.CombinedWithId == unit.Id)).Select(Copy).ToList();
            yield return ($"cut unit {Describe(book, unit)}", With(list, without), 1);
        }
    }

    private static IEnumerable<(string, BuilderList, int)> Additions(BookFile book, BuilderList list)
    {
        for (int i = 0; i < list.Units.Count; i++)
        {
            BuilderUnit unit = list.Units[i];
            RosterUnit? roster = book.Units.FirstOrDefault(r => r.Id == unit.RosterUnitId);
            if (roster is null) continue;
            foreach (UpgradeSection section in roster.Sections)
            {
                UpgradeChoice? taken = unit.Choices.FirstOrDefault(c => c.SectionId == section.Id);
                foreach (UpgradeOption option in section.Options)
                {
                    if (taken != null && (!section.IsCounted || taken.OptionId != option.Id)) continue;
                    List<BuilderUnit> units = list.Units.Select(Copy).ToList();
                    UpgradeChoice? existing = units[i].Choices.FirstOrDefault(c => c.SectionId == section.Id);
                    if (existing != null) existing.Count++;
                    else units[i].Choices.Add(new UpgradeChoice { SectionId = section.Id, OptionId = option.Id, Count = 1 });
                    yield return ($"add upgrade '{option.Label}' to {roster.Name}", With(list, units), 0);
                }
            }

            if (IsHero(book, unit) || unit.CombinedWithId != null
                || list.Units.Any(u => unit.Id != null && u.CombinedWithId == unit.Id)) continue;
            List<BuilderUnit> plus = list.Units.Select(Copy).ToList();
            BuilderUnit copy = Copy(unit);
            copy.Id = null;
            copy.JoinsUnitId = null;
            plus.Add(copy);
            yield return ($"add a copy of {roster.Name}", With(list, plus), 1);
        }
    }

    /// <summary>Catalog Errors that belong to these units alone (force-org lines, which need the whole list,
    /// are ignored), joined; null when there are none.</summary>
    private static string? UnitErrors(BookFile book, List<BuilderUnit> units)
    {
        var probe = new BuilderList
        {
            BookName = book.Name, PointsLimit = 100_000,
            Units = units.Select(u => new BuilderUnit
            {
                RosterUnitId = u.RosterUnitId, ModelCount = u.ModelCount, Id = u.Id, CombinedWithId = u.CombinedWithId,
                Choices = u.Choices.ToList(),
            }).ToList(),
        };
        List<string> errors = ListValidator.Validate(book, probe, ListCompiler.Compile(book, probe))
            .Where(issue => issue.Severity == ListIssueSeverity.Error && issue.UnitIndex >= 0)
            .Select(issue => issue.Message).ToList();
        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    /// <summary>The roster units a saved unit may be in the new book: the same name, else (a renamed unit) the
    /// unit with the embedded selection's roster id.</summary>
    private static List<RosterUnit> NearestCandidates(BuiltArmyFile old, List<BuilderUnit>? source,
        UnitFileEntry entry, BookFile book)
    {
        string name = EditableSession.NormalizeUnitName(entry.Name);
        List<RosterUnit> byName = book.Units.Where(u => u.Name == name).ToList();
        if (byName.Count > 0) return byName;
        string? rosterId = source?.FirstOrDefault()?.RosterUnitId;
        return rosterId == null ? new List<RosterUnit>() : book.Units.Where(u => u.Id == rosterId).ToList();
    }

    /// <summary>
    /// How close a compiled candidate is to the saved unit (higher is closer) - the judgement behind the nearest
    /// fit. Size dominates (each model off costs more than any weapon can earn back); then each saved weapon copy
    /// is paired greedily with its most similar unused candidate copy (same name scores highest; otherwise word
    /// overlap, the same melee/ranged role, range, attacks, AP and weapon rules), and candidate copies left over
    /// cost a little, so an upgrade nobody bought is not added for free; unit rules shared with the saved unit
    /// add a little and rules it never had subtract a little (so a mount or upgrade the old unit lacked is not
    /// picked to win a tie). Price closeness is the solver's own tie-break.
    /// </summary>
    public static double Closeness(UnitFileEntry candidate, UnitFileEntry saved)
    {
        double score = -2.0 * Math.Abs(candidate.ModelCount - saved.ModelCount);

        static List<WeaponFileEntry> Copies(UnitFileEntry u) =>
            u.Weapons.SelectMany(w => Enumerable.Repeat(w, Math.Clamp(w.Quantity, 1, 40))).ToList();
        List<WeaponFileEntry> pool = Copies(candidate);
        foreach (WeaponFileEntry want in Copies(saved))
        {
            int best = -1;
            double bestSim = double.NegativeInfinity;
            for (int k = 0; k < pool.Count; k++)
            {
                double sim = WeaponSimilarity(want, pool[k]);
                if (sim > bestSim) { bestSim = sim; best = k; }
            }
            if (best < 0) { score -= 0.5; continue; }
            score += bestSim;
            pool.RemoveAt(best);
        }
        score -= 0.25 * pool.Count;

        var savedRules = saved.SpecialRules.Select(r => r.PrintableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateRules = candidate.SpecialRules.Select(r => r.PrintableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        score += 0.3 * candidateRules.Count(savedRules.Contains);
        score -= 0.15 * candidateRules.Count(r => !savedRules.Contains(r));
        return score;
    }

    private static double WeaponSimilarity(WeaponFileEntry a, WeaponFileEntry b)
    {
        double sim = string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            ? 1.0
            : 0.6 * Jaccard(Words(a.Name), Words(b.Name));
        bool aRanged = a.RangeInches > 0f, bRanged = b.RangeInches > 0f;
        sim += aRanged == bRanged ? 0.3 : -0.5;
        if (Math.Abs(a.RangeInches - b.RangeInches) < 0.01f) sim += 0.1;
        if (a.Attacks == b.Attacks) sim += 0.1;
        if (a.ArmorPenetration == b.ArmorPenetration) sim += 0.1;
        sim += 0.3 * Jaccard(a.SpecialRules.Select(r => r.PrintableName).ToHashSet(StringComparer.OrdinalIgnoreCase),
            b.SpecialRules.Select(r => r.PrintableName).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return sim;
    }

    private static HashSet<string> Words(string name) =>
        name.Split(new[] { ' ', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        int both = a.Count(b.Contains);
        return (double)both / (a.Count + b.Count - both);
    }

    /// <summary>The file's embedded selections, grouped per playable unit: a unit's own BuilderUnit plus any
    /// copy merged into it (#107). Null for a unit whose selections cannot be lined up by name - an Army Forge
    /// import's playable half is THEIR data, so the two halves are not guaranteed to align.</summary>
    private static List<List<BuilderUnit>?> AlignSelections(BuiltArmyFile old)
    {
        var result = new List<List<BuilderUnit>?>();
        for (int i = 0; i < old.Units.Count; i++) result.Add(null);
        if (old.Selections is null || old.Book is null) return result;

        List<BuilderUnit> hosts = old.Selections.Units
            .Where(bu => string.IsNullOrEmpty(bu.CombinedWithId) || bu.CombinedWithId == bu.Id)
            .ToList();
        if (hosts.Count != old.Units.Count) return result;

        for (int i = 0; i < hosts.Count; i++)
        {
            BuilderUnit host = hosts[i];
            RosterUnit? roster = old.Book.Units.FirstOrDefault(r => r.Id == host.RosterUnitId);
            if (roster is null || roster.Name != EditableSession.NormalizeUnitName(old.Units[i].Name)) continue;

            var group = new List<BuilderUnit> { host };
            if (host.Id != null)
                group.AddRange(old.Selections.Units.Where(bu => bu != host && bu.CombinedWithId == host.Id));
            result[i] = group;
        }
        return result;
    }

    /// <summary>Map one unit's picks (host + merged copies) from <paramref name="oldBook"/> onto
    /// <paramref name="newBook"/> by section and option labels. Null with a reason when any pick has no
    /// counterpart.</summary>
    public static List<BuilderUnit>? Translate(BookFile oldBook, IReadOnlyList<BuilderUnit> source, BookFile newBook,
        out string? failure)
    {
        failure = null;
        var result = new List<BuilderUnit>();
        foreach (BuilderUnit bu in source)
        {
            RosterUnit? oldRoster = oldBook.Units.FirstOrDefault(r => r.Id == bu.RosterUnitId);
            if (oldRoster is null)
            {
                failure = $"roster unit '{bu.RosterUnitId}' missing from the embedded book";
                return null;
            }

            RosterUnit? newRoster = newBook.Units.FirstOrDefault(r => r.Id == oldRoster.Id && r.Name == oldRoster.Name)
                ?? newBook.Units.FirstOrDefault(r => r.Name == oldRoster.Name);
            if (newRoster is null)
            {
                failure = $"no unit named '{oldRoster.Name}' in book '{newBook.Name}'";
                return null;
            }

            var copy = new BuilderUnit
            {
                RosterUnitId = newRoster.Id,
                ModelCount = newRoster.BaseModelCount,
                Id = bu.Id,
                JoinsUnitId = bu.JoinsUnitId,
                CombinedWithId = bu.CombinedWithId,
            };

            foreach (UpgradeChoice choice in bu.Choices)
            {
                UpgradeSection? oldSection = oldRoster.Sections.FirstOrDefault(s => s.Id == choice.SectionId);
                UpgradeOption? oldOption = oldSection?.Options.FirstOrDefault(o => o.Id == choice.OptionId);
                if (oldSection is null || oldOption is null)
                {
                    failure = $"pick {choice.SectionId}/{choice.OptionId} missing from the embedded book";
                    return null;
                }

                (UpgradeSection Section, UpgradeOption Option)? mapped = MapPick(newRoster, oldSection, oldOption);
                if (mapped is null)
                {
                    failure = $"'{oldSection.Label}: {oldOption.Label}' has no counterpart on {newRoster.Name}";
                    return null;
                }
                copy.Choices.Add(new UpgradeChoice
                {
                    SectionId = mapped.Value.Section.Id, OptionId = mapped.Value.Option.Id, Count = choice.Count,
                });
            }
            result.Add(copy);
        }
        return result;
    }

    private static (UpgradeSection, UpgradeOption)? MapPick(RosterUnit roster, UpgradeSection oldSection,
        UpgradeOption oldOption)
    {
        // Same-label sections first; a reworded header falls back to any section with the option.
        IEnumerable<UpgradeSection> sections = roster.Sections.Where(s => SameText(s.Label, oldSection.Label))
            .Concat(roster.Sections.Where(s => !SameText(s.Label, oldSection.Label) && s.Variant == oldSection.Variant));

        foreach (Func<UpgradeOption, bool> match in new Func<UpgradeOption, bool>[]
                 {
                     o => SameText(o.Label, oldOption.Label),
                     o => SameText(OptionName(o.Label), OptionName(oldOption.Label)),
                     o => SameGains(o, oldOption),
                 })
        {
            foreach (UpgradeSection section in sections)
            {
                UpgradeOption? option = section.Options.FirstOrDefault(match);
                if (option != null) return (section, option);
            }
        }
        return null;
    }

    private static bool SameText(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // "Elite EMP Pistol (9", A3, Rending)" -> "Elite EMP Pistol": the pick, without the profile 3.6 may reprint.
    private static string OptionName(string label)
    {
        int paren = label.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? label[..paren] : label;
    }

    private static bool SameGains(UpgradeOption a, UpgradeOption b)
    {
        string Sig(UpgradeOption o) => string.Join("|",
            o.WeaponsGained.Select(w => $"w:{w.Name}x{w.Quantity}")
                .Concat(o.ItemsGained.Select(i => $"i:{i.Name}x{i.Quantity}"))
                .Concat(o.RulesGained.Select(r => $"r:{r.PrintableName}"))
                .Append($"m:{o.ModelsGained}")
                .OrderBy(s => s, StringComparer.Ordinal));
        string sa = Sig(a);
        return sa != "m:0" && sa == Sig(b);
    }

    /// <summary>One report as console lines.</summary>
    public static IEnumerable<string> Format(Report r)
    {
        yield return $"== {r.FileName}  [{r.BookName ?? "?"}: {r.OldVersion ?? "no book"} -> {r.NewVersion ?? "?"}]"
            + (r.Recompiled ? " (recompiled from its own selections)" : "");
        if (r.Error != null)
        {
            yield return $"   ERROR: {r.Error}";
            yield break;
        }
        foreach (UnitLine u in r.Units)
        {
            if (u.Failure != null)
            {
                yield return $"   UNSOLVED    {u.OldName} ({u.OldModels} models, {u.OldPoints} pts): {u.Failure}";
                continue;
            }
            string renamed = u.NewName != null && u.NewName != EditableSession.NormalizeUnitName(u.OldName)
                ? $" -> {u.NewName}" : "";
            string models = u.Method == "recompiled" || u.OldModels == u.NewModels ? "" : $" [models {u.OldModels} -> {u.NewModels}]";
            yield return $"   {u.Method,-11} {u.OldName}{renamed}: {u.OldPoints} -> {u.NewPoints}{models}"
                + (u.Diff.Length > 0 ? $"  {{{u.Diff}}}" : "");
        }
        foreach (string action in r.FitActions) yield return $"   fit: {action}";
        string bracket = r.Bracket is int b
            ? $" / bracket {b}{(r.NewTotal > b ? " OVER" : $" ({b - r.NewTotal} under)")}"
            : "";
        yield return $"   points: {r.OldTotal} -> {r.RecoveredTotal} recovered -> {r.NewTotal} final{bracket}; "
            + $"{r.Units.Count - r.Unsolved}/{r.Units.Count} units recovered";
        foreach (string issue in r.Issues) yield return $"   {issue}";
    }
}
