namespace TrackStash.Bootstrap.Tests;

public sealed class MigrateCommandTests
{
    [Fact]
    public async Task MigrateAsync_IsIdempotent_AndReportsCurrentVersion()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trackstash-bootstrap-migrate-{Guid.NewGuid():N}.db");
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);

            var first = await commands.MigrateAsync(dbPath);
            var second = await commands.MigrateAsync(dbPath);

            Assert.True(first.WasSuccessful);
            Assert.True(second.WasSuccessful);
            Assert.Equal(first.CurrentVersion, second.CurrentVersion);
            Assert.Empty(first.AppliedMigrations);
            Assert.Empty(second.AppliedMigrations);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
