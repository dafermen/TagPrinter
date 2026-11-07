using Microsoft.Data.Sqlite;
namespace TagPrinter;

/// <summary>Rebuildable performance indexes. User schema/snapshots stay compatible with version 3.</summary>
internal static class DatabasePerformance
{
    internal const string InventoryEvent="COALESCE(json_extract(state_json,'$.FreeTextMode'),0)=0";
    /// <summary>Creates expression/partial indexes with expressions matching the actual WHERE/ORDER BY clauses.</summary>
    internal static void Initialize(SqliteConnection connection)
    {
        using(var check=connection.CreateCommand())
        {
            check.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN('ix_perf_events_inventory','ix_perf_events_date','ix_perf_events_action','ix_perf_events_serial','ix_perf_assets_updated','ix_perf_assets_status','ix_perf_assets_location','ix_perf_assets_custodian','ix_perf_assets_description','ix_perf_assets_groups','ix_perf_catalog_prefix')";
            if(Convert.ToInt32(check.ExecuteScalar())==11)return;
        }
        using var transaction=connection.BeginTransaction();using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText=$"""
            CREATE INDEX IF NOT EXISTS ix_perf_events_inventory ON events(id DESC) WHERE {InventoryEvent};
            CREATE INDEX IF NOT EXISTS ix_perf_events_date ON events(created_utc,id DESC) WHERE {InventoryEvent};
            CREATE INDEX IF NOT EXISTS ix_perf_events_action ON events(action,id DESC) WHERE {InventoryEvent};
            CREATE INDEX IF NOT EXISTS ix_perf_events_serial ON events(trim(serial) COLLATE NOCASE,id DESC) WHERE {InventoryEvent};
            CREATE INDEX IF NOT EXISTS ix_perf_assets_updated ON assets(json_extract(record_json,'$.UpdatedUtc') DESC,id DESC);
            CREATE INDEX IF NOT EXISTS ix_perf_assets_status ON assets(json_extract(record_json,'$.Status'),json_extract(record_json,'$.UpdatedUtc') DESC,id DESC);
            CREATE INDEX IF NOT EXISTS ix_perf_assets_location ON assets(json_extract(record_json,'$.Location'),json_extract(record_json,'$.UpdatedUtc') DESC,id DESC);
            CREATE INDEX IF NOT EXISTS ix_perf_assets_custodian ON assets(json_extract(record_json,'$.Custodian'),json_extract(record_json,'$.UpdatedUtc') DESC,id DESC);
            CREATE INDEX IF NOT EXISTS ix_perf_assets_description ON assets(trim(json_extract(record_json,'$.AssetType')),trim(json_extract(record_json,'$.Brand')),trim(json_extract(record_json,'$.Model')),json_extract(record_json,'$.UpdatedUtc') DESC,id DESC);
            CREATE INDEX IF NOT EXISTS ix_perf_assets_groups ON assets(json_extract(record_json,'$.AssetType'),json_extract(record_json,'$.Brand'),json_extract(record_json,'$.Model')) WHERE json_extract(record_json,'$.Status')!='Retired';
            CREATE INDEX IF NOT EXISTS ix_perf_catalog_prefix ON asset_catalog(kind,value_key COLLATE NOCASE,name);
            """;cmd.ExecuteNonQuery();transaction.Commit();
    }
}
/// <summary>Owns a read snapshot and a native interrupt registration, disposed before the SQLite handle closes.</summary>
internal sealed class DatabaseRead : IDisposable
{
    internal SqliteConnection Connection {get;}
    internal SqliteTransaction Transaction {get;}
    private readonly CancellationTokenRegistration cancellation;
    /// <summary>Opens a worker-owned unpooled connection. Deferred read transactions preserve one WAL snapshot across count/page.</summary>
    private DatabaseRead(HistoryStore history,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();Connection=history.Open();
        try
        {
            Transaction=Connection.BeginTransaction(deferred:true);
            // SQLite permits interrupt from another thread while the handle is alive. Dispose registration before closing it.
            cancellation=token.Register(()=>SQLitePCL.raw.sqlite3_interrupt(Connection.Handle));token.ThrowIfCancellationRequested();
        }
        catch{cancellation.Dispose();Connection.Dispose();throw;}
    }
    /// <summary>Maps native SQLITE_INTERRUPT to cooperative cancellation for canceled UI/export requests.</summary>
    internal static T Run<T>(HistoryStore history,CancellationToken token,Func<DatabaseRead,T> read)
    {
        using var scope=new DatabaseRead(history,token);
        try{token.ThrowIfCancellationRequested();return read(scope);}
        catch(SqliteException)when(token.IsCancellationRequested){throw new OperationCanceledException(token);}
    }
    /// <summary>Creates a command participating in this exact read snapshot.</summary>
    internal SqliteCommand Command(){var command=Connection.CreateCommand();command.Transaction=Transaction;return command;}
    /// <summary>Stops callbacks while the handle is still valid, then releases transaction and connection.</summary>
    public void Dispose(){cancellation.Dispose();Transaction.Dispose();Connection.Dispose();}
}
/// <summary>A bounded page with its total and clamped page number from one database snapshot.</summary>
internal sealed record DatabasePage<T>(long Total,int Page,List<T> Rows);
