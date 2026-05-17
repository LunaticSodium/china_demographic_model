namespace ChinaDemographicModel.Core.Models;

public sealed class PopulationPyramid
{
    public const int MaxAge = 100;

    public int Year { get; init; }
    public double[] Male { get; init; } = new double[MaxAge + 1];
    public double[] Female { get; init; } = new double[MaxAge + 1];

    public double TotalMale => Male.Sum();
    public double TotalFemale => Female.Sum();
    public double Total => TotalMale + TotalFemale;

    public double SexRatio => TotalFemale <= 0 ? double.NaN : 100.0 * TotalMale / TotalFemale;

    public PopulationPyramid Clone()
    {
        var p = new PopulationPyramid { Year = Year };
        Array.Copy(Male, p.Male, Male.Length);
        Array.Copy(Female, p.Female, Female.Length);
        return p;
    }

    public static PopulationPyramid Empty(int year) => new() { Year = year };

    public double WorkingAgePopulation(int min = 15, int max = 64)
    {
        double s = 0;
        for (int a = min; a <= Math.Min(max, MaxAge); a++) s += Male[a] + Female[a];
        return s;
    }

    public double DependentChildren(int upTo = 14)
    {
        double s = 0;
        for (int a = 0; a <= Math.Min(upTo, MaxAge); a++) s += Male[a] + Female[a];
        return s;
    }

    public double DependentElderly(int from = 65)
    {
        double s = 0;
        for (int a = Math.Min(from, MaxAge); a <= MaxAge; a++) s += Male[a] + Female[a];
        return s;
    }
}
