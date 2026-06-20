using Microsoft.Data.Sqlite;
using TrackStash.Bootstrap;

namespace TrackStash.Bootstrap.Tests;

public sealed class ImportCsvIntegrationTests
{
    [Fact]
    public async Task ImportCsvAsync_ImportsFullHierarchy_InDependencyOrder()
    {
        var dbPath  = TempDb();
        var csvPath = TempCsv("""
            type,name,title,sort_name,isrc,mix_name,label_ref,artist_ref,artist_role,release_ref,disc_number,track_number,source,external_id,id
            label,Virelith Records,,,,,,,,,,,,
            artist,Bozra Bozra,,Bozra Bozra,,,,,,,,,
            release,,Virelith Sessions,,,,Virelith Records,Bozra Bozra,,,,,,,
            recording,,Signal Drift,,TST000000001,Original Mix,,"Bozra Bozra",primary,"Virelith Sessions",1,1,,,
            """);
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);
            var result = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath));

            Assert.Equal(4, result.TotalRows);
            Assert.Equal(4, result.SucceededRows);
            Assert.Equal(0, result.FailedRows);
            Assert.Equal(0, result.WarningCount);
            Assert.All(result.RowResults, r => Assert.True(r.Success));
            Assert.All(result.RowResults, r => Assert.Empty(r.Warnings));

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM label"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM artist"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM recording"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release_label_link"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release_artist_credit"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release_recording"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM recording_artist_credit"));
        }
        finally { Cleanup(dbPath, csvPath); }
    }

    [Fact]
    public async Task ImportCsvAsync_IsIdempotent_WhenRunTwice()
    {
        var dbPath  = TempDb();
        var csvPath = TempCsv("""
            type,name,title,label_ref,artist_ref
            label,Virelith Records,,,,
            artist,Bozra Bozra,,,,
            release,,Virelith Sessions,Virelith Records,Bozra Bozra
            """);
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);
            var first  = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath));
            var second = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath));

            Assert.Equal(0, first.FailedRows);
            Assert.Equal(0, second.FailedRows);

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM label"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM artist"));
            Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release"));
        }
        finally { Cleanup(dbPath, csvPath); }
    }

    [Fact]
    public async Task ImportCsvAsync_DryRun_DoesNotWriteToDatabase()
    {
        var dbPath  = TempDb();
        var csvPath = TempCsv("""
            type,name
            label,Virelith Records
            artist,Bozra Bozra
            """);
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);
            var result = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath, DryRun: true));

            Assert.Equal(2, result.TotalRows);
            Assert.Equal(2, result.SucceededRows);
            Assert.True(result.DryRun);
            Assert.False(result.FailFast);
            Assert.All(result.RowResults, r => Assert.Equal("DryRun", r.Action));

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM label"));
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM artist"));
        }
        finally { Cleanup(dbPath, csvPath); }
    }

    [Fact]
    public async Task ImportCsvAsync_SkipsInvalidRows_AndReportsErrors()
    {
        var dbPath  = TempDb();
        var csvPath = TempCsv("""
            type,name,title
            label,Virelith Records,
            label,,
            release,,
            """);
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);
            var result = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath));

            Assert.Equal(3, result.TotalRows);
            Assert.Equal(1, result.SucceededRows);
            Assert.Equal(2, result.FailedRows);

            var failures = result.RowResults.Where(r => !r.Success).ToList();
            Assert.All(failures, r => Assert.Contains("Missing required field", r.Error!));
        }
        finally { Cleanup(dbPath, csvPath); }
    }

    [Fact]
    public async Task ImportCsvAsync_ReportsWarnings_ForUnresolvedLinks()
    {
        var dbPath = TempDb();
        var csvPath = TempCsv("""
            type,title,label_ref,artist_ref
            release,Warning Release,Missing Label,Missing Artist
            """);
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);
            var result = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath));

            Assert.Equal(1, result.TotalRows);
            Assert.Equal(1, result.SucceededRows);
            Assert.Equal(0, result.FailedRows);
            Assert.Equal(2, result.WarningCount);

            var row = Assert.Single(result.RowResults);
            Assert.True(row.Success);
            Assert.Equal(2, row.Warnings.Count);
            Assert.Contains("Unresolved label reference", row.Warnings[0]);
            Assert.Contains("Unresolved artist reference", row.Warnings[1]);
        }
        finally { Cleanup(dbPath, csvPath); }
    }

    [Fact]
    public async Task ImportCsvAsync_FailFast_StopsAfterFirstFailure()
    {
        var dbPath = TempDb();
        var csvPath = TempCsv("""
            type,name,title
            label,,
            artist,Artist That Should Not Run,
            """);
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);
            var result = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath, FailFast: true));

            Assert.Equal(2, result.TotalRows);
            Assert.Equal(0, result.SucceededRows);
            Assert.Equal(1, result.FailedRows);
            Assert.True(result.FailFast);
            Assert.Single(result.RowResults);
            Assert.False(result.RowResults[0].Success);

            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM label"));
            Assert.Equal(0, await CountAsync(conn, "SELECT COUNT(*) FROM artist"));
        }
        finally { Cleanup(dbPath, csvPath); }
    }

    [Fact]
    public async Task ImportCsvAsync_ResolvesLabelAndArtistFromExistingDb_ForReleaseRows()
    {
        var dbPath  = TempDb();
        var commands = new BootstrapCommands();

        try
        {
            await commands.InitDbAsync(dbPath);

            // seed label and artist before the CSV runs
            var label  = await commands.SeedLabelAsync(new SeedLabelRequest(DatabasePath: dbPath, Name: "Pre-Existing Records"));
            var artist = await commands.SeedArtistAsync(new SeedArtistRequest(DatabasePath: dbPath, Name: "Pre-Existing Artist"));

            // CSV only has a release that references the pre-existing entities by name
            var csvPath = TempCsv($"""
                type,name,title,label_ref,artist_ref
                release,,Pre-Existing Release,Pre-Existing Records,Pre-Existing Artist
                """);

            try
            {
                var result = await commands.ImportCsvAsync(new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath));
                Assert.Equal(1, result.TotalRows);
                Assert.Equal(1, result.SucceededRows);

                await using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();
                Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release_label_link"));
                Assert.Equal(1, await CountAsync(conn, "SELECT COUNT(*) FROM release_artist_credit"));
            }
            finally { File.Delete(csvPath); }
        }
        finally { Cleanup(dbPath, null); }
    }

    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), $"trackstash-import-csv-{Guid.NewGuid():N}.db");

    private static string TempCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackstash-import-csv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content.TrimStart());
        return path;
    }

    private static void Cleanup(string dbPath, string? csvPath)
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);
        if (csvPath is not null && File.Exists(csvPath)) File.Delete(csvPath);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
