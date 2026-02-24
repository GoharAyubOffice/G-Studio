namespace GStudio.Common.Timeline;

public readonly record struct TimeRange(double Start, double End)
{
    public bool Contains(double time)
    {
        return time >= Start && time <= End;
    }

    public TimeRange Normalize()
    {
        return Start <= End ? this : new TimeRange(End, Start);
    }
}
