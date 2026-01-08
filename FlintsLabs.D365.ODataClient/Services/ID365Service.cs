using System.Linq.Expressions;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Main interface for D365 OData service with fluent query builder
/// </summary>
public interface ID365Service
{
    /// <summary>
    /// Start a query for the specified entity (non-generic)
    /// </summary>
    D365Service Entity(string entity);
    
    /// <summary>
    /// Start a query for the specified entity using user-defined enum.
    /// Uses [Description] attribute if present, otherwise uses enum name.
    /// </summary>
    /// <example>
    /// <code>
    /// // Define your enum
    /// public enum MyEntities
    /// {
    ///     [Description("CustomersV3")]
    ///     Customer,
    ///     
    ///     LegalEntities  // Uses "LegalEntities" as entity name
    /// }
    /// 
    /// // Usage
    /// d365.Entity(MyEntities.Customer).ToListAsync();
    /// </code>
    /// </example>
    D365Service Entity(Enum entity);
    
    /// <summary>
    /// Start a strongly-typed query for the specified entity
    /// </summary>
    D365Query<T> Entity<T>(string entity);
    
    /// <summary>
    /// Start a strongly-typed query using user-defined enum.
    /// Uses [Description] attribute if present, otherwise uses enum name.
    /// </summary>
    D365Query<T> Entity<T>(Enum entity);
    
    /// <summary>
    /// Add OData filter criteria (raw string)
    /// </summary>
    D365Service Where(string criteria);
    
    /// <summary>
    /// Select specific columns
    /// </summary>
    D365Service Select(string[] selectColumns);
    
    /// <summary>
    /// Expand navigation properties
    /// </summary>
    D365Service Expand(params string[] expandColumns);
    
    /// <summary>
    /// Add entity key identity for single-record operations
    /// </summary>
    D365Service AddIdentity(string property, string value);
    
    /// <summary>
    /// Add entity key identity for single-record operations
    /// </summary>
    D365Service AddIdentity(string property, object value);
    
    /// <summary>
    /// Add multiple entity key identities
    /// </summary>
    D365Service AddIdentities(Dictionary<string, object> identities);

    /// <summary>
    /// Execute query and return first record or null
    /// </summary>
    Task<T?> FirstOrDefaultAsync<T>();
    
    /// <summary>
    /// Execute query and return list of records
    /// </summary>
    Task<List<T>?> ToListAsync<T>();
    
    /// <summary>
    /// Execute query and transform results
    /// </summary>
    Task<List<T1>?> ToListAsync<T, T1>(Func<T, T1> factory);

    /// <summary>
    /// Create a new record
    /// </summary>
    Task<string> AddAsync<T>(T obj);
    
    /// <summary>
    /// Create a new record and parse response
    /// </summary>
    Task<T1?> AddAndParseObject<T, T1>(T obj);
    
    /// <summary>
    /// Delete a record
    /// </summary>
    Task<string> DeleteAsync();
    
    /// <summary>
    /// Update a record
    /// </summary>
    Task<string> UpdateAsync<T>(T obj);

    /// <summary>
    /// Try to parse D365 error message from JSON response
    /// </summary>
    (bool IsError, string Message) TryParseD365ErrorMessage(string jsonMessage);
}
