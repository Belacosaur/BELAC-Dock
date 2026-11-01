namespace Belac.Dock.Core.Separation;

public enum SeparationPhase
{
    Mirror,
    Attempt,
    DockOnly,
    Fallback
}

public sealed class SeparationSession
{
    public SeparationPhase Phase { get; private set; } = SeparationPhase.Mirror;
    public DateTime? AttemptStartedUtc { get; private set; }

    public void BeginAttempt(DateTime utc)
    {
        Phase = SeparationPhase.Attempt;
        AttemptStartedUtc = utc;
    }

    public void MarkVerified()
    {
        if (Phase == SeparationPhase.Attempt)
            Phase = SeparationPhase.DockOnly;
    }

    public void MarkFailed()
    {
        if (Phase == SeparationPhase.Attempt)
            Phase = SeparationPhase.Fallback;
    }

    public void Timeout(DateTime utc, TimeSpan limit)
    {
        if (Phase == SeparationPhase.Attempt &&
            AttemptStartedUtc is DateTime started &&
            utc - started >= limit)
        {
            Phase = SeparationPhase.Fallback;
        }
    }

    public void Disable()
    {
        Phase = SeparationPhase.Mirror;
        AttemptStartedUtc = null;
    }
}
