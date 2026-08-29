namespace MotionUtils;

internal readonly struct MotionSequenceHandle
{
    private readonly MotionSequenceRun? _run;

    internal MotionSequenceHandle(MotionSequenceRun run)
    {
        _run = run;
    }

    internal bool IsActive => _run?.IsActive == true;

    internal void Kill()
    {
        _run?.Kill();
    }
}
