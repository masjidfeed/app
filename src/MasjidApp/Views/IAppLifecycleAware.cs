namespace MasjidApp.Views;

/// <summary>Receives application lifecycle transitions that do not raise page events.</summary>
internal interface IAppLifecycleAware
{
    /// <summary>Called before the application is suspended.</summary>
    void OnAppStopped();

    /// <summary>Called after the application resumes from suspension.</summary>
    void OnAppResumed();
}
