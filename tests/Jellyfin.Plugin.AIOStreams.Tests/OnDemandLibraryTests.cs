using Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class OnDemandLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyio-tests-" + Guid.NewGuid().ToString("N"));

    public OnDemandLibraryTests()
    {
        StreamFolder.Create(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteMovieAsync_WritesStrmAndNfo()
    {
        var result = await OnDemandLibrary.WriteMovieAsync(_root, "Dune", "2021", "tt1160419", "https://media.example/play?x=1", CancellationToken.None);

        var strmPath = Path.Combine(_root, "movies", "Dune (2021)", "Dune (2021).strm");
        Assert.True(File.Exists(strmPath));
        Assert.Equal("https://media.example/play?x=1", await File.ReadAllTextAsync(strmPath));

        var nfoPath = Path.Combine(_root, "movies", "Dune (2021)", "movie.nfo");
        var nfo = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<uniqueid type=\"imdb\">tt1160419</uniqueid>", nfo);

        Assert.Equal(1, result.Strms);
        Assert.Equal(2, result.Files);
    }

    [Fact]
    public async Task WriteShowAsync_WritesEpisodesPerSeason()
    {
        var result = await OnDemandLibrary.WriteShowAsync(_root, "Dune", "2021", "tt1160419", new[]
        {
            new EpisodeEntry(1, 1, "https://media.example/e1"),
            new EpisodeEntry(1, 2, "https://media.example/e2"),
            new EpisodeEntry(2, 1, "https://media.example/e3")
        }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "tv", "Dune (2021)", "tvshow.nfo")));
        Assert.Equal("https://media.example/e1", await File.ReadAllTextAsync(Path.Combine(_root, "tv", "Dune (2021)", "Season 01", "S01E01.strm")));
        Assert.True(File.Exists(Path.Combine(_root, "tv", "Dune (2021)", "Season 02", "S02E01.strm")));
        Assert.Equal(3, result.Strms);
        Assert.Equal(4, result.Files);
    }

    [Fact]
    public async Task RemoveTitle_DeletesFolder()
    {
        await OnDemandLibrary.WriteMovieAsync(_root, "Dune", "2021", "tt1160419", "https://media.example/play", CancellationToken.None);
        var removed = OnDemandLibrary.RemoveTitle(_root, "movie", "Dune", "2021");
        Assert.True(removed);
        Assert.False(Directory.Exists(Path.Combine(_root, "movies", "Dune (2021)")));

        var removedAgain = OnDemandLibrary.RemoveTitle(_root, "movie", "Dune", "2021");
        Assert.False(removedAgain);
    }

    [Fact]
    public async Task List_ListsMoviesAndSeries()
    {
        await OnDemandLibrary.WriteMovieAsync(_root, "Dune", "2021", "tt1160419", "https://media.example/play", CancellationToken.None);
        await OnDemandLibrary.WriteShowAsync(_root, "Severance", "2022", "tt11280740", new[] { new EpisodeEntry(1, 1, "https://media.example/e1") }, CancellationToken.None);

        var titles = OnDemandLibrary.List(_root);
        Assert.Equal(2, titles.Count);
        var movie = titles.Single(t => t.Type == "movie");
        var series = titles.Single(t => t.Type == "series");
        Assert.Equal("Dune", movie.Name);
        Assert.Equal("2021", movie.Year);
        Assert.Equal("Severance", series.Name);
        Assert.Equal("2022", series.Year);
    }

    [Fact]
    public void List_WithoutYear_StillParses()
    {
        Directory.CreateDirectory(Path.Combine(_root, "movies", "Untitled"));
        var titles = OnDemandLibrary.List(_root);
        Assert.Equal("Untitled", Assert.Single(titles).Name);
        Assert.Null(Assert.Single(titles).Year);
    }
}
