using System.Text.Json;
using FDG;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Import;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #440 - the one backward-compatibility promise of the OPR 3.6 upgrade (plan section 1 rule 6): an army saved
// under 3.5 keeps loading and playing. The fixtures are six ORIGINAL 3.5.3 catalog lists (Fixtures/legacy-3.5,
// moved out of armies/ before #440 rebuilt the catalog against the 3.6 books): two renamed-book families
// (Wormhole Daemons of Change, Saurian Starhost), deleted catalog names (Crack, Resistance, Piercing Assault -
// #436), an AoF list, a spell granting the deleted "+6\" range when shooting" helper (#439 D8), and a plain list.
// Each loads into a GameDataStore, plays a full seeded AI game, and the #168 drop diagnostic names exactly the
// references 3.6 no longer resolves - no more (a resolvable name dropped would be a regression), no fewer.
[TestFixture]
[NonParallelizable] // RuleDiagnostics and CurrentRulebook are process-wide
public class Legacy35ArmyTests
{
    private static string FixturePath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-3.5", file);

    private static BuiltArmyFile Load(string file) =>
        JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(FixturePath(file)), RuleJson.Options)!;

    /// <summary>Fixture -> the rule names the #168 diagnostic must drop, sorted (ordinal). A drop is a name the
    /// list still uses that neither its embedded definitions nor the 3.6 catalog define any more: the deleted 3.5
    /// catalog rules (#436 - Resistance, Piercing Assault; a 3.5 file never embeds catalog rules, only book ones,
    /// which is why "Crack", which these books defined themselves, still resolves from the embedded copy) and the
    /// catalog helper "+6\" range when shooting" a 3.5 Battle Rune grants (#439 D8). The renamed-family lists drop
    /// nothing: their embedded book definitions cover every name they use.</summary>
    private static readonly Dictionary<string, string[]> ExpectedDrops = new()
    {
        ["Wormhole Demons of Change 1k.fdgarmy"] = Array.Empty<string>(),
        ["3k - Saurian Starhost.fdgarmy"] = Array.Empty<string>(),
        ["2k - Orks - Horde Mixed.fdgarmy"] = new[] { "Piercing Assault", "Resistance" },
        ["1k - Ogres.fdgarmy"] = new[] { "Resistance" },
        ["2k - Dwarf Guilds - Ambush and Scout-Heavy.fdgarmy"] = new[] { "+6\" range when shooting" },
        ["2k - Robot Legions - Mixed.fdgarmy"] = Array.Empty<string>(),
    };

    public static IEnumerable<string> Fixtures => ExpectedDrops.Keys;

    [SetUp]
    public void InstallRulebook() => BundledBookRulebook.Install();

    [TearDown]
    public void ClearRulebook() => CurrentRulebook.Installed = null;

    [Test]
    public void EveryFixtureIsA35ForgeList()
    {
        foreach (string file in Fixtures)
        {
            BuiltArmyFile army = Load(file);
            Assert.That(army.Book?.Version, Is.EqualTo("OPR v3.5.3"), file);
            Assert.That(LegacyArmyNotice.IsLegacy(LegacyArmyNotice.RulesVersionOf(army)), Is.True, file);

            // #438 D8: the catalog validator no longer knows 3.5 "Unique", so reopening an old list in the Forge
            // (validated against its OWN embedded 3.5 book) raises no Unique error, duplicate or not.
            IReadOnlyList<ListIssue> issues = ListValidator.Validate(army.Book!, army.Selections!, army);
            Assert.That(issues.Where(i => i.Message.Contains("Unique")), Is.Empty, file);
        }
    }

    [TestCaseSource(nameof(Fixtures))]
    [CancelAfter(240_000)]
    public async Task A35List_LoadsPlaysToCompletion_AndDropsExactlyTheNames36NoLongerResolves(string file)
    {
        BuiltArmyFile army = Load(file);
        var drops = new List<RuleDrop>();
        void Collect(RuleDrop d) { lock (drops) drops.Add(d); }
        void Swallow(string _) { }
        RuleDiagnostics.OnRuleDropped += Collect;
        RuleDiagnostics.OnWarning += Swallow;
        try
        {
            GameResult result = await PlaySeededGame(army, seed: 4401);
            Assert.That(result.Outcome, Is.EqualTo(EGameOutcome.Win).Or.EqualTo(EGameOutcome.Tie), result.ToSummaryLine());
            Assert.That(result.RoundsPlayed, Is.EqualTo(4), "a full game, not one cut short");
        }
        finally
        {
            RuleDiagnostics.OnRuleDropped -= Collect;
            RuleDiagnostics.OnWarning -= Swallow;
        }

        string[] launched = drops.Select(d => d.RuleName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] audited = ArmyRuleAudit.Audit(army).Drops.Select(d => d.RuleName).Distinct()
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(launched, Is.EqualTo(ExpectedDrops[file]), $"{file}: launch drops [{string.Join(", ", launched)}]");
            Assert.That(audited, Is.EqualTo(ExpectedDrops[file]), $"{file}: the builder's #168 audit agrees with the launch");
        });
    }

    /// <summary>Plays a fresh two-slot game (setup, deployment, four rounds) with the legacy list on both sides,
    /// solo-rules AI, seeded probabilistic dice - the DeterminismTests shape over the app's public bus.</summary>
    private static async Task<GameResult> PlaySeededGame(ArmyListFile army, int seed)
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bus = new LocalMessageBus();
        var slots = new PlayerSlot[2];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
            var aiGame = new FDGGame_AsLocal(store, bus);
            slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
        }

        GameSettings settings = GameSettings.GetDefault();
        settings.RandomnessType = ERandomnessType.Probabilistic;
        settings.DiceSeed = seed;

        var completed = new TaskCompletionSource<GameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FDGServer(store, bus, settings, slots);
        server.OnGameCompleted += r => completed.TrySetResult(r);

        Task first = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(200)));
        Assert.That(first, Is.SameAs(completed.Task), "the legacy game must play to completion without hanging");
        return await completed.Task;
    }

    // -- Renamed books (#437): the rulebook backfill still finds the 3.6 book for every renamed family. --------

    [TestCase("Wormhole Demons of Change 1k.fdgarmy", "Daemons of Change")]
    [TestCase("3k - Saurian Starhost.fdgarmy", "Saurian Starhosts")]
    public void ARenamedFamilyFixture_StillFindsIts36Book(string file, string bookName)
    {
        BuiltArmyFile army = Load(file);
        Assume.That(army.Faction, Is.Not.EqualTo(bookName), "the fixture carries the 3.5 name");

        string book36 = BundledBookFor(army.Faction, army.GameSystem);
        Assert.That(book36, Is.EqualTo(bookName));
        Assert.That(CurrentRulebook.DefinitionsForFaction(army.Faction, army.GameSystem), Is.Not.Empty,
            "the backfill serves the renamed book's definitions to the old faction name");
    }

    // The AoF family has no shipped 3.5 list to keep, so its case is the faction string alone.
    [Test]
    public void TheAofRenamedFamily_StillFindsIts36Book()
    {
        Assert.That(BundledBookFor("Rift Daemons of Change", GameSystems.AgeOfFantasy), Is.EqualTo("Daemons of Change"));
        Assert.That(CurrentRulebook.DefinitionsForFaction("Rift Daemons of Change", GameSystems.AgeOfFantasy),
            Is.Not.Empty);
    }

    private static string BundledBookFor(string faction, string? system)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        foreach (string path in Directory.EnumerateFiles(dir, "*" + BookFile.EXTENSION_WITH_PERIOD).OrderBy(p => p, StringComparer.Ordinal))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            if (OprBookNames.Matches(book, faction, system)) return book.Name;
        }
        return "(none)";
    }

    // -- Plan Q5: the amber notice, in the lobby and on the CLI. ------------------------------------------------

    [Test]
    public void TheLobbyShowsOneAmberLinePerLegacyArmy_AndNoneForA36One()
    {
        BuiltArmyFile legacy = Load("1k - Ogres.fdgarmy");
        var players = new[]
        {
            Slot(new ArmyListSummary(true, legacy.Name, legacy.Faction, legacy.TotalPoints, legacy.GameSystem,
                LegacyArmyNotice.RulesVersionOf(legacy))),
            Slot(new ArmyListSummary(true, "Fresh", "Ogres", 1000, GameSystems.AgeOfFantasy, "OPR v3.6.0")),
            Slot(new ArmyListSummary(true, "Hand-made", "Test", 500)),
            Slot(new ArmyListSummary(false, "N/A", "N/A", 0)),
        };

        Assert.That(LobbyScreen.LegacyArmyNotices(players),
            Is.EqualTo(new[] { $"Army '{legacy.Name}' was built for OPR v3.5.3; it plays with 3.6 rules" }));
    }

    [Test]
    public void TheCliPrintsTheSameLine_WhenALegacyListIsLoaded()
    {
        TextReader inBefore = Console.In;
        TextWriter outBefore = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetIn(new StringReader($"1\n{FixturePath("2k - Robot Legions - Mixed.fdgarmy")}\n"));
            Console.SetOut(captured);
            ArmyListFile army = ArmyLoader.PromptForArmy("Player 1");
            Assert.That(captured.ToString(),
                Does.Contain($"Army '{army.Name}' was built for OPR v3.5.3; it plays with 3.6 rules"));
        }
        finally
        {
            Console.SetIn(inBefore);
            Console.SetOut(outBefore);
        }
    }

    private static LobbyPlayerInfoSummary Slot(ArmyListSummary summary) =>
        new("player", summary, ETeamOption.Team1, EPlayerType.Local, new ConnectionID(Guid.NewGuid()),
            new PlayerID(Guid.NewGuid()));
}
