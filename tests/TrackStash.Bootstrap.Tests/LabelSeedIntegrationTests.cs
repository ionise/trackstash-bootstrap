using Microsoft.Data.Sqlite;
using TrackStash.Bootstrap;

namespace TrackStash.Bootstrap.Tests;

public sealed class LabelSeedIntegrationTests
{
    [Fact]
    public async Task SeedLabelAsync_ReusesCanonicalRow_ForPunctuationVariants()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trackstash-bootstrap-{Guid.NewGuid():N}.db");
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);

            var first = await commands.SeedLabelAsync(new SeedLabelRequest(
                DatabasePath: dbPath,
                Name: "Distynqive Records"));

            var second = await commands.SeedLabelAsync(new SeedLabelRequest(
                DatabasePath: dbPath,
                Name: "Distyn'qive Records"));

            Assert.Equal(first.LabelId, second.LabelId);
            Assert.Equal(SeedLabelAction.ReusedByNormalizedName, second.Action);

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM label"));
            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM label_alias"));
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
