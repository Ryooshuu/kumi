using Kumi.Game.Extensions;
using Kumi.Game.IO;
using Kumi.Game.IO.Archives;
using Kumi.Game.Models;
using NuGet.Packaging;
using osu.Framework.Extensions;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using Realms;

namespace Kumi.Game.Database;

public abstract class RealmModelImporter<TModel> : IModelImporter<TModel>
    where TModel : RealmObject, IHasFiles, IHasGuidPrimaryKey, ISoftDelete
{
    private static readonly ThreadedTaskScheduler import_scheduler = new ThreadedTaskScheduler(1, nameof(RealmModelImporter<TModel>));

    protected readonly UserDataStorage Files;
    protected readonly RealmAccess Realm;

    protected RealmModelImporter(Storage storage, RealmAccess realm)
    {
        Realm = realm;
        Files = new UserDataStorage(realm, storage);
    }

    public Task Import(params string[] paths)
        => ImportModels(paths.Select(p => new ImportTask(p)).ToArray());

    public Task Import(ImportTask[] tasks)
        => ImportModels(tasks);

    public async Task<IEnumerable<TModel>> ImportModels(ImportTask[] tasks)
    {
        if (tasks.Length == 0)
            return Enumerable.Empty<TModel>();

        var imported = new List<TModel>();

        await Task.WhenAll(tasks.Select(async task =>
        {
            try
            {
                var model = await Import(task);

                lock (imported)
                {
                    if (model != null)
                        imported.Add(model);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled, so we don't care.
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Could not import ({task})", LoggingTarget.Database);
            }
        })).ConfigureAwait(false);

        return imported;
    }

    public async Task<TModel?> Import(ImportTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TModel? import;

        using (var reader = task.GetReader())
        {
            import = await importFromArchive(reader, cancellationToken).ConfigureAwait(false);
        }

        return import;
    }

    private async Task<TModel?> importFromArchive(ArchiveReader archive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TModel? model = null;

        try
        {
            model = CreateModel(archive);

            if (model == null)
                return null;
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Could not import ({archive.ArchivePath})", LoggingTarget.Database);
        }

        var scheduledImport = Task.Factory.StartNew(() => ImportModel(model!, archive, cancellationToken),
            cancellationToken,
            TaskCreationOptions.HideScheduler,
            import_scheduler);

        return await scheduledImport.ConfigureAwait(false);
    }

    public virtual TModel ImportModel(TModel item, ArchiveReader? archive = null, CancellationToken cancellationToken = default)
        => Realm.Run(realm =>
        {
            TModel? existing;

            if (archive != null)
            {
                // TODO: check if there's an existing item with the same hash.
            }

            try
            {
                Logger.Log($"Beginning import from {archive?.ArchivePath ?? "unknown"}...", LoggingTarget.Database);

                var files = new List<RealmNamedFileUsage>();

                if (archive != null)
                    foreach (var filenames in getShortenedFilenames(archive))
                        using (var s = archive.GetStream(filenames.original)!)
                        {
                            files.Add(new RealmNamedFileUsage(Files.Add(s, realm, false), filenames.shortened));
                        }

                using (var transaction = realm.BeginWrite())
                {
                    foreach (var file in files)
                        if (!file.File.IsManaged)
                            realm.Add(file.File, true);

                    transaction.Commit();
                }

                item.Files.AddRange(files);
                item.Hash = ComputeHash(item);

                using (var transaction = realm.BeginWrite())
                {
                    Populate(item, archive, realm, cancellationToken);
                    PreImport(item, realm);
                    realm.Add(item);
                    PostImport(item, realm);

                    transaction.Commit();
                }

                Logger.Log("Import successfully completed!", LoggingTarget.Database);
            }
            catch (Exception e)
            {
                if (!(e is TaskCanceledException))
                    Logger.Error(e, "Database import or population failed.", LoggingTarget.Database);

                throw;
            }

            realm.Refresh();
            return item.Detach();
        });

    protected abstract string[] HashableFileTypes { get; }

    public string ComputeHash(TModel item)
    {
        var hashable = new MemoryStream();

        foreach (var file in item.Files.Where(f => HashableFileTypes.Any(ext => f.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).OrderBy(f => f.FileName))
            using (var s = Files.Store.GetStream(file.File.GetStoragePath()))
            {
                s.CopyTo(hashable);
            }

        if (hashable.Length > 0)
            return hashable.ComputeSHA2Hash();

        return item.Hash;
    }

    private IEnumerable<(string original, string shortened)> getShortenedFilenames(ArchiveReader reader)
    {
        var prefix = reader.FileNames.GetCommonPrefix();
        if (!(prefix.EndsWith('/') || prefix.EndsWith('\\')))
            prefix = string.Empty;

        foreach (var file in reader.FileNames)
            yield return (file, file.Substring(prefix.Length).ToStandardisedPath());
    }

    /// <summary>
    /// Create a bare-bones model from the provided archive.
    /// </summary>
    /// <param name="archive">The archive to create the model with.</param>
    /// <returns>A model populated with minimal information</returns>
    protected abstract TModel? CreateModel(ArchiveReader archive);

    /// <summary>
    /// Populate the provided model completely from the provided archive.
    /// After this method is called, the model should be ready to be inserted into the database.
    /// </summary>
    /// <param name="model">The model to populate.</param>
    /// <param name="archive">The archive to use as a reference for population.</param>
    /// <param name="realm">The current realm context.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    protected abstract void Populate(TModel model, ArchiveReader? archive, Realm realm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform any actions before the import is added to the database.
    /// </summary>
    /// <param name="model">The model prepared for import.</param>
    /// <param name="realm">The current realm context.</param>
    protected virtual void PreImport(TModel model, Realm realm)
    {
    }

    /// <summary>
    /// Perform any actions before the import has been committed to the database.
    /// </summary>
    /// <param name="model">The model prepared for import.</param>
    /// <param name="realm">The current realm context.</param>
    protected virtual void PostImport(TModel model, Realm realm)
    {
    }

    public abstract IEnumerable<string> HandledFileExtensions { get; }
}
