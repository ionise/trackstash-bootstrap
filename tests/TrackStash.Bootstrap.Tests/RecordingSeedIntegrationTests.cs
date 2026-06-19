using Microsoft.Data.Sqlite;
using TrackStash.Bootstrap;

namespace TrackStash.Bootstrap.Tests;

public sealed class RecordingSeedIntegrationTests
{
    [Fact]
    public async Task SeedRecordingAsync_ReusesCanonicalRow_AndPersistsReleaseLinkAndRelationship()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trackstash-bootstrap-recording-{Guid.NewGuid():N}.db");
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

            var release = await commands.SeedReleaseAsync(new SeedReleaseRequest(
                DatabasePath: dbPath,
                Title: "Signal Drift",
                LabelId: label.LabelId,
                ArtistId: artist.ArtistId));

            var remixSource = await commands.SeedRecordingAsync(new SeedRecordingRequest(
                DatabasePath: dbPath,
                Title: "Signal Drift (Original)",
                ReleaseId: release.ReleaseId,
                TrackNumber: 1,
                ArtistId: artist.ArtistId));

            var first = await commands.SeedRecordingAsync(new SeedRecordingRequest(
                DatabasePath: dbPath,
                Title: "Signal Drift (Remix)",
                MixName: "Extended Mix",
                ReleaseId: release.ReleaseId,
                DiscNumber: 1,
                TrackNumber: 2,
                ArtistId: artist.ArtistId,
                RelatedRecordingId: remixSource.RecordingId,
                RelationshipType: "remix_of"));

            var second = await commands.SeedRecordingAsync(new SeedRecordingRequest(
                DatabasePath: dbPath,
                Title: "Signal Drift (Remix)",
                MixName: "Extended Mix",
                ReleaseId: release.ReleaseId,
                DiscNumber: 1,
                TrackNumber: 2,
                ArtistId: artist.ArtistId,
                RelatedRecordingId: remixSource.RecordingId,
                RelationshipType: "remix_of"));

            Assert.Equal(first.RecordingId, second.RecordingId);
            Assert.Equal(SeedRecordingAction.ReusedByNormalizedTitleAndMixName, second.Action);

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM recording"));
            Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM release_recording"));
            Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM recording_relationship"));
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
