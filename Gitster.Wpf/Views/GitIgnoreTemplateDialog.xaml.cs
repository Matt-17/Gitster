using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

using Gitster.Services.Features;
using Gitster.ApplicationLayer.Features;

namespace Gitster.Views;

public partial class GitIgnoreTemplateDialog : Window, INotifyPropertyChanged
{
    private readonly GitIgnoreTemplateService _templates;
    private string _selectedTemplate = string.Empty;
    private string _previewText = string.Empty;

    public GitIgnoreTemplateDialog(GitIgnoreTemplateService templates)
    {
        InitializeComponent();
        _templates = templates;
        TemplateNames = templates.TemplateNames;
        SelectedTemplate = TemplateNames.FirstOrDefault() ?? string.Empty;
        DataContext = this;
        UpdatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> TemplateNames { get; }

    public string SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (_selectedTemplate == value)
                return;

            _selectedTemplate = value;
            OnPropertyChanged();
            UpdatePreview();
        }
    }

    public string PreviewText
    {
        get => _previewText;
        private set
        {
            if (_previewText == value)
                return;

            _previewText = value;
            OnPropertyChanged();
        }
    }

    private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is string template)
            SelectedTemplate = template;
    }

    private void Append_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void UpdatePreview()
    {
        PreviewText = string.IsNullOrWhiteSpace(SelectedTemplate)
            ? string.Empty
            : _templates.GetPreview(SelectedTemplate);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
