using Xunit;

using Jellyfin.Plugin.AIOStreams.Services;

namespace Jellyfin.Plugin.AIOStreams.Tests;

public class StreamFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyio-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Validate_Missing_ReturnsMissing()
    {
        Assert.Equal(FolderState.Missing, StreamFolder.Validate(_root));
    }

    [Fact]
    public void Validate_File_ReturnsNotDirectory()
    {
        File.WriteAllText(_root, "x");
        Assert.Equal(FolderState.NotDirectory, StreamFolder.Validate(_root));
    }

    [Fact]
    public void Validate_EmptyDirectory_ReturnsOk()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(FolderState.Ok, StreamFolder.Validate(_root));
    }

    [Fact]
    public void Create_CreatesRootAndCategories()
    {
        StreamFolder.Create(_root);
        Assert.True(Directory.Exists(Path.Combine(_root, StreamFolder.MoviesDirName)));
        Assert.True(Directory.Exists(Path.Combine(_root, StreamFolder.TvDirName)));
        Assert.Equal(FolderState.Ok, StreamFolder.Validate(_root));
    }

    [Theory]
    [InlineData("Dune", null, "Dune")]
    [InlineData("Dune", "2021", "Dune (2021)")]
    [InlineData("  My  :  Title?  ", null, "My Title")]
    public void BuildFolderName_FormatsCorrectly(string title, string? year, string expected)
    {
        Assert.Equal(expected, StreamFolder.BuildFolderName(title, year));
    }

    [Fact]
    public void MovieDir_UsesTrashLayout()
    {
        var dir = StreamFolder.MovieDir(_root, "Dune", "2021");
        Assert.Equal(Path.Combine(_root, "movies", "Dune (2021)"), dir);
    }

    [Fact]
    public void TvShowDir_UsesTrashLayout()
    {
        var dir = StreamFolder.TvShowDir(_root, "Dune", "2021");
        Assert.Equal(Path.Combine(_root, "tv", "Dune (2021)"), dir);
    }

    [Theory]
    [InlineData(1, 2, "S01E02.strm")]
    [InlineData(10, 5, "S10E05.strm")]
    public void EpisodeFileName_FormatsCorrectly(int season, int episode, string expected)
    {
        Assert.Equal(expected, StreamFolder.EpisodeFileName(season, episode));
    }

    [Theory]
    [InlineData("2021", "2021")]
    [InlineData("2021-05-15", "2021")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractYear_Parses(string? input, string? expected)
    {
        Assert.Equal(expected, StreamFolder.ExtractYear(input));
    }

    [Theory]
    [InlineData("tt1234567", "tt1234567")]
    [InlineData("kitsu:123", null)]
    [InlineData(null, null)]
    public void ExtractImdbId_Parses(string? input, string? expected)
    {
        Assert.Equal(expected, StreamFolder.ExtractImdbId(input));
    }
}
