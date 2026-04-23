using CommunityToolkit.Mvvm.ComponentModel;

namespace FluxRoute.Core.Models;

public partial class ProfileScore : ObservableObject
{
    public string DisplayName { get; init; } = "";
    public string FileName { get; init; } = "";

    [ObservableProperty]
    private int score = -1;

    [ObservableProperty]
    private string scoreText = "—";

    [ObservableProperty]
    private string detailText = "";

    [ObservableProperty]
    private string lastCheckedText = "";

    [ObservableProperty]
    private bool isExcluded;

    public void SetScore(double rate)
    {
        Score = (int)Math.Round(rate * 100);
        IsExcluded = Score == 0;
        ScoreText = IsExcluded ? "❌ 0% (исключён)" : $"{Score}%";
        LastCheckedText = DateTime.Now.ToString("HH:mm:ss");
    }

    public void SetProbeResult(ProfileProbeResult result)
    {
        Score = result.Score;
        IsExcluded = Score == 0;
        ScoreText = IsExcluded ? "❌ 0% (исключён)" : $"{Score}%";
        DetailText = result.Summary;
        LastCheckedText = DateTime.Now.ToString("HH:mm:ss");
    }

    public void SetPending()
    {
        Score = -1;
        ScoreText = "⏳ проверяется...";
        DetailText = "";
        IsExcluded = false;
    }

    public void SetSkipped()
    {
        Score = -1;
        ScoreText = "—";
        DetailText = "";
        LastCheckedText = "";
        IsExcluded = false;
    }
}
