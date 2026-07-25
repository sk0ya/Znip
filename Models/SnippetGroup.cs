using System.ComponentModel;

namespace Znip.Models;

public class SnippetGroup : INotifyPropertyChanged
{
    private string _name = "";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
