using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrackStash.Bootstrap.Config;

public static class ConfigResolver
{
    public static BootstrapConfig Resolve(IReadOnlyDictionary<string, string?> cliOptions)
    {
        var config = new BootstrapConfig();

        var configFilePath = GetValue(cliOptions, "config")
            ?? Environment.GetEnvironmentVariable("TRACKSTASH_CONFIG");

        if (!string.IsNullOrWhiteSpace(configFilePath))
            ApplyFile(config, configFilePath);

        ApplyEnvironmentVariables(config);
        ApplyCliOptions(config, cliOptions);

        return config;
    }

    private static void ApplyFile(BootstrapConfig config, string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Config file not found: {filePath}");

        var yaml = File.ReadAllText(filePath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var file = deserializer.Deserialize<BootstrapConfig>(yaml);
        if (file is null)
            return;

        if (!string.IsNullOrWhiteSpace(file.Provider))
            config.Provider = file.Provider;

        if (!string.IsNullOrWhiteSpace(file.Sqlite?.DbPath))
            config.Sqlite.DbPath = file.Sqlite.DbPath;

        if (!string.IsNullOrWhiteSpace(file.Migrations?.Mode))
            config.Migrations.Mode = file.Migrations.Mode;

        if (!string.IsNullOrWhiteSpace(file.Output?.Format))
            config.Output.Format = file.Output.Format;

        if (!string.IsNullOrWhiteSpace(file.Logging?.Verbosity))
            config.Logging.Verbosity = file.Logging.Verbosity;
    }

    private static void ApplyEnvironmentVariables(BootstrapConfig config)
    {
        var provider = Environment.GetEnvironmentVariable("TRACKSTASH_PROVIDER");
        if (!string.IsNullOrWhiteSpace(provider)) config.Provider = provider;

        var dbPath = Environment.GetEnvironmentVariable("TRACKSTASH_SQLITE_DB_PATH");
        if (!string.IsNullOrWhiteSpace(dbPath)) config.Sqlite.DbPath = dbPath;

        var migrationsMode = Environment.GetEnvironmentVariable("TRACKSTASH_MIGRATIONS_MODE");
        if (!string.IsNullOrWhiteSpace(migrationsMode)) config.Migrations.Mode = migrationsMode;

        var outputFormat = Environment.GetEnvironmentVariable("TRACKSTASH_OUTPUT_FORMAT");
        if (!string.IsNullOrWhiteSpace(outputFormat)) config.Output.Format = outputFormat;

        var verbosity = Environment.GetEnvironmentVariable("TRACKSTASH_VERBOSITY");
        if (!string.IsNullOrWhiteSpace(verbosity)) config.Logging.Verbosity = verbosity;
    }

    private static void ApplyCliOptions(BootstrapConfig config, IReadOnlyDictionary<string, string?> options)
    {
        if (GetValue(options, "provider") is { } provider)
            config.Provider = provider;

        if (GetValue(options, "db-path") is { } dbPath)
            config.Sqlite.DbPath = dbPath;

        if (GetValue(options, "output") is { } output)
            config.Output.Format = output;

        if (GetValue(options, "verbosity") is { } verbosity)
            config.Logging.Verbosity = verbosity;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> options, string key)
        => options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
