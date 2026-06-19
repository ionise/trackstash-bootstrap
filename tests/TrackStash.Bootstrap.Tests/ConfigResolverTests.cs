using TrackStash.Bootstrap.Config;

namespace TrackStash.Bootstrap.Tests;

public sealed class ConfigResolverTests
{
    [Fact]
    public void Resolve_UsesYamlFileValues_WhenNoOverridesProvided()
    {
        var configPath = CreateTempConfigFile("""
provider: sqlite
sqlite:
  dbPath: /tmp/from-file.db
migrations:
  mode: manual
output:
  format: json
logging:
  verbosity: detailed
""");

        try
        {
            ClearRelevantEnvironmentVariables();

            var config = ConfigResolver.Resolve(new Dictionary<string, string?>
            {
                ["config"] = configPath,
            });

            Assert.Equal("sqlite", config.Provider);
            Assert.Equal("/tmp/from-file.db", config.Sqlite.DbPath);
            Assert.Equal("manual", config.Migrations.Mode);
            Assert.Equal("json", config.Output.Format);
            Assert.Equal("detailed", config.Logging.Verbosity);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Resolve_PrefersCliOptions_OverEnvironmentAndConfigFile()
    {
        var configPath = CreateTempConfigFile("""
provider: sqlite
sqlite:
  dbPath: /tmp/from-file.db
output:
  format: text
logging:
  verbosity: normal
""");

        var previousDbPath = Environment.GetEnvironmentVariable("TRACKSTASH_SQLITE_DB_PATH");
        var previousOutput = Environment.GetEnvironmentVariable("TRACKSTASH_OUTPUT_FORMAT");

        try
        {
            Environment.SetEnvironmentVariable("TRACKSTASH_SQLITE_DB_PATH", "/tmp/from-env.db");
            Environment.SetEnvironmentVariable("TRACKSTASH_OUTPUT_FORMAT", "text");

            var config = ConfigResolver.Resolve(new Dictionary<string, string?>
            {
                ["config"] = configPath,
                ["db-path"] = "/tmp/from-cli.db",
                ["output"] = "json",
            });

            Assert.Equal("/tmp/from-cli.db", config.Sqlite.DbPath);
            Assert.Equal("json", config.Output.Format);
            Assert.Equal("sqlite", config.Provider);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACKSTASH_SQLITE_DB_PATH", previousDbPath);
            Environment.SetEnvironmentVariable("TRACKSTASH_OUTPUT_FORMAT", previousOutput);
            File.Delete(configPath);
        }
    }

    private static string CreateTempConfigFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackstash-bootstrap-config-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, content);
        return path;
    }

    private static void ClearRelevantEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("TRACKSTASH_PROVIDER", null);
        Environment.SetEnvironmentVariable("TRACKSTASH_SQLITE_DB_PATH", null);
        Environment.SetEnvironmentVariable("TRACKSTASH_MIGRATIONS_MODE", null);
        Environment.SetEnvironmentVariable("TRACKSTASH_OUTPUT_FORMAT", null);
        Environment.SetEnvironmentVariable("TRACKSTASH_VERBOSITY", null);
        Environment.SetEnvironmentVariable("TRACKSTASH_CONFIG", null);
    }
}
