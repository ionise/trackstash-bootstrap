using TrackStash.Bootstrap;

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
	var bootstrap = new BootstrapCommands();

	try
	{
		return command switch
		{
			"init-db" => await RunInitDbAsync(bootstrap, options).ConfigureAwait(false),
			"seed-label" => await RunSeedLabelAsync(bootstrap, options).ConfigureAwait(false),
			_ => UnknownCommand(command),
		};
	}
	catch (ArgumentException ex)
	{
		Console.Error.WriteLine(ex.Message);
		return 2;
	}
	catch (Exception ex)
	{
		Console.Error.WriteLine($"Unexpected error: {ex.Message}");
		return 1;
	}
}

static async Task<int> RunInitDbAsync(BootstrapCommands bootstrap, IReadOnlyDictionary<string, string?> options)
{
	var dbPath = GetRequiredOption(options, "db-path");
	var result = await bootstrap.InitDbAsync(dbPath).ConfigureAwait(false);

	Console.WriteLine($"provider: sqlite");
	Console.WriteLine($"database: {result.DatabasePath}");
	Console.WriteLine($"currentVersion: {result.CurrentVersion}");
	Console.WriteLine($"appliedMigrations: {result.AppliedMigrationsCount}");
	Console.WriteLine("status: ready");
	return 0;
}

static async Task<int> RunSeedLabelAsync(BootstrapCommands bootstrap, IReadOnlyDictionary<string, string?> options)
{
	var dbPath = GetRequiredOption(options, "db-path");
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
	Console.WriteLine($"labelId: {result.LabelId}");
	Console.WriteLine($"action: {result.Action}");
	Console.WriteLine($"normalizedName: {result.NormalizedName}");
	return 0;
}

static int UnknownCommand(string command)
{
	Console.Error.WriteLine($"Unknown command: {command}");
	PrintUsage();
	return 2;
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
{
	return options.TryGetValue(key, out var value) ? value : null;
}

static void PrintUsage()
{
	Console.WriteLine("Usage:");
	Console.WriteLine("  trackstash-bootstrap init-db --db-path <path>");
	Console.WriteLine("  trackstash-bootstrap seed-label --db-path <path> --name <name> [--id <labelId>] [--source <source> --external-id <id>]");
}
