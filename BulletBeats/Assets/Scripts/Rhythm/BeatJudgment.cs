/// <summary>How close to a beat an input landed.</summary>
public enum BeatJudgment
{
    Perfect,
    Good,
    Miss,
    /// <summary>The shot destroyed an asteroid, but the timing was outside Good and Perfect.</summary>
    Fine
}
