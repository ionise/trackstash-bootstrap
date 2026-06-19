using Microsoft.Data.Sqlite;
using TrackStash.Bootstrap;

namespace TrackStash.Bootstrap.Tests;

public sealed class ArtistSeedIntegrationTests
{
    [Fact]
    public async Task SeedArtistAsync_ReusesCanonicalRow_ForPunctuationVariants()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trackstash-bootstrap-artist-{Guid.NewGuid():N}.db");
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);

            var first = await commands.SeedArtistAsync(new SeedArtistRequest(
                DatabasePath: dbPath,
                Name: "Bozra Bozra"));

            var second = await commands.SeedArtistAsync(new SeedArtistRequest(
                DatabasePath: dbPath,
                Name: "BozraBozra"));

            Assert.Equal(first.ArtistId, second.ArtistId);
            Assert.Equal(SeedArtistAction.ReusedByNormalizedName, second.Action);

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM artist"));
            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM artist_alias"));
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
