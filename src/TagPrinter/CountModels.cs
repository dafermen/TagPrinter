using System.Globalization;
namespace TagPrinter;

/// <summary>One local physical count. Location and expected scope are frozen at creation; terminal sessions are read-only.</summary>
internal sealed record CountSession(long Id,int Revision,string Name,string Location,string Status,long ExpectedCount,string CreatedUtc,string UpdatedUtc,string ClosedUtc,string Notes)
{
    /// <summary>Stable local reference, distinct from asset and service codes.</summary>
    public string Code=>"PC-"+Id.ToString("D8",CultureInfo.InvariantCulture);
}
/// <summary>Frozen asset evidence plus the latest effective reading. Unexpected withdrawn rows remain available to audit.</summary>
internal sealed record CountEntry(long Id,long SessionId,string? AssetUid,bool Expected,AssetRecord? Snapshot,string LookupKey,bool Observed,string ObservedUtc,string InputMode,string InputValue);
/// <summary>A durable result returned after the reading and audit commit together.</summary>
internal sealed record CountScan(CountSession Session,CountEntry Entry,string Result,bool Duplicate);
/// <summary>Per-session totals; filters/paging never change the reconciliation denominator.</summary>
internal sealed record CountTotals(long Expected,long Found,long Pending,long WrongLocation,long Unexpected,long Retired,long Unknown,long Readings,long Duplicates);
/// <summary>Read-only result projection with its derived outcome.</summary>
internal sealed record CountRow(CountEntry Entry,string Outcome);
/// <summary>One consistent snapshot of session, totals, clamped page and bounded results.</summary>
internal sealed record CountPage(CountSession Session,CountTotals Totals,long Total,int Page,List<CountRow> Rows);
/// <summary>Append-only action evidence. Resolution captures the asset used to match an editable current serial.</summary>
internal sealed record CountAction(long Id,int Revision,string AtUtc,string Action,long? EntryId,string InputMode,string InputValue,string Result,string? ResolutionJson,string Note);
