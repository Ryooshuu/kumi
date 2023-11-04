﻿using Realms;

namespace Kumi.Game.Database;

/// <summary>
/// A class backed by realm that handles certain objects with assignable default values.
/// </summary>
/// <typeparam name="TModel">The model.</typeparam>
public abstract class RealmBackedDefaultStore<TModel> : IRealmBackedDefaultStore<TModel>
    where TModel : RealmObject, IHasGuidPrimaryKey
{
    public IEnumerable<TModel> DefaultValues { get; set; } = new List<TModel>();

    public RealmBackedDefaultStore(RealmAccess realmAccess)
    {
        realm = realmAccess;
    }
    
    private readonly RealmAccess realm;
    
    public TModel? Get(Func<TModel, bool> query) => realm.Run(r => r.All<TModel>().First(query));
    
    public IEnumerable<TModel> GetAll() => realm.Run(r => r.All<TModel>().ToList());
    
    public void Write(Func<TModel, bool> query, Action<TModel> action)
    {
        realm.Write(r =>
        {
            var item = r.All<TModel>().First(query);

            if (item == null)
                return;
            
            action(item);
        });
    }
    
    public void RegisterDefaults()
    {
        realm.Write(r =>
        {
            AssignDefaults();
            var existing = r.All<TModel>().ToList();

            foreach (var item in DefaultValues)
            {
                if (existing.Any(e => Compare(e, item)))
                {
                    continue;
                }

                r.Add(item);
            }
        });
    }
    public abstract void AssignDefaults();
    
    /// <summary>
    /// Gets default values from an instance of <see cref="IStoreDefaults{TModel}"/>.
    /// </summary>
    /// <param name="defaults">The instance.</param>
    public void AssignDefaultsFor(IStoreDefaults<TModel> defaults)
    {
        foreach (var item in defaults.GetDefaultValues())
        {
            DefaultValues.Append(item);
        }
    }
    
    
    public virtual bool Compare(TModel model, TModel other)
    {
        return model == other;
    }

    public void Reset()
    {
        realm.Write(r =>
        {
            foreach (var item in r.All<TModel>())
            {
                r.Remove(item);
            }
        });
        
        RegisterDefaults();
    }
}
