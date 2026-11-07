using System.Globalization;
namespace TagPrinter;

/// <summary>One maintenance job or warranty, linked to a stable asset identity rather than its editable serial.</summary>
internal sealed record ServiceRecord
{
    /// <summary>Database identity. Zero designates a new, unsaved record.</summary>
    public long Id {get;init;}
    /// <summary>Optimistic version; an outdated editor cannot overwrite a newer save.</summary>
    public int Revision {get;init;}
    /// <summary>Canonical asset GUID. It cannot be changed after creation.</summary>
    public string AssetUid {get;init;}="";
    /// <summary>Stable Maintenance or Warranty key, independent of interface language.</summary>
    public string Kind {get;init;}="Maintenance";
    /// <summary>Short description of the job or coverage.</summary>
    public string Title {get;init;}="";
    /// <summary>Preventive/Corrective for jobs; empty for warranties.</summary>
    public string Category {get;init;}="Preventive";
    /// <summary>Workflow key. Deadlines are calculated separately and never silently change this value.</summary>
    public string Status {get;init;}="Planned";
    /// <summary>ISO calendar date: actual job start (optional) or warranty start (required).</summary>
    public string StartDate {get;init;}="";
    /// <summary>ISO calendar date: planned job date or inclusive warranty expiration date.</summary>
    public string DueDate {get;init;}="";
    /// <summary>Actual job completion date; required only for Completed maintenance.</summary>
    public string CompletedDate {get;init;}="";
    /// <summary>Service company, manufacturer or warranty provider.</summary>
    public string Provider {get;init;}="";
    /// <summary>Invoice, contract, case or service reference.</summary>
    public string Reference {get;init;}="";
    /// <summary>Optional recorded/estimated maintenance amount; null is distinct from zero.</summary>
    public decimal? Cost {get;init;}
    /// <summary>Explicit three-letter currency code when an amount is supplied. No conversion is performed.</summary>
    public string Currency {get;init;}="";
    /// <summary>Work performed, result, coverage details or cancellation reason.</summary>
    public string Notes {get;init;}="";
    /// <summary>Immutable UTC creation instant.</summary>
    public string CreatedUtc {get;init;}="";
    /// <summary>UTC instant of this revision.</summary>
    public string UpdatedUtc {get;init;}="";
    /// <summary>Stable human-readable local reference.</summary>
    public string Code=>(Kind=="Warranty"?"WT-":"MT-")+Id.ToString("D8",CultureInfo.InvariantCulture);
}
/// <summary>One bounded list row; current asset captions are read through the stable link.</summary>
internal sealed record ServiceRow(ServiceRecord Record,string AssetCode,string AssetType,string Brand,string Model,string Serial,string AssetStatus);
/// <summary>Filter values captured on the UI thread before a worker opens its read snapshot.</summary>
internal sealed record ServiceFilter(string Kind,string Search="",string Status="",string Deadline="All",int Days=30,string? AssetUid=null,bool IncludeRetired=false);
/// <summary>A page and alert totals from the same filtered read snapshot. Totals ignore only the deadline selector.</summary>
internal sealed record ServicePage(long Total,int Page,List<ServiceRow> Rows,long Late,long Upcoming);
/// <summary>Immutable before/after revision. No application account is needed to keep a local change history.</summary>
internal sealed record ServiceChange(long Id,string AtUtc,ServiceRecord? Before,ServiceRecord After);
