using Microsoft.Data.Sqlite;
using TrackStash.Bootstrap;

namespace TrackStash.Bootstrap.Tests;

public sealed class ReleaseSeedIntegrationTests
{
    [Fact]
    public async Task SeedReleaseAsync_ReusesCanonicalRow_AndPersistsLabelAndArtistLinks()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trackstash-bootstrap-release-{Guid.NewGuid():N}.db");
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);

            var label = await commands.SeedLabelAsync(new SeedLabelRequest(
                DatabasePath: dbPath,
                Name: "Virelith Records"));

            var artist = await commands.SeedArtistAsync(new SeedArtistRequest(
                DatabasePath: dbPath,
                Name: "Virelith"));

            var first = await commands.SeedReleaseAsync(new SeedReleaseRequest(
                DatabasePath: dbPath,
                Title: "Virelith Sessions",
                LabelId: label.LabelId,
                ArtistId: artist.ArtistId));

            var second = await commands.SeedReleaseAsync(new SeedReleaseRequest(
                DatabasePath: dbPath,
                Title: "Virelith Sessions",
                LabelId: label.LabelId,
                ArtistId: artist.ArtistId));

            Assert.Equal(first.ReleaseId, second.ReleaseId);
            Assert.Equal(SeedReleaseAction.ReusedByNormalizedTitleAndLabel, second.Action);

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM release"));
            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM release_label_link"));
            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM release_artist_credit"));
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
