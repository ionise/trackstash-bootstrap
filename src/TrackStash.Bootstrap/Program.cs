using TrackStash.Bootstrap;
using TrackStash.Bootstrap.Config;
using TrackStash.Bootstrap.Output;

var exitCode = await RunAsync(args).ConfigureAwait(false);
return exitCode;

static async Task<int> RunAsync(string[] args)
{
	if (args.Length == 0)
	{
		PrintUsage();
		return 2;
	}

	var command = args[0].ToLowerInvariant();
	var options = ParseOptions(args, 1);
	var config = ConfigResolver.Resolve(options);
	var bootstrap = new BootstrapCommands();
	var jsonMode = CommandOutput.IsJsonMode(config.Output.Format);

	try
	{
		return command switch
		{
			"init-db"    => await RunInitDbAsync(bootstrap, config, jsonMode).ConfigureAwait(false),
			"seed-label" => await RunSeedLabelAsync(bootstrap, config, options, jsonMode).ConfigureAwait(false),
			"status"     => await RunStatusAsync(bootstrap, config, jsonMode).ConfigureAwait(false),
			_            => UnknownCommand(command),
		};
	}
	catch (ArgumentException ex)
	{
		if (jsonMode)
			CommandOutput.WriteJson(command, ok: false, exitCode: 2, data: null, errors: [ex.Message]);
		else
			Console.Error.WriteLine(ex.Message);
		return 2;
	}
	catch (Exception ex)
	{
		if (jsonMode)
			CommandOutput.WriteJson(command, ok: false, exitCode: 1, data: null, errors: [ex.Message]);
		else
			Console.Error.WriteLine($"Unexpected error: {ex.Message}");
		return 1;
	}
}

static async Task<int> RunStatusAsync(BootstrapCommands bootstrap, BootstrapConfig config, bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var result = await bootstrap.StatusAsync(dbPath).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("status", ok: true, exitCode: 0, data: new
		{
			databasePath = result.DatabasePath,
			databaseExists = result.DatabaseExists,
			currentVersion = result.CurrentVersion,
			capabilities = new
			{
				supportsTransactions = result.Capabilities.SupportsTransactions,
				supportsCaseInsensitiveSearch = result.Capabilities.SupportsCaseInsensitiveSearch,
				supportsBinaryVectorStorage = result.Capabilities.SupportsBinaryVectorStorage,
				supportsJsonPayloadStorage = result.Capabilities.SupportsJsonPayloadStorage,
				supportsIndexedExternalRefs = result.Capabilities.SupportsIndexedExternalRefs,
			},
		});
	}
	else
	{
		CommandOutput.WriteText([
			("provider", config.Provider),
			("database", result.DatabasePath),
			("databaseExists", result.DatabaseExists),
			("currentVersion", result.CurrentVersion),
			("supportsTransactions", result.Capabilities.SupportsTransactions),
			("supportsCaseInsensitiveSearch", result.Capabilities.SupportsCaseInsensitiveSearch),
			("supportsBinaryVectorStorage", result.Capabilities.SupportsBinaryVectorStorage),
			("supportsJsonPayloadStorage", result.Capabilities.SupportsJsonPayloadStorage),
			("supportsIndexedExternalRefs", result.Capabilities.SupportsIndexedExternalRefs),
		]);
	}

	return 0;
}

static async Task<int> RunInitDbAsync(BootstrapCommands bootstrap, BootstrapConfig config, bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var result = await bootstrap.InitDbAsync(dbPath).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("init-db", ok: true, exitCode: 0, data: new
		{
			provider = config.Provider,
			databasePath = result.DatabasePath,
			currentVersion = result.CurrentVersion,
			appliedMigrations = result.AppliedMigrationsCount,
		});
	}
	else
	{
		CommandOutput.WriteText([
			("provider", config.Provider),
			("database", result.DatabasePath),
			("currentVersion", result.CurrentVersion),
			("appliedMigrations", result.AppliedMigrationsCount),
			("status", "ready"),
		]);
	}

	return 0;
}

static async Task<int> RunSeedLabelAsync(
	BootstrapCommands bootstrap,
	BootstrapConfig config,
	IReadOnlyDictionary<string, string?> options,
	bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var name = GetRequiredOption(options, "name");
	var labelId = GetOption(options, "id");
	var source = GetOption(options, "source");
	var externalId = GetOption(options, "external-id");

	if (!string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(source))
		throw new ArgumentException("--source is required when --external-id is provided.");

	var request = new SeedLabelRequest(
		DatabasePath: dbPath,
		Name: name,
		LabelId: labelId,
		Source: source,
		ExternalId: externalId);

	var result = await bootstrap.SeedLabelAsync(request).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("seed-label", ok: true, exitCode: 0, data: new
		{
			labelId = result.LabelId,
			action = result.Action.ToString(),
			normalizedName = result.NormalizedName,
		});
	}
	else
	{
		CommandOutput.WriteText([
			("labelId", result.LabelId),
			("action", result.Action),
			("normalizedName", result.NormalizedName),
		]);
	}

	return 0;
}

static int UnknownCommand(string command)
{
	Console.Error.WriteLine($"Unknown command: {command}");
	PrintUsage();
	return 2;
}

static string RequireDbPath(BootstrapConfig config)
{
	if (string.IsNullOrWhiteSpace(config.Sqlite.DbPath))
		throw new ArgumentException("--db-path is required (or set sqlite.dbPath in config file / TRACKSTASH_SQLITE_DB_PATH env var).");
	return config.Sqlite.DbPath;
}

static Dictionary<string, string?> ParseOptions(string[] args, int startIndex)
{
	var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

	for (var i = startIndex; i < args.Length; i++)
	{
		var arg = args[i];
		if (!arg.StartsWith("--", StringComparison.Ordinal))
			continue;

		var key = arg[2..];
		string? value = null;

		if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
		{
			value = args[i + 1];
			i++;
		}

		options[key] = value;
	}

	return options;
}

static string GetRequiredOption(IReadOnlyDictionary<string, string?> options, string key)
{
	var value = GetOption(options, key);
	if (string.IsNullOrWhiteSpace(value))
		throw new ArgumentException($"--{key} is required.");
	return value;
}

static string? GetOption(IReadOnlyDictionary<string, string?> options, string key)
	=> options.TryGetValue(key, out var value) ? value : null;

static void PrintUsage()
{
	Console.WriteLine("Usage:");
	Console.WriteLine("  trackstash-bootstrap status     --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap init-db    --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap seed-label --db-path <path> --name <name> [--id <id>] [--source <source> --external-id <id>] [--output json]");
	Console.WriteLine();
	Console.WriteLine("Options resolved from (highest to lowest priority):");
	Console.WriteLine("  CLI flags > env vars > config file (--config <path>) > defaults");
}
