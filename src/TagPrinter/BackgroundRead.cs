namespace TagPrinter;

/// <summary>One background reader per view. New requests cancel/replace old work; only the latest result may reach controls.</summary>
internal sealed class BackgroundRead<T> : IDisposable
{
    private sealed record Request(long Generation,Func<CancellationToken,T> Read,Action<T> Apply,Action<Exception> Error);
    private Request? pending;
    private CancellationTokenSource? active;
    private bool running,disposed;
    private long generation;
    private Action<bool>? busy;
    internal bool ForceAsync {get;set;}
    internal bool Synchronous=>!ForceAsync && Environment.GetCommandLineArgs().Contains("--self-test");
    internal bool IsBusy=>running;
    /// <summary>Captures delegates on the UI thread; Read must not access any live controls.</summary>
    internal void Submit(Func<CancellationToken,T> read,Action<T> apply,Action<Exception> error,Action<bool> busyChanged)
    {
        if(disposed)return;
        if(Synchronous){apply(read(CancellationToken.None));return;}
        busy=busyChanged;pending=new(++generation,read,apply,error);active?.Cancel();
        if(!running){running=true;busyChanged(true);_ = Drain();}else busyChanged(true);
    }
    /// <summary>Coalesces queued requests without spawning a new worker for every keystroke/filter change.</summary>
    private async Task Drain()
    {
        try
        {
            while(!disposed && pending is {} request)
            {
                pending=null;using var cancellation=new CancellationTokenSource();active=cancellation;
                try
                {
                    var result=await Task.Run(()=>request.Read(cancellation.Token));
                    if(!disposed && request.Generation==generation && !cancellation.IsCancellationRequested)request.Apply(result);
                }
                catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
                catch(Exception error){if(!disposed && request.Generation==generation)request.Error(error);}
                finally{active=null;}
            }
        }
        finally{running=false;if(!disposed)busy?.Invoke(false);}
    }
    /// <summary>Stops result delivery and interrupts current SQL work before child controls are disposed.</summary>
    public void Dispose(){disposed=true;pending=null;active?.Cancel();}
}
