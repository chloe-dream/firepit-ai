using Firepit.Core.Projects;

namespace Firepit.Core.Sessions;

public sealed class Session
{
    public Session(ProjectContext context, string adapterId)
    {
        Context = context;
        AdapterId = adapterId;
        State = SessionState.Cold;
    }

    public ProjectContext Context { get; }

    public string AdapterId { get; }

    public SessionState State { get; set; }

    public int Pid { get; set; }
}
