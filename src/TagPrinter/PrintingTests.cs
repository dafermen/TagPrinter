using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace TagPrinter;

/// <summary>Exercises inventory exclusion, keyboard submission and job tracking with no native printer access.</summary>
internal static class PrintingTests
{
    /// <summary>Runs real JSON/SQLite/UI behavior against injected submission and queue observers.</summary>
    internal static void Run(string folder)
    {
        TestTracker();TestFreeText(folder);TestCommand(folder);TestReadFailure(folder);
        File.WriteAllText(Path.Combine(folder,"printing-results.txt"),"PASS: Free text adds zero DB rows for PNG/ZPL/code/print; old free-text rows hidden without deletion; Ctrl+P/button share captured submission; duplicate/invalid shortcuts suppressed; queued/printing/removed/completed/error/unobserved/timeout states; notice expiry and disposal. No real printer queries or submissions.");
    }

    /// <summary>Checks status priorities and exact document/ID correlation, including fast jobs never observed.</summary>
    private static void TestTracker()
    {
        var ticket=new PrintTicket("Fake","unique",42);
        if(WindowsPrintJobs.Matches(ticket,"other",42) || WindowsPrintJobs.Matches(ticket,"unique",43) || !WindowsPrintJobs.Matches(ticket,"unique",42))throw new Exception("Job correlation admitted an unrelated job");
        foreach(var item in new[]{(0u,"queued"),(8u,"spooling"),(0x10u,"printing"),(1u,"paused"),(0x20u,"offline"),(0x40u,"paper"),(2u,"error"),(0x4u,"deleting"),(0x100u,"deleted"),(0x80u,"complete"),(0x1000u,"complete"),(0x82u,"error")})
        {
            var tracker=new PrintJobTracker();var result=tracker.Observe(new(true,true,item.Item1,42));if(result.Key!=item.Item2)throw new Exception("Job status mapping failed");
            if(!result.Terminal && tracker.Observe(new(true)).Key!="left")throw new Exception("Observed job disappearance not reported");
        }
        if(new PrintJobTracker(42).Observe(new(false)).JobId!=42)throw new Exception("Known RAW ID lost when tracking unavailable");
        var fast=new PrintJobTracker();if(fast.Observe(new(true)).Terminal || fast.Observe(new(true)).Terminal || fast.Observe(new(true)).Key!="unobserved")throw new Exception("Unseen job was incorrectly declared complete");
        if(new PrintJobTracker().Observe(new(false)).Key!="unavailable")throw new Exception("Unavailable queue was treated as success");
    }

    /// <summary>Counts actual SQLite rows rather than relying on a filtered report; preserves old free-text rows.</summary>
    private static void TestFreeText(string folder)
    {
        string root=Path.Combine(folder,"no-free-history-"+Guid.NewGuid());Directory.CreateDirectory(root);
        using var form=new MainForm(Path.Combine(root,"work.json"),AppConfig.CreateDefault());form.Show();
        var state=new WorkState{FreeTextMode=true,FreeText="Caja de cables HDMI",Serial="HIDDEN123"};
        form.Restore(state);form.ExportTagFile(Path.Combine(root,"text.png"));form.ExportTagFile(Path.Combine(root,"text.zpl"),true);form.ExportCodeFile(Path.Combine(root,"code.png"));
        if(Count(form.History)!=0 || form.History.Add("PrintSubmitted",state)!=0)throw new Exception("Free text was inserted into inventory DB");
        long old=form.History.Add("ExportTag",new WorkState{Serial="LEGACY"});
        using(var connection=new SqliteConnection("Data Source="+form.History.DatabasePath))
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="UPDATE events SET state_json=$json WHERE id=$id";command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(state));command.Parameters.AddWithValue("$id",old);command.ExecuteNonQuery();
        }
        if(Count(form.History)!=1 || form.History.Query().Count!=0 || form.History.Get(old) is null)throw new Exception("Legacy Free text not hidden/preserved");
        SelectFake(form);form.JobNoticeDuration=TimeSpan.FromMilliseconds(30);form.JobPollInterval=TimeSpan.FromMilliseconds(1);
        var pending=new TaskCompletionSource<PrintTicket>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.PrintSubmitOverride=_=>pending.Task;form.JobReaderOverride=_=>Task.FromResult(new PrintJobObservation(true,true,0x1000,7));
        var printed=form.RequestPrintAsync();form.Restore(form.CaptureWork() with {FreeTextMode=false});pending.SetResult(new PrintTicket("Simulated label queue","fake-free",7));Pump(()=>printed.IsCompleted);printed.GetAwaiter().GetResult();
        if(Count(form.History)!=1)throw new Exception("Free-text print was inserted into DB");
        form.Restore(state);form.SaveConfig();form.Close();using var reopened=new MainForm(Path.Combine(root,"work.json"),AppConfig.CreateDefault());
        if(!reopened.CaptureWork().FreeTextMode || reopened.CaptureWork().FreeText!=state.FreeText)throw new Exception("Free-text autosave was lost");
    }

    /// <summary>Drives the shared command and actual temporary UI through sending/printing/removal/expiry.</summary>
    private static void TestCommand(string folder)
    {
        string root=Path.Combine(folder,"print-command-"+Guid.NewGuid());Directory.CreateDirectory(root);
        using var form=new MainForm(Path.Combine(root,"work.json"),AppConfig.CreateDefault());form.Show();SelectFake(form);
        form.Restore(form.CaptureWork() with {Serial="FROZEN123",FreeTextMode=false,Language="en"});
        form.JobPollInterval=TimeSpan.FromMilliseconds(100);form.JobNoticeDuration=TimeSpan.FromMilliseconds(80);
        int submissions=0,reads=0;var pending=new TaskCompletionSource<PrintTicket>(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkState? submitted=null;
        form.PrintSubmitOverride=s=>{submissions++;submitted=s;return pending.Task;};
        form.JobReaderOverride=t=>{if(t.Printer!="Simulated label queue" || t.Document!="unique-test")throw new Exception("Observer received wrong captured job");reads++;return Task.FromResult(reads==1?new PrintJobObservation(true,true,8,42):reads==2?new PrintJobObservation(true,true,0x10,42):new PrintJobObservation(true));};
        if(!form.HandlePrintShortcut(Keys.Control|Keys.P))throw new Exception("Shortcut not handled");form.HandlePrintShortcut(Keys.Control|Keys.P);
        if(submissions!=1 || !form.PrintNoticeText.Contains("Sending"))throw new Exception("Duplicate shortcut submitted or sending notice missing");
        long other=form.History.Add("ExportTag",new WorkState{Serial="OTHER"});form.LoadHistoryEvent(form.History.Get(other)!);
        pending.SetResult(new PrintTicket("Simulated label queue","unique-test"));
        Pump(()=>form.PrintNoticeText.Contains("Printing ("));
        using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,form.ClientRectangle);shot.Save(Path.Combine(folder,"printing-job-en.png"));}
        Pump(()=>form.PrintNoticeText.Contains("Job left"));
        var row=form.History.Query().Single(e=>e.Action=="PrintSubmitted");if(row.State.Serial!="FROZEN123" || row.SourceEventId!=null || submitted?.Serial!="FROZEN123")throw new Exception("Submission/history used later editor values or provenance");
        Pump(()=>!form.PrintNoticeVisible);
        form.HandlePrintShortcut(Keys.Control|Keys.P,true);if(submissions!=1)throw new Exception("Held shortcut repeated after a fast job completed");
        form.Restore(form.CaptureWork() with {FreeTextMode=true,FreeText=" "});form.HandlePrintShortcut(Keys.Control|Keys.P);
        if(submissions!=1)throw new Exception("Invalid label shortcut submitted");
        form.ShowReports();form.HandlePrintShortcut(Keys.Control|Keys.P);if(submissions!=1)throw new Exception("Reports shortcut printed a hidden label");
        form.Close();
    }

    /// <summary>Times out a fake blocked queue read, hides the terminal notice and ignores completion after disposal.</summary>
    private static void TestReadFailure(string folder)
    {
        using var form=new MainForm(Path.Combine(folder,"print-failure-"+Guid.NewGuid()+".json"),AppConfig.CreateDefault());form.Show();SelectFake(form);
        form.Restore(form.CaptureWork() with {Language="es"});form.JobReadTimeout=TimeSpan.FromMilliseconds(20);form.JobNoticeDuration=TimeSpan.FromMilliseconds(30);
        var blocked=new TaskCompletionSource<PrintJobObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.PrintSubmitOverride=s=>Task.FromResult(new PrintTicket(s.Printer,"fake-timeout",9));form.JobReaderOverride=_=>blocked.Task;
        var work=form.RequestPrintAsync();Pump(()=>work.IsCompleted);work.GetAwaiter().GetResult();
        if(!form.PrintNoticeText.Contains("seguimiento no disponible"))throw new Exception("Timed-out tracking claimed completion");
        using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,form.ClientRectangle);shot.Save(Path.Combine(folder,"printing-job-es.png"));}
        Pump(()=>!form.PrintNoticeVisible);
        form.JobTrackingLimit=TimeSpan.Zero;
        var limited=form.RequestPrintAsync();Pump(()=>limited.IsCompleted);limited.GetAwaiter().GetResult();
        if(!form.PrintNoticeText.Contains("Seguimiento finalizado"))throw new Exception("Tracking deadline did not show an uncertain result");
        long before=Count(form.History);form.PrintSubmitOverride=_=>Task.FromException<PrintTicket>(new IOException("Fake failed submission"));
        var failed=form.RequestPrintAsync();Pump(()=>failed.IsCompleted);failed.GetAwaiter().GetResult();
        if(!form.PrintNoticeText.Contains("Falló") || Count(form.History)!=before)throw new Exception("Failed submission created an inventory event");
        form.Restore(form.CaptureWork() with {Printer=""});var missing=form.RequestPrintAsync();Pump(()=>missing.IsCompleted);
        if(Count(form.History)!=before || !form.PrintNoticeText.Contains("Falló"))throw new Exception("Missing printer accepted");
        form.Close();blocked.SetException(new IOException("Late fake Windows failure"));Application.DoEvents();
    }

    /// <summary>Uses a synthetic installed-printer item; transports and status readers remain injected.</summary>
    private static void SelectFake(MainForm form)
    {
        var selector=form.Controls.Find("PrinterSelector",true).OfType<ComboBox>().Single();selector.Items.Add("Simulated label queue");selector.SelectedItem="Simulated label queue";
    }
    /// <summary>Counts physical event rows, including hidden legacy rows, in the isolated database.</summary>
    private static long Count(HistoryStore store)
    {
        using var connection=new SqliteConnection("Data Source="+store.DatabasePath);connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM events";return (long)command.ExecuteScalar()!;
    }
    /// <summary>Pumps WinForms only in tests, with a bounded deadline so fake async completions reach the UI thread.</summary>
    private static void Pump(Func<bool> condition)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();while(!condition() && timer.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(2);}if(!condition())throw new Exception("Timed out waiting for simulated printing UI");
    }
}
