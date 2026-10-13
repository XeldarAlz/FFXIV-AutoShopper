namespace AutoShopper.Core.Shopping.Model;

public sealed class ListValidation
{
    public const LineProblems ErrorMask = LineProblems.NotMarketable
        | LineProblems.InvalidQuantity
        | LineProblems.InvalidUnitCap
        | LineProblems.InvalidExcess
        | LineProblems.QualityUnavailable
        | LineProblems.MergedOverLimit;

    public const LineProblems WarningMask = LineProblems.Duplicate;

    private readonly LineProblems[] problems;

    public ListValidation(LineProblems[] problems, bool itemsChecked)
    {
        this.problems = problems;
        ItemsChecked = itemsChecked;
        for (var index = 0; index < problems.Length; index++)
        {
            if (IsError(problems[index]))
            {
                ErrorLines++;
            }
            else if ((problems[index] & WarningMask) != 0)
            {
                WarningLines++;
            }
        }
    }

    public int LineCount => problems.Length;

    public int ErrorLines { get; }

    public int WarningLines { get; }

    // False while item data is still loading: marketability and HQ availability were not checked yet.
    public bool ItemsChecked { get; }

    public bool HasErrors => ErrorLines > 0;

    public LineProblems At(int lineIndex) => (uint)lineIndex < (uint)problems.Length ? problems[lineIndex] : LineProblems.None;

    public static bool IsError(LineProblems lineProblems) => (lineProblems & ErrorMask) != 0;
}
