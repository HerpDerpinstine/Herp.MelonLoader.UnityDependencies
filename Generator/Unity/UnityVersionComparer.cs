namespace Generator;

public class UnityVersionComparer : IComparer<UnityVersion>
{
    public static readonly UnityVersionComparer Instance = new();
    
    private static int GetBuildTypeOrder(char buildType) => buildType switch
    {
        'a' => 0, // Alpha
        'b' => 1, // Beta
        'c' => 2, // China
        'f' => 3, // Final
        'p' => 4, // Patch
        'x' => 5, // Experimental
        _ => int.MaxValue
    };

    public int Compare(UnityVersion x, UnityVersion y)
    {
        int result;

        if ((result = x.Major.CompareTo(y.Major)) != 0)
            return result;

        if ((result = x.Minor.CompareTo(y.Minor)) != 0)
            return result;

        if ((result = x.Patch.CompareTo(y.Patch)) != 0)
            return result;

        if ((result = GetBuildTypeOrder(x.BuildType)
                .CompareTo(GetBuildTypeOrder(y.BuildType))) != 0)
            return result;

        return x.BuildNumber.CompareTo(y.BuildNumber);
    }
}