using TrackStash.Core.Normalization;
using TrackStash.Core.Sqlite;
using TrackStash.Core.Storage;

namespace TrackStash.Bootstrap;

public sealed class BootstrapCommands
{
    public async Task<InitDbResult> InitDbAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var provider = new SqliteStorageProvider(databasePath);
        var migrationResult = await provider.Migrations.ApplyPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);

        return new InitDbResult(
            DatabasePath: databasePath,
            CurrentVersion: migrationResult.CurrentVersion,
            AppliedMigrationsCount: migrationResult.AppliedMigrations.Count);
    }

    public async Task<SeedLabelResult> SeedLabelAsync(SeedLabelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var normalizedName = EntityNameNormalizer.NormalizeStrict(request.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Label name cannot normalize to an empty key.");

        var provider = new SqliteStorageProvider(request.DatabasePath);
        await using var uow = await provider.BeginUnitOfWorkAsync(cancellationToken).ConfigureAwait(false);

        Label? existingByExternal = null;
        if (!string.IsNullOrWhiteSpace(request.Source) && !string.IsNullOrWhiteSpace(request.ExternalId))
        {
            existingByExternal = await uow.Labels
                .GetByExternalRefAsync(request.Source!, request.ExternalId!, cancellationToken)
                .ConfigureAwait(false);
        }

        var existingByNormalized = await uow.Labels
            .GetByNormalizedNameAsync(normalizedName, cancellationToken)
            .ConfigureAwait(false);

        var existing = existingByExternal ?? existingByNormalized;

        var action = existingByExternal is not null
            ? SeedLabelAction.ReusedByExternalReference
            : existingByNormalized is not null
                ? SeedLabelAction.ReusedByNormalizedName
                : SeedLabelAction.Created;

        var labelId = ResolveLabelId(request, existing);
        var now = DateTimeOffset.UtcNow;

        var aliases = BuildAliases(existing, request.Name, normalizedName);
        var refs = BuildExternalReferences(request, now);

        var label = new Label
        {
            Id = labelId,
            Name = existing?.Name ?? request.Name,
            NormalizedName = existing?.NormalizedName ?? normalizedName,
            SortName = existing?.SortName,
            SourcePayloadJson = existing?.SourcePayloadJson,
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
            Aliases = aliases,
            ExternalReferences = refs,
        };

        await uow.Labels.UpsertAsync(label, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SeedLabelResult(labelId, normalizedName, action);
    }

    private static string ResolveLabelId(SeedLabelRequest request, Label? existing)
    {
        if (existing is not null)
            return existing.Id;

        if (!string.IsNullOrWhiteSpace(request.LabelId))
            return request.LabelId!;

        return Guid.NewGuid().ToString("D").ToLowerInvariant();
    }

    private static IReadOnlyList<EntityAlias> BuildAliases(Label? existing, string inputName, string normalizedName)
    {
        var aliases = existing?.Aliases?.ToList() ?? [];
        var hasAlias = aliases.Any(alias => string.Equals(alias.Value, inputName, StringComparison.OrdinalIgnoreCase));

        if (!hasAlias && existing is not null && !string.Equals(existing.Name, inputName, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(new EntityAlias
            {
                Value = inputName,
                NormalizedValue = normalizedName,
                IsPrimary = false,
            });
        }

        return aliases;
    }

    private static IReadOnlyList<EntityReference> BuildExternalReferences(SeedLabelRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.Source) || string.IsNullOrWhiteSpace(request.ExternalId))
            return Array.Empty<EntityReference>();

        return
        [
            new EntityReference
            {
                Source = request.Source!,
                ExternalId = request.ExternalId!,
                IsPrimary = true,
                LastSeenUtc = now,
            },
        ];
    }
}

public sealed record InitDbResult(string DatabasePath, int CurrentVersion, int AppliedMigrationsCount);

public sealed record SeedLabelRequest(
    string DatabasePath,
    string Name,
    string? LabelId = null,
    string? Source = null,
    string? ExternalId = null);

public sealed record SeedLabelResult(string LabelId, string NormalizedName, SeedLabelAction Action);

public enum SeedLabelAction
{
    Created = 0,
    ReusedByExternalReference = 1,
    ReusedByNormalizedName = 2,
}
