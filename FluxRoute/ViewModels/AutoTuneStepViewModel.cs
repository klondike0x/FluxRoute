using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;

namespace FluxRoute.ViewModels;

public enum AutoTuneStepStatus
{
    Pending,
    Running,
    Success,
    Failed
}

public sealed class AutoTuneStepViewModel : ObservableObject
{
    public int Number { get; }
    public string IpSetMode { get; }
    public string GameFilterProtocol { get; }
    public string DisplayName => $"{IpSetMode} / {(GameFilterProtocol == "Выкл" ? "без фильтра" : GameFilterProtocol)}";

    private AutoTuneStepStatus _status;
    public AutoTuneStepStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusGlyph));
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    private string _resultText = "Ожидает проверки";
    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public string StatusText => Status switch
    {
        AutoTuneStepStatus.Running => "Проверяется",
        AutoTuneStepStatus.Success => "Успешно",
        AutoTuneStepStatus.Failed => "Есть ошибки",
        _ => "Ожидает"
    };

    public string StatusGlyph => Status switch
    {
        AutoTuneStepStatus.Running => "●",
        AutoTuneStepStatus.Success => "✓",
        AutoTuneStepStatus.Failed => "!",
        _ => "·"
    };

    public MediaBrush StatusBrush => Status switch
    {
        AutoTuneStepStatus.Running => new SolidColorBrush(MediaColor.FromRgb(88, 166, 255)),
        AutoTuneStepStatus.Success => new SolidColorBrush(MediaColor.FromRgb(63, 185, 80)),
        AutoTuneStepStatus.Failed => new SolidColorBrush(MediaColor.FromRgb(248, 81, 73)),
        _ => new SolidColorBrush(MediaColor.FromRgb(102, 117, 142))
    };

    public AutoTuneStepViewModel(int number, string ipSetMode, string gameFilterProtocol)
    {
        Number = number;
        IpSetMode = ipSetMode;
        GameFilterProtocol = gameFilterProtocol;
    }

    public void MarkRunning()
    {
        Status = AutoTuneStepStatus.Running;
        ResultText = "Проверка целей...";

    }

    public void MarkResult(int successCount, int totalCount, double latencyMs)
    {
        Status = successCount > 0 && successCount == totalCount
            ? AutoTuneStepStatus.Success
            : AutoTuneStepStatus.Failed;
        ResultText = $"{successCount}/{totalCount} целей • {latencyMs:0} мс";
    }

    public void MarkFailed(string message = "Не удалось проверить")
    {
        Status = AutoTuneStepStatus.Failed;
        ResultText = message;
    }
}
