using Kumi.Game.Charts;
using Kumi.Game.IO;
using osu.Framework.Allocation;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using Realms;

namespace Kumi.Game.Database;

public class RealmAccess : IDisposable
{
    /// <summary>
    /// Version history:
    /// 1    2023-10-28   Initial version.
    /// 2    2023-12-07   Rename RealmUser to RealmAccount.
    /// 3    2023-12-08   Add more properties to ScoreInfo.
    /// </summary>
    private const int schema_version = 3;

    public string FileName { get; }

    private Realm? updateRealm;

    public Realm Realm
    {
        get
        {
            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException("Realm can only be accessed from the update thread.");

            if (updateRealm == null)
            {
                updateRealm = getInstance();
                
                // Apply subscriptions to the update realm if any.
                foreach (var action in subscriptions.Keys.ToArray())
                    registerSubscription(action);
            }

            return updateRealm;
        }
    }


    private RealmConfiguration config => new RealmConfiguration(storage.GetFullPath(FileName))
    {
        SchemaVersion = schema_version,
        MigrationCallback = onMigrate,
    };

    public RealmAccess(Storage storage, string fileName = "kumi.realm", GameThread thread = null)
    {
        this.storage = storage;
        FileName = fileName;

        realmSyncContext = thread.SynchronizationContext ?? SynchronizationContext.Current;

        cleanupPendingDeletions();
    }
    
    private readonly Storage storage;
    private readonly SynchronizationContext? realmSyncContext;
    private readonly Dictionary<Func<Realm, IDisposable>, IDisposable> subscriptions = new Dictionary<Func<Realm, IDisposable>, IDisposable>();

    public T Run<T>(Func<Realm, T> action)
    {
        if (ThreadSafety.IsUpdateThread)
            return action(Realm);

        using var r = getInstance();
        return action(r);
    }

    public void Run(Action<Realm> action)
    {
        if (ThreadSafety.IsUpdateThread)
        {
            action(Realm);
        }
        else
        {
            using var r = getInstance();
            action(r);
        }
    }

    public T Write<T>(Func<Realm, T> action)
    {
        if (ThreadSafety.IsUpdateThread)
            return write(Realm, action);

        using var r = getInstance();
        return write(r, action);
    }

    public void Write(Action<Realm> action)
    {
        if (ThreadSafety.IsUpdateThread)
        {
            write(Realm, action);
        }
        else
        {
            using var r = getInstance();
            write(r, action);
        }
    }

    /// <summary>
    /// Subscribes to notifications for all objects of type <typeparamref name="TModel" /> that match the query.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <typeparam name="TModel">The model.</typeparam>
    public IDisposable Subscribe<TModel>(NotificationCallbackDelegate<TModel> action)
        where TModel : RealmObject
        => Subscribe((r) => r.All<TModel>(), action);

    /// <summary>
    /// Subscribes to notifications for all objects of type <typeparamref name="TModel" /> that match the query.
    /// </summary>
    /// <param name="query">The query to use.</param>
    /// <param name="action">The action to execute.</param>
    /// <typeparam name="TModel">The model.</typeparam>
    public IDisposable Subscribe<TModel>(Func<Realm, IQueryable<TModel>> query, NotificationCallbackDelegate<TModel> callback)
        where TModel : RealmObject
    {
        // We need to keep a reference to the subscription object to prevent it from being garbage collected.
        // And to dispose it when we're done with it.
        Func<Realm, IDisposable> action = r => query(r).SubscribeForNotifications(callback);
        
        handleActionSafely(() => registerSubscription(action));

        return new InvokeOnDisposal(() =>
        {
            // We'll need to remove the subscription from the dictionary before disposing it, otherwise we'll get an exception.
            void removeSubscription()
            {
                if (subscriptions.TryGetValue(action, out var subscription))
                {
                    subscription.Dispose();
                    subscriptions.Remove(action);
                }
            }

            handleActionSafely(removeSubscription);
        });
    }

    private void registerSubscription(Func<Realm, IDisposable> action)
    {
        subscriptions[action] = action(Realm);
    }

    public bool Compact() => Realm.Compact(config);

    private void onMigrate(Migration migration, ulong oldSchemaVersion)
    {
        for (var i = oldSchemaVersion + 1; i <= schema_version; i++)
            migrateTo(migration, i);
    }

    private void migrateTo(Migration migration, ulong targetSchemaVersion)
    {
        switch (targetSchemaVersion)
        {
        }
    }

    private static T write<T>(Realm realm, Func<Realm, T> func)
    {
        Transaction? transaction = null;

        try
        {
            if (!realm.IsInTransaction)
                transaction = realm.BeginWrite();

            var result = func(realm);
            transaction?.Commit();
            return result;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static void write(Realm realm, Action<Realm> action)
    {
        Transaction? transaction = null;

        try
        {
            if (!realm.IsInTransaction)
                transaction = realm.BeginWrite();

            action(realm);
            transaction?.Commit();
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private Realm getInstance()
        => Realm.GetInstance(config);
    
    private void handleActionSafely(Action action)
    {
        if (ThreadSafety.IsUpdateThread)
        {
            realmSyncContext.Send(_ => action(), null);
        }
        else
        {
            realmSyncContext.Post(_ => action(), null);
        }
    }
    
    private void cleanupPendingDeletions()
    {
        try
        {
            using (var realm = getInstance())
            using (var transaction = realm.BeginWrite())
            {
                var pendingDeletionCharts = realm.All<ChartSetInfo>().Where(c => c.DeletePending);

                foreach (var c in pendingDeletionCharts)
                    realm.Remove(c);

                transaction.Commit();
            }

            new UserDataStorage(this, storage).Cleanup();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to clean up unused files. This may be due to the database schema being too old.");
        }
    }

    public void Dispose()
    {
        updateRealm?.Dispose();
        GC.SuppressFinalize(this);
    }
}
