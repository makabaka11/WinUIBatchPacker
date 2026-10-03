using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WinUIBatchPacker;

public class MediaRow : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _number = "";
    // WinUI's generated XAML type metadata constructs models through a default
    // constructor and property setters, so these cannot be required/init-only.
    public string DisplayName { get; set; } = "";
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();
    public string Episode { get; set; } = "";
    public string EpisodeDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(Episode)) return "";
            var special = Regex.Match(Episode, @"^(OVA|OAD|SP|NCOP|NCED)(\d+)$");
            if (special.Success) return special.Groups[1].Value == "NCOP" ? Episode : special.Groups[1].Value + int.Parse(special.Groups[2].Value).ToString("D2");
            var numeric = Regex.Match(Episode, @"^(0*\d+)(?:\.(\d+))?$");
            if (numeric.Success)
            {
                var whole = int.Parse(numeric.Groups[1].Value).ToString("D2");
                return numeric.Groups[2].Success ? whole + "." + numeric.Groups[2].Value : whole;
            }
            return Episode;
        }
    }
    public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; Changed(); } } }
    public string Number { get => _number; set { if (_number != value) { _number = value; Changed(); } } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record PackOptions(string Ffmpeg, string Encoding, string FallbackLanguage,
    bool DefaultSubtitle, bool ReplaceOriginal, string OutputFolder);

public sealed record FontToolsLocation(string Python, string BinDirectory);

public enum IssueDecision { Continue, Skip, Stop }

public sealed class BatchControlException(IssueDecision decision) : Exception
{
    public IssueDecision Decision { get; } = decision;
}
