using TrackStash.Core.Normalization;
using TrackStash.Core.Sqlite;
using TrackStash.Core.Storage;

namespace TrackStash.Bootstrap;

public sealed record SeedReleaseRequest(
    string DatabasePath,
    string Title,
    string? ReleaseId = null,
    string? LabelId = null,
    string? ArtistId = null,
    string? Source = null,
    string? ExternalId = null);

public sealed record SeedReleaseResult(string ReleaseId, string NormalizedTitle, SeedReleaseAction Action);

public enum SeedReleaseAction
{
    Created = 0,
    ReusedByExternalReference = 1,
    ReusedByNormalizedTitleAndLabel = 2,
    ReusedByNormalizedTitle = 3,
}

public sealed record SeedRecordingRequest(
    string DatabasePath,
    string Title,
    string? RecordingId = null,
    string? MixName = null,
    string? Isrc = null,
    string? ReleaseId = null,
    int? DiscNumber = null,
    int? TrackNumber = null,
    string? ArtistId = null,
    string? ArtistRole = null,
    string? RelatedRecordingId = null,
    string? RelationshipType = null,
    string? RelationshipSource = null,
    decimal? RelationshipConfidence = null,
    string? RelationshipNotes = null,
    string? Source = null,
    string? ExternalId = null);

public sealed record SeedRecordingResult(string RecordingId, string NormalizedTitle, string? NormalizedMixName, SeedRecordingAction Action);

public enum SeedRecordingAction
{
    Created = 0,
    ReusedByExternalReference = 1,
    ReusedByIsrc = 2,
    ReusedByNormalizedTitleAndMixName = 3,
    ReusedByNormalizedTitle = 4,
}

public sealed record StatusResult(
    string DatabasePath,
    bool DatabaseExists,
    int CurrentVersion,
    StorageCapabilities Capabilities);

public sealed record SeedArtistRequest(
    string DatabasePath,
    string Name,
    string? ArtistId = null,
    string? SortName = null,
    string? Source = null,
    string? ExternalId = null);

public sealed record SeedArtistResult(string ArtistId, string NormalizedName, SeedArtistAction Action);

public enum SeedArtistAction
{
    Created = 0,
    ReusedByExternalReference = 1,
    ReusedByNormalizedName = 2,
}

public sealed class BootstrapCommands
{
    public async Task<StatusResult> StatusAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var provider = new SqliteStorageProvider(databasePath);
        var exists = File.Exists(databasePath);
        var version = exists
            ? await provider.Migrations.GetCurrentVersionAsync(cancellationToken).ConfigureAwait(false)
            : 0;

        return new StatusResult(
            DatabasePath: databasePath,
            DatabaseExists: exists,
            CurrentVersion: version,
            Capabilities: provider.Capabilities);
    }

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

    public async Task<MigrationResult> MigrateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var provider = new SqliteStorageProvider(databasePath);
        return await provider.Migrations.ApplyPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
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
        var refs = BuildExternalReferences(request.Source, request.ExternalId, now);

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

    public async Task<SeedArtistResult> SeedArtistAsync(SeedArtistRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var normalizedName = EntityNameNormalizer.NormalizeStrict(request.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Artist name cannot normalize to an empty key.");

        var provider = new SqliteStorageProvider(request.DatabasePath);
        await using var uow = await provider.BeginUnitOfWorkAsync(cancellationToken).ConfigureAwait(false);

        Artist? existingByExternal = null;
        if (!string.IsNullOrWhiteSpace(request.Source) && !string.IsNullOrWhiteSpace(request.ExternalId))
        {
            existingByExternal = await uow.Artists
                .GetByExternalRefAsync(request.Source!, request.ExternalId!, cancellationToken)
                .ConfigureAwait(false);
        }

        var existingByNormalized = await uow.Artists
            .GetByNormalizedNameAsync(normalizedName, cancellationToken)
            .ConfigureAwait(false);

        var existing = existingByExternal ?? existingByNormalized;

        var action = existingByExternal is not null
            ? SeedArtistAction.ReusedByExternalReference
            : existingByNormalized is not null
                ? SeedArtistAction.ReusedByNormalizedName
                : SeedArtistAction.Created;

        var artistId = ResolveArtistId(request, existing);
        var now = DateTimeOffset.UtcNow;

        var aliases = BuildAliases(existing, request.Name, normalizedName);
        var refs = BuildExternalReferences(request.Source, request.ExternalId, now);

        var artist = new Artist
        {
            Id = artistId,
            Name = existing?.Name ?? request.Name,
            NormalizedName = existing?.NormalizedName ?? normalizedName,
            SortName = existing?.SortName ?? request.SortName,
            SourcePayloadJson = existing?.SourcePayloadJson,
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
            Aliases = aliases,
            ExternalReferences = refs,
            Relationships = existing?.Relationships ?? Array.Empty<EntityRelationship>(),
        };

        await uow.Artists.UpsertAsync(artist, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SeedArtistResult(artistId, normalizedName, action);
    }

    public async Task<SeedReleaseResult> SeedReleaseAsync(SeedReleaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);

        var normalizedTitle = EntityNameNormalizer.NormalizeStrict(request.Title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            throw new ArgumentException("Release title cannot normalize to an empty key.");

        var provider = new SqliteStorageProvider(request.DatabasePath);
        await using var uow = await provider.BeginUnitOfWorkAsync(cancellationToken).ConfigureAwait(false);

        Label? label = null;
        if (!string.IsNullOrWhiteSpace(request.LabelId))
        {
            label = await uow.Labels.GetByIdAsync(request.LabelId!, cancellationToken).ConfigureAwait(false);
            if (label is null)
                throw new ArgumentException($"Label '{request.LabelId}' was not found.");
        }

        Artist? artist = null;
        if (!string.IsNullOrWhiteSpace(request.ArtistId))
        {
            artist = await uow.Artists.GetByIdAsync(request.ArtistId!, cancellationToken).ConfigureAwait(false);
            if (artist is null)
                throw new ArgumentException($"Artist '{request.ArtistId}' was not found.");
            if (string.IsNullOrWhiteSpace(artist.Name))
                throw new ArgumentException($"Artist '{request.ArtistId}' does not have a display name for a release credit.");
        }

        Release? existingByExternal = null;
        if (!string.IsNullOrWhiteSpace(request.Source) && !string.IsNullOrWhiteSpace(request.ExternalId))
        {
            existingByExternal = await uow.Releases
                .GetByExternalRefAsync(request.Source!, request.ExternalId!, cancellationToken)
                .ConfigureAwait(false);
        }

        var existingByLabel = existingByExternal is null && label is not null
            ? await uow.Releases.GetByNormalizedTitleAndLabelAsync(normalizedTitle, label.Id, cancellationToken).ConfigureAwait(false)
            : null;

        var existingByTitle = existingByExternal is null && existingByLabel is null
            ? await uow.Releases.GetByNormalizedTitleAndLabelAsync(normalizedTitle, null, cancellationToken).ConfigureAwait(false)
            : null;

        var existing = existingByExternal ?? existingByLabel ?? existingByTitle;

        var action = existingByExternal is not null
            ? SeedReleaseAction.ReusedByExternalReference
            : existingByLabel is not null
                ? SeedReleaseAction.ReusedByNormalizedTitleAndLabel
                : existingByTitle is not null
                    ? SeedReleaseAction.ReusedByNormalizedTitle
                    : SeedReleaseAction.Created;

        var releaseId = ResolveReleaseId(request, existing);
        var now = DateTimeOffset.UtcNow;

        var aliases = BuildAliases(existing, request.Title, normalizedTitle);
        var refs = BuildExternalReferences(request.Source, request.ExternalId, now);
        var labelLinks = BuildReleaseLabelLinks(label);
        var artistCredits = BuildReleaseArtistCredits(artist);

        var release = new Release
        {
            Id = releaseId,
            Name = existing?.Name ?? request.Title,
            NormalizedName = existing?.NormalizedName ?? normalizedTitle,
            Title = existing?.Title ?? request.Title,
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
            Aliases = aliases,
            ExternalReferences = refs,
            ArtistCredits = artistCredits,
            LabelLinks = labelLinks,
        };

        await uow.Releases.UpsertAsync(release, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SeedReleaseResult(releaseId, normalizedTitle, action);
    }

    public async Task<SeedRecordingResult> SeedRecordingAsync(SeedRecordingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);

        var normalizedTitle = EntityNameNormalizer.NormalizeStrict(request.Title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            throw new ArgumentException("Recording title cannot normalize to an empty key.");

        var normalizedMixName = string.IsNullOrWhiteSpace(request.MixName)
            ? null
            : EntityNameNormalizer.NormalizeStrict(request.MixName);
        if (!string.IsNullOrWhiteSpace(request.MixName) && string.IsNullOrWhiteSpace(normalizedMixName))
            throw new ArgumentException("Recording mix name cannot normalize to an empty key.");

        var provider = new SqliteStorageProvider(request.DatabasePath);
        await using var uow = await provider.BeginUnitOfWorkAsync(cancellationToken).ConfigureAwait(false);

        Release? release = null;
        if (!string.IsNullOrWhiteSpace(request.ReleaseId))
        {
            release = await uow.Releases.GetByIdAsync(request.ReleaseId!, cancellationToken).ConfigureAwait(false);
            if (release is null)
                throw new ArgumentException($"Release '{request.ReleaseId}' was not found.");
        }

        Artist? artist = null;
        if (!string.IsNullOrWhiteSpace(request.ArtistId))
        {
            artist = await uow.Artists.GetByIdAsync(request.ArtistId!, cancellationToken).ConfigureAwait(false);
            if (artist is null)
                throw new ArgumentException($"Artist '{request.ArtistId}' was not found.");
            if (string.IsNullOrWhiteSpace(artist.Name))
                throw new ArgumentException($"Artist '{request.ArtistId}' does not have a display name for a recording credit.");
        }

        Recording? relatedRecording = null;
        if (!string.IsNullOrWhiteSpace(request.RelatedRecordingId))
        {
            relatedRecording = await uow.Recordings.GetByIdAsync(request.RelatedRecordingId!, cancellationToken).ConfigureAwait(false);
            if (relatedRecording is null)
                throw new ArgumentException($"Related recording '{request.RelatedRecordingId}' was not found.");
        }

        Recording? existingByExternal = null;
        if (!string.IsNullOrWhiteSpace(request.Source) && !string.IsNullOrWhiteSpace(request.ExternalId))
        {
            existingByExternal = await uow.Recordings
                .GetByExternalRefAsync(request.Source!, request.ExternalId!, cancellationToken)
                .ConfigureAwait(false);
        }

        Recording? existingByIsrc = null;
        if (existingByExternal is null && !string.IsNullOrWhiteSpace(request.Isrc))
        {
            existingByIsrc = await uow.Recordings.GetByIsrcAsync(request.Isrc!, cancellationToken).ConfigureAwait(false);
        }

        Recording? existingByNormalizedTitleAndMix = null;
        if (existingByExternal is null && existingByIsrc is null)
        {
            existingByNormalizedTitleAndMix = await uow.Recordings
                .GetByNormalizedTitleAndMixNameAsync(normalizedTitle, normalizedMixName, cancellationToken)
                .ConfigureAwait(false);
        }

        Recording? existingByTitle = null;
        if (existingByExternal is null && existingByIsrc is null && existingByNormalizedTitleAndMix is null)
        {
            existingByTitle = await uow.Recordings
                .GetByNormalizedTitleAndMixNameAsync(normalizedTitle, null, cancellationToken)
                .ConfigureAwait(false);
        }

        var existing = existingByExternal ?? existingByIsrc ?? existingByNormalizedTitleAndMix ?? existingByTitle;

        var action = existingByExternal is not null
            ? SeedRecordingAction.ReusedByExternalReference
            : existingByIsrc is not null
                ? SeedRecordingAction.ReusedByIsrc
                : existingByNormalizedTitleAndMix is not null
                    ? SeedRecordingAction.ReusedByNormalizedTitleAndMixName
                    : existingByTitle is not null
                        ? SeedRecordingAction.ReusedByNormalizedTitle
                        : SeedRecordingAction.Created;

        var recordingId = ResolveRecordingId(request, existing);
        var now = DateTimeOffset.UtcNow;

        var aliases = BuildAliases(existing, request.Title, normalizedTitle);
        var refs = BuildExternalReferences(request.Source, request.ExternalId, now);
        var artistCredits = BuildRecordingArtistCredits(artist, request.ArtistRole);
        var releaseLinks = BuildRecordingReleaseLinks(release, request.DiscNumber, request.TrackNumber);
        var relationships = BuildRecordingRelationships(relatedRecording, request.RelationshipType, request.RelationshipSource, request.RelationshipConfidence, request.RelationshipNotes, now);

        var recording = new Recording
        {
            Id = recordingId,
            Name = existing?.Name ?? request.Title,
            NormalizedName = existing?.NormalizedName ?? normalizedTitle,
            Title = existing?.Title ?? request.Title,
            MixName = existing?.MixName ?? normalizedMixName,
            Isrc = existing?.Isrc ?? request.Isrc,
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
            Aliases = aliases,
            ExternalReferences = refs,
            ArtistCredits = artistCredits,
            ReleaseLinks = releaseLinks,
            Relationships = relationships,
        };

        await uow.Recordings.UpsertAsync(recording, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SeedRecordingResult(recordingId, normalizedTitle, normalizedMixName, action);
    }

    private static string ResolveLabelId(SeedLabelRequest request, Label? existing)
    {
        if (existing is not null)
            return existing.Id;

        if (!string.IsNullOrWhiteSpace(request.LabelId))
            return request.LabelId!;

        return Guid.NewGuid().ToString("D").ToLowerInvariant();
    }

    private static string ResolveArtistId(SeedArtistRequest request, Artist? existing)
    {
        if (existing is not null)
            return existing.Id;

        if (!string.IsNullOrWhiteSpace(request.ArtistId))
            return request.ArtistId!;

        return Guid.NewGuid().ToString("D").ToLowerInvariant();
    }

    private static string ResolveReleaseId(SeedReleaseRequest request, Release? existing)
    {
        if (existing is not null)
            return existing.Id;

        if (!string.IsNullOrWhiteSpace(request.ReleaseId))
            return request.ReleaseId!;

        return Guid.NewGuid().ToString("D").ToLowerInvariant();
    }

    private static string ResolveRecordingId(SeedRecordingRequest request, Recording? existing)
    {
        if (existing is not null)
            return existing.Id;

        if (!string.IsNullOrWhiteSpace(request.RecordingId))
            return request.RecordingId!;

        return Guid.NewGuid().ToString("D").ToLowerInvariant();
    }

    private static IReadOnlyList<EntityAlias> BuildAliases(CanonicalEntity? existing, string inputName, string normalizedName)
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

    private static IReadOnlyList<ReleaseLabelLink> BuildReleaseLabelLinks(Label? label)
    {
        if (label is null)
            return Array.Empty<ReleaseLabelLink>();

        return
        [
            new ReleaseLabelLink
            {
                LabelId = label.Id,
                IsPrimary = true,
                Role = "primary",
            },
        ];
    }

    private static IReadOnlyList<ReleaseArtistCredit> BuildReleaseArtistCredits(Artist? artist)
    {
        if (artist is null)
            return Array.Empty<ReleaseArtistCredit>();

        return
        [
            new ReleaseArtistCredit
            {
                ArtistId = artist.Id,
                CreditName = artist.Name,
                Position = 0,
            },
        ];
    }

    private static IReadOnlyList<RecordingArtistCredit> BuildRecordingArtistCredits(Artist? artist, string? role)
    {
        if (artist is null)
            return Array.Empty<RecordingArtistCredit>();

        return
        [
            new RecordingArtistCredit
            {
                ArtistId = artist.Id,
                CreditName = artist.Name,
                Role = string.IsNullOrWhiteSpace(role) ? "primary" : role,
                Position = 0,
            },
        ];
    }

    private static IReadOnlyList<RecordingReleaseLink> BuildRecordingReleaseLinks(Release? release, int? discNumber, int? trackNumber)
    {
        if (release is null)
            return Array.Empty<RecordingReleaseLink>();

        return
        [
            new RecordingReleaseLink
            {
                ReleaseId = release.Id,
                DiscNumber = discNumber,
                TrackNumber = trackNumber,
            },
        ];
    }

    private static IReadOnlyList<RecordingRelationship> BuildRecordingRelationships(
        Recording? relatedRecording,
        string? relationshipType,
        string? source,
        decimal? confidence,
        string? notes,
        DateTimeOffset now)
    {
        if (relatedRecording is null)
            return Array.Empty<RecordingRelationship>();

        return
        [
            new RecordingRelationship
            {
                RelatedRecordingId = relatedRecording.Id,
                RelationshipType = string.IsNullOrWhiteSpace(relationshipType) ? "remix_of" : relationshipType,
                Source = source,
                Confidence = confidence,
                Notes = notes,
                CreatedUtc = now,
                UpdatedUtc = now,
            },
        ];
    }

    private static IReadOnlyList<EntityReference> BuildExternalReferences(string? source, string? externalId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(externalId))
            return Array.Empty<EntityReference>();

        return
        [
            new EntityReference
            {
                Source = source!,
                ExternalId = externalId!,
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
