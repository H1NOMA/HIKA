using Hika.Catalog;
using Hika.Config;
using Hika.Learning;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Обучение — единственная часть программы, которая меняет своё поведение
/// сама. Именно поэтому её нужно проверять строже остального: сломанное
/// обучение не падает и не жалуется, оно просто медленно портит узнавание
/// команд, и заметить это можно будет только по ощущению «раньше работало лучше».
/// </summary>
public class AdaptationTests
{
    [Fact]
    public void СловаИзУспешныхКомандВажнееЧастых()
    {
        var profile = new UserProfile();

        // Слово-пустышка звучало много раз, но ни к чему не привело.
        for (int i = 0; i < 10; i++) Adaptation.Observe(profile, "погода сегодня", useful: false);

        // А это прозвучало дважды — и оба раза что-то запустило.
        Adaptation.Observe(profile, "халдайверс", useful: true);
        Adaptation.Observe(profile, "халдайверс", useful: true);

        var terms = Adaptation.PromptTerms(profile, 10);

        Assert.Equal("халдайверс", terms[0]);
    }

    [Fact]
    public void СлужебныеСловаВСловарьНеПопадают()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 20; i++) Adaptation.Observe(profile, "открой мне пожалуйста фотошоп", useful: true);

        var terms = Adaptation.PromptTerms(profile, 20);

        Assert.Contains("фотошоп", terms);
        Assert.DoesNotContain("открой", terms);
        Assert.DoesNotContain("пожалуйста", terms);
    }

    [Fact]
    public void СловарьНеРастётБезГраниц()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 200; i++) Adaptation.Observe(profile, $"слово{i} слово{i}", useful: true);

        Assert.Equal(12, Adaptation.PromptTerms(profile, 12).Count);
    }

    [Fact]
    public void НаписаниеИмениПринимаетсяПослеПовторов()
    {
        var profile = new UserProfile();

        Assert.Null(Adaptation.ObserveWakeVariant(profile, "фика", 0.6, threshold: 3));
        Assert.Null(Adaptation.ObserveWakeVariant(profile, "фика", 0.6, threshold: 3));

        // Третий раз — принимаем.
        Assert.Equal("фика", Adaptation.ObserveWakeVariant(profile, "фика", 0.6, threshold: 3));
    }

    [Theory]
    // Совсем непохожее не запоминаем никогда: иначе в имена уедет
    // любое слово, сказанное в тишине.
    [InlineData(0.1)]
    // Точное совпадение запоминать незачем — оно и так работает.
    [InlineData(0.99)]
    public void КрайностиВНаписанияИмениНеПопадают(double score)
    {
        var profile = new UserProfile();

        for (int i = 0; i < 5; i++)
            Assert.Null(Adaptation.ObserveWakeVariant(profile, "слово", score, threshold: 3));
    }

    [Fact]
    public void СинонимУчитсяТолькоИзПохожейПары()
    {
        var profile = new UserProfile();

        // «Халдайверс два» и «хеллдайверс» — явно одно и то же.
        Assert.True(Adaptation.LearnAlias(profile, "халдайверс два", "хеллдайверс", "steam:hd2", "Helldivers 2"));

        // А вот это человек просто сделал следом, и связывать их нельзя.
        Assert.False(Adaptation.LearnAlias(profile, "погода в москве", "фотошоп", "adobe:ps", "Photoshop"));

        Assert.Single(profile.Aliases);
        Assert.Equal("steam:hd2", profile.Aliases["халдайверс два"].EntryId);
    }

    [Fact]
    public void РучнойСинонимНеПерезаписываетсяСамообучением()
    {
        var profile = new UserProfile();
        profile.Aliases["игра"] = new AliasStat { EntryId = "my:game", EntryName = "Моя игра", Manual = true };

        Adaptation.LearnAlias(profile, "игра", "игра другая", "other:thing", "Другое");

        Assert.Equal("my:game", profile.Aliases["игра"].EntryId);
    }

    [Fact]
    public void ПрибавкаЗаЗапускиМалаИСПотолком()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 500; i++) Adaptation.RememberLaunch(profile, "steam");

        var boost = Adaptation.LaunchBoost(profile, "steam", max: 0.10);

        Assert.True(boost <= 0.10, $"прибавка {boost:F3} перебила бы само сходство");
        Assert.True(boost > 0.05, $"прибавка {boost:F3} слишком мала, чтобы что-то решать");
        Assert.Equal(0, Adaptation.LaunchBoost(profile, "неизвестное", max: 0.10));
    }
}

public class CatalogLearningTests
{
    private sealed class Prior : IEntryPrior
    {
        public Dictionary<string, string> Aliases { get; } = new();
        public Dictionary<string, double> Boosts { get; } = new();

        public string? AliasTarget(string phrase) => Aliases.GetValueOrDefault(phrase);
        public double Boost(string entryId) => Boosts.GetValueOrDefault(entryId);
    }

    private static AppCatalog Build(params (string Id, string Name)[] entries)
    {
        var catalog = new AppCatalog();
        catalog.SetInstalled(entries
            .Select(e => CatalogEntry.Create(e.Id, EntryKind.Installed, e.Id, new[] { e.Name }))
            .ToList());
        return catalog;
    }

    [Fact]
    public void ВыученныйСинонимНаходитсяДажеБезСходства()
    {
        var catalog = Build(("steam:hd2", "Helldivers 2"));

        // Без обучения такая фраза не нашла бы ничего: общих букв нет вовсе.
        Assert.Null(catalog.Resolve("ад ныряльщики", 0.62));

        var prior = new Prior();
        prior.Aliases["ад ныряльщики"] = "steam:hd2";
        catalog.Prior = prior;

        var match = catalog.Resolve("ад ныряльщики", 0.62);

        Assert.NotNull(match);
        Assert.Equal("steam:hd2", match!.Entry.Id);
    }

    [Fact]
    public void ЧастоЗапускаемоеВыигрываетСпорПриРавномСходстве()
    {
        var catalog = Build(("a:steam", "стим"), ("b:steam", "стим"));

        var prior = new Prior();
        prior.Boosts["b:steam"] = 0.08;
        catalog.Prior = prior;

        Assert.Equal("b:steam", catalog.Resolve("стим", 0.62)!.Entry.Id);
    }

    [Fact]
    public void ПрибавкаНеВытаскиваетНепохожее()
    {
        var catalog = Build(("a:word", "ворд"), ("b:junk", "совершенно другое приложение"));

        var prior = new Prior();
        prior.Boosts["b:junk"] = 0.10;
        catalog.Prior = prior;

        Assert.Equal("a:word", catalog.Resolve("ворд", 0.62)!.Entry.Id);
    }
}

public class ProfileStoreTests
{
    [Fact]
    public void ПрофильПереживаетПерезапуск()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hika-profile-{Guid.NewGuid():N}.json");

        try
        {
            using (var store = new ProfileStore(path))
            {
                store.Load();
                Adaptation.Observe(store.Profile, "халдайверс", useful: true);
                Adaptation.RememberLaunch(store.Profile, "steam:hd2");
                store.Touch();
                store.Flush();
            }

            using var reopened = new ProfileStore(path);
            var profile = reopened.Load();

            Assert.Contains("халдайверс", profile.Terms.Keys);
            Assert.Equal(1, profile.Launches["steam:hd2"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void БитыйПрофильНеМешаетЗапуску()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hika-profile-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ это не json");

        try
        {
            using var store = new ProfileStore(path);
            var profile = store.Load();

            Assert.Equal(0, profile.Utterances);
            Assert.True(File.Exists(path + ".broken"), "испорченный файл должен сохраниться рядом");
        }
        finally
        {
            foreach (var f in new[] { path, path + ".broken" })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}

public class LearningEngineTests
{
    private static LearningEngine Fresh(out string profilePath, out string journalPath)
    {
        profilePath = Path.Combine(Path.GetTempPath(), $"hika-p-{Guid.NewGuid():N}.json");
        journalPath = Path.Combine(Path.GetTempPath(), $"hika-j-{Guid.NewGuid():N}.jsonl");

        var engine = new LearningEngine(
            new LearningConfig { WakeVariantThreshold = 2 },
            new ProfileStore(profilePath),
            new SpeechJournal(journalPath));

        engine.Start();
        return engine;
    }

    [Fact]
    public void НеудачаЗатемУдачаДаютСиноним()
    {
        var engine = Fresh(out var p, out var j);

        try
        {
            engine.Observe(new JournalEntry { Text = "ави запусти халдайверс два", Success = false },
                "запусти халдайверс два");

            engine.Observe(new JournalEntry
            {
                Text = "ави запусти хеллдайверс",
                Success = true,
                EntryId = "steam:hd2",
                Intent = "Helldivers 2",
            }, "запусти хеллдайверс");

            Assert.Equal("steam:hd2", engine.AliasTarget("запусти халдайверс два"));
        }
        finally
        {
            engine.Dispose();
            Cleanup(p, j);
        }
    }

    [Fact]
    public void ПовторТойЖеФразыСинонимомНеСтановится()
    {
        var engine = Fresh(out var p, out var j);

        try
        {
            engine.Observe(new JournalEntry { Text = "ави открой стим", Success = false }, "открой стим");
            engine.Observe(new JournalEntry
            {
                Text = "ави открой стим",
                Success = true,
                EntryId = "steam",
            }, "открой стим");

            Assert.Empty(engine.Profile.Aliases);
        }
        finally
        {
            engine.Dispose();
            Cleanup(p, j);
        }
    }

    [Fact]
    public void ВыключенноеОбучениеНичегоНеЗапоминает()
    {
        var profilePath = Path.Combine(Path.GetTempPath(), $"hika-p-{Guid.NewGuid():N}.json");
        var journalPath = Path.Combine(Path.GetTempPath(), $"hika-j-{Guid.NewGuid():N}.jsonl");

        var engine = new LearningEngine(
            new LearningConfig { Enabled = false },
            new ProfileStore(profilePath),
            new SpeechJournal(journalPath));

        engine.Start();

        try
        {
            engine.Observe(new JournalEntry { Text = "открой стим", Success = true, EntryId = "steam" }, "открой стим");

            Assert.Equal(0, engine.Profile.Utterances);
            Assert.Empty(engine.Vocabulary());
            Assert.Equal(0, engine.Boost("steam"));
        }
        finally
        {
            engine.Dispose();
            Cleanup(profilePath, journalPath);
        }
    }

    [Fact]
    public void ДневникРечиПозволяетСобратьПрофильЗаново()
    {
        var engine = Fresh(out var p, out var j);

        try
        {
            for (int i = 0; i < 3; i++)
            {
                engine.Observe(new JournalEntry
                {
                    Text = "ави открой фотошоп",
                    Success = true,
                    EntryId = "adobe:ps",
                }, "открой фотошоп");
            }

            engine.Forget();
            Assert.Equal(0, engine.Profile.Utterances);

            engine.RebuildFromJournal();

            Assert.Equal(3, engine.Profile.Utterances);
            Assert.Equal(3, engine.Profile.Launches["adobe:ps"]);
        }
        finally
        {
            engine.Dispose();
            Cleanup(p, j);
        }
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var path in paths)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
