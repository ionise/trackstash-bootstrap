using System.Globalization;
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
			"migrate" => await RunMigrateAsync(bootstrap, config, jsonMode).ConfigureAwait(false),
			"import-csv" => await RunImportCsvAsync(bootstrap, config, options, jsonMode).ConfigureAwait(false),
			"doctor" => await RunNotImplementedAsync("doctor", jsonMode).ConfigureAwait(false),
			"repair-indexes" => await RunNotImplementedAsync("repair-indexes", jsonMode).ConfigureAwait(false),
			"seed-label" => await RunSeedLabelAsync(bootstrap, config, options, jsonMode).ConfigureAwait(false),
			"seed-artist" => await RunSeedArtistAsync(bootstrap, config, options, jsonMode).ConfigureAwait(false),
			"seed-release" => await RunSeedReleaseAsync(bootstrap, config, options, jsonMode).ConfigureAwait(false),
			"seed-recording" => await RunSeedRecordingAsync(bootstrap, config, options, jsonMode).ConfigureAwait(false),
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

static async Task<int> RunImportCsvAsync(
	BootstrapCommands bootstrap,
	BootstrapConfig config,
	IReadOnlyDictionary<string, string?> options,
	bool jsonMode)
{
	var dbPath  = RequireDbPath(config);
	var csvPath = GetRequiredOption(options, "file");
	var dryRun  = options.ContainsKey("dry-run");
	var failFast = options.ContainsKey("fail-fast");

	var request = new ImportCsvRequest(DatabasePath: dbPath, CsvPath: csvPath, DryRun: dryRun, FailFast: failFast);
	var result  = await bootstrap.ImportCsvAsync(request).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("import-csv", ok: result.FailedRows == 0, exitCode: result.FailedRows > 0 ? 1 : 0, data: new
		{
			databasePath = result.DatabasePath,
			csvPath = result.CsvPath,
			totalRows = result.TotalRows,
			succeededRows = result.SucceededRows,
			failedRows = result.FailedRows,
			dryRun = result.DryRun,
			failFast = result.FailFast,
			warningCount = result.WarningCount,
			rowResults = result.RowResults.Select(r => new
			{
				rowNumber = r.RowNumber,
				entityType = r.EntityType,
				entityId = r.EntityId,
				action = r.Action,
				success = r.Success,
				error = r.Error,
				warnings = r.Warnings,
			}),
		});
	}
	else
	{
		CommandOutput.WriteText([
			("database", result.DatabasePath),
			("csv", result.CsvPath),
			("totalRows", result.TotalRows),
			("succeededRows", result.SucceededRows),
			("failedRows", result.FailedRows),
			("dryRun", result.DryRun),
			("failFast", result.FailFast),
			("warningCount", result.WarningCount),
		]);
		foreach (var row in result.RowResults.Where(r => r.Warnings.Count > 0))
			Console.Error.WriteLine($"  Row {row.RowNumber} ({row.EntityType}) warnings: {string.Join("; ", row.Warnings)}");
		foreach (var row in result.RowResults.Where(r => !r.Success))
			Console.Error.WriteLine($"  Row {row.RowNumber} ({row.EntityType}): {row.Error}");
	}

	return result.FailedRows > 0 ? 1 : 0;
}

static Task<int> RunNotImplementedAsync(string command, bool jsonMode)
{
	var message = $"Command not yet implemented: {command}";

	if (jsonMode)
		CommandOutput.WriteJson(command, ok: false, exitCode: 1, data: null, errors: [message]);
	else
		Console.Error.WriteLine(message);

	return Task.FromResult(1);
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

static async Task<int> RunMigrateAsync(BootstrapCommands bootstrap, BootstrapConfig config, bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var result = await bootstrap.MigrateAsync(dbPath).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("migrate", ok: true, exitCode: 0, data: new
		{
			databasePath = dbPath,
			currentVersion = result.CurrentVersion,
			appliedMigrations = result.AppliedMigrations.Count,
			wasSuccessful = result.WasSuccessful,
			message = result.Message,
		});
	}
	else
	{
		CommandOutput.WriteText([
			("database", dbPath),
			("currentVersion", result.CurrentVersion),
			("appliedMigrations", result.AppliedMigrations.Count),
			("wasSuccessful", result.WasSuccessful),
			("message", result.Message),
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

static async Task<int> RunSeedArtistAsync(
	BootstrapCommands bootstrap,
	BootstrapConfig config,
	IReadOnlyDictionary<string, string?> options,
	bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var name = GetRequiredOption(options, "name");
	var artistId = GetOption(options, "id");
	var sortName = GetOption(options, "sort-name");
	var source = GetOption(options, "source");
	var externalId = GetOption(options, "external-id");

	if (!string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(source))
		throw new ArgumentException("--source is required when --external-id is provided.");

	var request = new SeedArtistRequest(
		DatabasePath: dbPath,
		Name: name,
		ArtistId: artistId,
		SortName: sortName,
		Source: source,
		ExternalId: externalId);

	var result = await bootstrap.SeedArtistAsync(request).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("seed-artist", ok: true, exitCode: 0, data: new
		{
			artistId = result.ArtistId,
			action = result.Action.ToString(),
			normalizedName = result.NormalizedName,
		});
	}
	else
	{
		CommandOutput.WriteText([
			("artistId", result.ArtistId),
			("action", result.Action),
			("normalizedName", result.NormalizedName),
		]);
	}

	return 0;
}

static async Task<int> RunSeedReleaseAsync(
	BootstrapCommands bootstrap,
	BootstrapConfig config,
	IReadOnlyDictionary<string, string?> options,
	bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var title = GetRequiredOption(options, "title");
	var releaseId = GetOption(options, "id");
	var labelId = GetOption(options, "label-id");
	var artistId = GetOption(options, "artist-id");
	var source = GetOption(options, "source");
	var externalId = GetOption(options, "external-id");

	if (!string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(source))
		throw new ArgumentException("--source is required when --external-id is provided.");

	var request = new SeedReleaseRequest(
		DatabasePath: dbPath,
		Title: title,
		ReleaseId: releaseId,
		LabelId: labelId,
		ArtistId: artistId,
		Source: source,
		ExternalId: externalId);

	var result = await bootstrap.SeedReleaseAsync(request).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("seed-release", ok: true, exitCode: 0, data: new
		{
			releaseId = result.ReleaseId,
			action = result.Action.ToString(),
			normalizedTitle = result.NormalizedTitle,
		});
	}
	else
	{
		CommandOutput.WriteText([
			("releaseId", result.ReleaseId),
			("action", result.Action),
			("normalizedTitle", result.NormalizedTitle),
		]);
	}

	return 0;
}

static async Task<int> RunSeedRecordingAsync(
	BootstrapCommands bootstrap,
	BootstrapConfig config,
	IReadOnlyDictionary<string, string?> options,
	bool jsonMode)
{
	var dbPath = RequireDbPath(config);
	var title = GetRequiredOption(options, "title");
	var recordingId = GetOption(options, "id");
	var mixName = GetOption(options, "mix-name");
	var isrc = GetOption(options, "isrc");
	var releaseId = GetOption(options, "release-id");
	var discNumber = ParseNullableIntOption(options, "disc-number");
	var trackNumber = ParseNullableIntOption(options, "track-number");
	var artistId = GetOption(options, "artist-id");
	var artistRole = GetOption(options, "artist-role");
	var relatedRecordingId = GetOption(options, "related-recording-id");
	var relationshipType = GetOption(options, "relationship-type");
	var relationshipSource = GetOption(options, "relationship-source");
	var relationshipConfidence = ParseNullableDecimalOption(options, "relationship-confidence");
	var relationshipNotes = GetOption(options, "relationship-notes");
	var source = GetOption(options, "source");
	var externalId = GetOption(options, "external-id");

	if (!string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(source))
		throw new ArgumentException("--source is required when --external-id is provided.");

	var request = new SeedRecordingRequest(
		DatabasePath: dbPath,
		Title: title,
		RecordingId: recordingId,
		MixName: mixName,
		Isrc: isrc,
		ReleaseId: releaseId,
		DiscNumber: discNumber,
		TrackNumber: trackNumber,
		ArtistId: artistId,
		ArtistRole: artistRole,
		RelatedRecordingId: relatedRecordingId,
		RelationshipType: relationshipType,
		RelationshipSource: relationshipSource,
		RelationshipConfidence: relationshipConfidence,
		RelationshipNotes: relationshipNotes,
		Source: source,
		ExternalId: externalId);

	var result = await bootstrap.SeedRecordingAsync(request).ConfigureAwait(false);

	if (jsonMode)
	{
		CommandOutput.WriteJson("seed-recording", ok: true, exitCode: 0, data: new
		{
			recordingId = result.RecordingId,
			action = result.Action.ToString(),
			normalizedTitle = result.NormalizedTitle,
			normalizedMixName = result.NormalizedMixName,
		});
	}
	else
	{
		CommandOutput.WriteText([
			("recordingId", result.RecordingId),
			("action", result.Action),
			("normalizedTitle", result.NormalizedTitle),
			("normalizedMixName", result.NormalizedMixName),
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

static int? ParseNullableIntOption(IReadOnlyDictionary<string, string?> options, string key)
{
	var value = GetOption(options, key);
	if (string.IsNullOrWhiteSpace(value))
		return null;

	if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
		throw new ArgumentException($"--{key} must be an integer.");

	return parsed;
}

static decimal? ParseNullableDecimalOption(IReadOnlyDictionary<string, string?> options, string key)
{
	var value = GetOption(options, key);
	if (string.IsNullOrWhiteSpace(value))
		return null;

	if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
		throw new ArgumentException($"--{key} must be a decimal number.");

	return parsed;
}

static void PrintUsage()
{
	Console.WriteLine("Usage:");
	Console.WriteLine("  trackstash-bootstrap status     --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap init-db    --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap migrate    --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap import-csv --db-path <path> --file <path.csv> [--dry-run] [--fail-fast] [--output json]");
	Console.WriteLine("  trackstash-bootstrap doctor     --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap repair-indexes --db-path <path> [--output json]");
	Console.WriteLine("  trackstash-bootstrap seed-label --db-path <path> --name <name> [--id <id>] [--source <source> --external-id <id>] [--output json]");
	Console.WriteLine("  trackstash-bootstrap seed-artist --db-path <path> --name <name> [--id <id>] [--sort-name <sort>] [--source <source> --external-id <id>] [--output json]");
	Console.WriteLine("  trackstash-bootstrap seed-release --db-path <path> --title <title> [--id <id>] [--label-id <id>] [--artist-id <id>] [--source <source> --external-id <id>] [--output json]");
	Console.WriteLine("  trackstash-bootstrap seed-recording --db-path <path> --title <title> [--id <id>] [--mix-name <mix>] [--isrc <isrc>] [--release-id <id>] [--disc-number <n>] [--track-number <n>] [--artist-id <id>] [--artist-role <role>] [--related-recording-id <id>] [--relationship-type <type>] [--relationship-source <source>] [--relationship-confidence <value>] [--relationship-notes <text>] [--source <source> --external-id <id>] [--output json]");
	Console.WriteLine();
	Console.WriteLine("Options resolved from (highest to lowest priority):");
	Console.WriteLine("  CLI flags > env vars > config file (--config <path>) > defaults");
}
