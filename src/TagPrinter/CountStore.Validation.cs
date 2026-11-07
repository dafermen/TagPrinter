using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace TagPrinter;

internal sealed partial class CountStore
{
    /// <summary>Checks normalized count evidence and complete action/state consistency before a staged restore can replace live data.</summary>
    internal static void ValidateDatabase(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();
        void Bad(string message)=>throw new InvalidDataException(message);
        void Utc(string value){if(!DateTimeOffset.TryParseExact(value,"O",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time) || time.Offset!=TimeSpan.Zero)Bad("Invalid count UTC timestamp.");}
        void Asset(AssetRecord a){AssetStore.Validate(a);if(a.Number<1 || a.Revision<1 || !Guid.TryParseExact(a.Uid,"N",out _) || !DateTimeOffset.TryParse(a.CreatedUtc,CultureInfo.InvariantCulture,out _) || !DateTimeOffset.TryParse(a.UpdatedUtc,CultureInfo.InvariantCulture,out _))Bad("Invalid frozen asset metadata.");}
        cmd.CommandText="SELECT "+SessionColumns+" FROM count_sessions";using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var s=Session(reader);Text(s.Name,160,true);Text(s.Location,160,true);Text(s.Notes,2000);Utc(s.CreatedUtc);Utc(s.UpdatedUtc);if(s.Id<1 || s.Revision<1 || s.ExpectedCount<0 || s.Name!=s.Name.Trim() || s.Location!=s.Location.Trim() || s.Status is not ("Open" or "Closed" or "Canceled"))Bad("Invalid count session.");if(s.Status=="Open"){if(s.ClosedUtc!="")Bad("An open count has a closure timestamp.");}else Utc(s.ClosedUtc);
        }
        cmd.CommandText="SELECT "+SelectedEntry+",s.location,s.status"+Joined;using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var e=Entry(reader);if(e.Id<1 || e.SessionId<1)Bad("Invalid count entry identity.");
            if(e.AssetUid is null){if(e.Expected || e.Snapshot is not null || e.InputMode is not ("Serial" or "AssetCode") || string.IsNullOrWhiteSpace(e.LookupKey) || e.LookupKey!=AssetCatalog.Key(e.LookupKey))Bad("Invalid unresolved reading.");Token(e.InputMode,e.LookupKey);}
            else
            {
                if(e.Snapshot is not {} a)throw new InvalidDataException("Missing frozen asset.");Asset(a);if(a.Uid!=e.AssetUid || e.LookupKey!=AssetCatalog.Key(a.Serial))Bad("Count asset metadata mismatch.");if(e.Expected && (a.Location!=reader.GetString(10) || a.Status=="Retired"))Bad("Invalid expected count scope.");
            }
            if(e.Observed){Utc(e.ObservedUtc);Token(e.InputMode,e.InputValue);if(e.AssetUid is null && AssetCatalog.Key(e.InputValue)!=e.LookupKey)Bad("Unresolved key mismatch.");}
            else if(e.ObservedUtc!="" || e.InputValue!="" || (e.AssetUid is not null && e.InputMode!=""))Bad("Unobserved entry retains effective reading data.");
        }
        // Ordered streaming keeps only one session/action revision at a time, rather than a global audit dictionary.
        cmd.CommandText="SELECT a.session_id,a.revision,a.at_utc,a.action,a.entry_id,a.input_mode,a.input_value,a.result,a.resolution_json,a.note,"+string.Join(',',SessionColumns.Split(',').Select(k=>"s."+k))+",e.id,e.session_id,e.asset_uid,e.expected,e.snapshot_json,e.lookup_key,e.observed,e.observed_utc,e.input_mode,e.input_value FROM count_actions a JOIN count_sessions s ON s.id=a.session_id LEFT JOIN count_entries e ON e.id=a.entry_id ORDER BY a.session_id,a.revision";
        long lastSession=0;int revision=0;string state="Open",lastAt="";CountSession? session=null;
        void End(){if(session is not null && (revision!=session.Revision || state!=session.Status || lastAt!=session.UpdatedUtc || (state!="Open" && session.ClosedUtc!=lastAt)))Bad("Count session/action final state mismatch.");}
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            long id=reader.GetInt64(0);if(id!=lastSession){End();session=new(reader.GetInt64(10),reader.GetInt32(11),reader.GetString(12),reader.GetString(13),reader.GetString(14),reader.GetInt64(15),reader.GetString(16),reader.GetString(17),reader.GetString(18),reader.GetString(19));lastSession=id;revision=0;state="Open";lastAt="";}
            string action=reader.GetString(3),at=reader.GetString(2),mode=reader.GetString(5),input=reader.GetString(6),result=reader.GetString(7),note=reader.GetString(9);Utc(at);Text(note,2000);if(reader.GetInt32(1)!=++revision || state!="Open")Bad("Broken count action revision/state chain.");lastAt=at;
            if(action is "Created" or "Closed" or "Canceled")
            {
                if(!reader.IsDBNull(4) || mode!="" || input!="" || result!="" || !reader.IsDBNull(8))Bad("Invalid count lifecycle action.");if(action=="Created"){if(revision!=1 || at!=session!.CreatedUtc || note!=session.Notes)Bad("Invalid count creation evidence.");}else{if(revision==1 || note!="")Bad("Invalid count closure evidence.");state=action;}continue;
            }
            if(revision==1 || action is not ("Seen" or "Duplicate" or "Undo") || reader.IsDBNull(20) || reader.GetInt64(21)!=id || note!="")Bad("Invalid count entry action.");
            var entry=new CountEntry(reader.GetInt64(20),reader.GetInt64(21),reader.IsDBNull(22)?null:reader.GetString(22),reader.GetInt32(23)==1,reader.IsDBNull(24)?null:JsonSerializer.Deserialize<AssetRecord>(reader.GetString(24)),reader.GetString(25),reader.GetInt32(26)==1,reader.GetString(27),reader.GetString(28),reader.GetString(29));
            string expected=Outcome(entry with {Observed=true},session!);
            if(action=="Undo"){if(mode!="" || input!="" || !reader.IsDBNull(8) || result!=expected)Bad("Invalid reading withdrawal.");continue;}
            Token(mode,input);
            if(entry.AssetUid is null){if(!reader.IsDBNull(8) || result is not ("Unknown" or "Ambiguous") || mode!=entry.InputMode || AssetCatalog.Key(input)!=entry.LookupKey)Bad("Invalid unresolved action evidence.");}
            else
            {
                var resolution=reader.IsDBNull(8)?null:JsonSerializer.Deserialize<AssetRecord>(reader.GetString(8));if(resolution is null)throw new InvalidDataException("Missing count identity resolution.");Asset(resolution);if(resolution.Uid!=entry.AssetUid || resolution.Number!=entry.Snapshot!.Number || result!=expected)Bad("Count identity/result evidence mismatch.");string key=AssetCatalog.Key(input);if(mode=="AssetCode"?key!=resolution.Code:key!=AssetCatalog.Key(resolution.Serial) && key!=entry.LookupKey)Bad("Count input does not match its identity evidence.");
            }
        }
        End();
        // Each check uses indexed correlated lookups. Final effective fields must match the last Seen/Undo, ignoring duplicates.
        string previous="COALESCE((SELECT CASE x.action WHEN 'Seen' THEN 1 ELSE 0 END FROM count_actions x WHERE x.entry_id=a.entry_id AND x.revision<a.revision AND x.action IN('Seen','Undo') ORDER BY x.revision DESC LIMIT 1),0)";
        foreach(string sql in new[]{
            "SELECT COUNT(*) FROM count_sessions s WHERE s.expected_count!=(SELECT COUNT(*) FROM count_entries e WHERE e.session_id=s.id AND e.expected=1) OR NOT EXISTS(SELECT 1 FROM count_actions a WHERE a.session_id=s.id)",
            "SELECT COUNT(*) FROM (SELECT session_id,lookup_key FROM count_entries WHERE expected=1 AND lookup_key!='' GROUP BY session_id,lookup_key HAVING COUNT(*)>1)",
            "SELECT COUNT(*) FROM count_actions a WHERE a.action IN('Seen','Duplicate','Undo') AND "+previous+"!=CASE WHEN a.action='Seen' THEN 0 ELSE 1 END",
            "SELECT COUNT(*) FROM count_entries e WHERE (e.expected=0 AND NOT EXISTS(SELECT 1 FROM count_actions a WHERE a.entry_id=e.id AND a.action IN('Seen','Undo') AND a.action='Seen')) OR e.observed!=COALESCE((SELECT CASE a.action WHEN 'Seen' THEN 1 ELSE 0 END FROM count_actions a WHERE a.entry_id=e.id AND a.action IN('Seen','Undo') ORDER BY a.revision DESC LIMIT 1),0)",
            "SELECT COUNT(*) FROM count_entries e WHERE e.observed=1 AND (e.observed_utc!=(SELECT a.at_utc FROM count_actions a WHERE a.entry_id=e.id AND a.action IN('Seen','Undo') AND a.action='Seen' ORDER BY a.revision DESC LIMIT 1) OR e.input_mode!=(SELECT a.input_mode FROM count_actions a WHERE a.entry_id=e.id AND a.action IN('Seen','Undo') AND a.action='Seen' ORDER BY a.revision DESC LIMIT 1) OR e.input_value!=(SELECT a.input_value FROM count_actions a WHERE a.entry_id=e.id AND a.action IN('Seen','Undo') AND a.action='Seen' ORDER BY a.revision DESC LIMIT 1))"})
        {cmd.CommandText=sql;if(Convert.ToInt64(cmd.ExecuteScalar())!=0)Bad("Count scope/effective reading/audit mismatch.");}
        foreach(var spec in new[]{("count_entries","session_id","count_sessions","id"),("count_entries","asset_uid","assets","uid"),("count_actions","session_id","count_sessions","id"),("count_actions","entry_id","count_entries","id")})
        {cmd.CommandText="PRAGMA foreign_key_list("+spec.Item1+")";bool found=false;using(var reader=cmd.ExecuteReader())while(reader.Read())if(reader.GetString(2)==spec.Item3 && reader.GetString(3)==spec.Item2 && reader.GetString(4)==spec.Item4)found=true;if(!found)Bad("Missing count foreign key.");}
    }
}
