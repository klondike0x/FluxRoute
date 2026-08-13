namespace FluxRoute.Core.Services;

public readonly record struct NetworkTrafficRate(
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    bool IsAvailable);

public interface INetworkTrafficMonitor
{
    NetworkTrafficRate Sample();
}

public readonly record struct NetworkTrafficDisplay(string Download, string Upload)
{
    public static NetworkTrafficDisplay Create(
        bool protectionIsRunning,
        NetworkTrafficRate rate)
    {
        if (!protectionIsRunning || !rate.IsAvailable)
            return new NetworkTrafficDisplay("0 Б/с", "0 Б/с");

        return new NetworkTrafficDisplay(
            TrafficSpeedFormatter.Format(rate.DownloadBytesPerSecond),
            TrafficSpeedFormatter.Format(rate.UploadBytesPerSecond));
    }
}
