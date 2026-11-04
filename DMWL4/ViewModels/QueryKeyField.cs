using MWL4.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MWL4.ViewModels
{
    /// <summary>An extra query key shown in the Query panel (chosen in Settings -> Query).</summary>
    public class QueryKeyField : INotifyPropertyChanged
    {
        public QueryKeyField(QueryKeyDefinition definition)
        {
            Definition = definition;
        }

        public QueryKeyDefinition Definition { get; }
        public string Label => Definition.Label + ":";
        public string ToolTip => Definition.ToolTip;

        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set { _value = value ?? string.Empty; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
