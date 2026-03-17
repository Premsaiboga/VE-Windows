using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class IntentModelView : UserControl
{
    private string _currentSubTab = "Model";
    private List<DictionaryWord> _allWords = new();
    private List<DictionaryWord> _allSnippets = new();
    private Timer? _modelSaveDebounce;
    private bool _isLoadingModel;

    public IntentModelView()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            UpdateSubTabSelection();
            _ = LoadModelTab();
        };
    }

    // --- Sub-Tab Navigation ---

    private void SubTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tab)
        {
            _currentSubTab = tab;
            UpdateSubTabSelection();
            ShowSubTab(tab);
        }
    }

    private void UpdateSubTabSelection()
    {
        var tabs = new[] { TabModel, TabInstructions, TabDictionary, TabSnippets };
        foreach (var tab in tabs)
        {
            var isSelected = (tab.Tag as string) == _currentSubTab;
            tab.Foreground = isSelected
                ? new SolidColorBrush(Color.FromRgb(0, 124, 236))
                : new SolidColorBrush(Color.FromRgb(135, 142, 146));
            tab.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(0, 124, 236))
                : Brushes.Transparent;
        }
    }

    private void ShowSubTab(string tab)
    {
        ModelContent.Visibility = tab == "Model" ? Visibility.Visible : Visibility.Collapsed;
        InstructionsContent.Visibility = tab == "Instructions" ? Visibility.Visible : Visibility.Collapsed;
        DictionaryContent.Visibility = tab == "Dictionary" ? Visibility.Visible : Visibility.Collapsed;
        SnippetsContent.Visibility = tab == "Snippets" ? Visibility.Visible : Visibility.Collapsed;

        switch (tab)
        {
            case "Model": _ = LoadModelTab(); break;
            case "Instructions": _ = LoadInstructions(); break;
            case "Dictionary": _ = LoadDictionary(); break;
            case "Snippets": _ = LoadSnippets(); break;
        }
    }

    // --- Model Tab ---

    private async Task LoadModelTab()
    {
        _isLoadingModel = true;
        ModelLoading.Visibility = Visibility.Visible;
        ModelData.Visibility = Visibility.Collapsed;

        var (name, behaviour) = await IntentMailService.Instance.GetIntentModel();

        DispatcherHelper.RunOnUI(() =>
        {
            CompanionNameBox.Text = name ?? "";
            BehaviourBox.Text = behaviour ?? "";
            ModelLoading.Visibility = Visibility.Collapsed;
            ModelData.Visibility = Visibility.Visible;
            _isLoadingModel = false;
        });
    }

    private void Model_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingModel) return;
        _modelSaveDebounce?.Dispose();
        _modelSaveDebounce = new Timer(_ => _ = SaveModel(), null,
            TimeSpan.FromMilliseconds(1500), Timeout.InfiniteTimeSpan);
    }

    private async Task SaveModel()
    {
        string name = "", behaviour = "";
        DispatcherHelper.RunOnUI(() =>
        {
            name = CompanionNameBox.Text;
            behaviour = BehaviourBox.Text;
        });
        await IntentMailService.Instance.UpdateIntentModel(name, behaviour);
    }

    private void SuggestionChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var text = btn.Content?.ToString();
            if (!string.IsNullOrEmpty(text) && !BehaviourBox.Text.Contains(text))
            {
                BehaviourBox.Text += (BehaviourBox.Text.Length > 0 ? "\n" : "") + text;
            }
        }
    }

    // --- Instructions Tab ---

    private async Task LoadInstructions()
    {
        InstructionsLoading.Visibility = Visibility.Visible;
        var instructions = await KnowledgeAgentService.Instance.GetInstructions();

        DispatcherHelper.RunOnUI(() =>
        {
            InstructionsLoading.Visibility = Visibility.Collapsed;
            InstructionsList.ItemsSource = instructions;
        });
    }

    private async void AddInstruction_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Add Instruction", "Enter instruction content:");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            await KnowledgeAgentService.Instance.SaveInstruction(dialog.Result, "whatsapp");
            await LoadInstructions();
        }
    }

    private async void DeleteInstruction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AIInstruction inst)
        {
            await KnowledgeAgentService.Instance.DeleteInstruction(inst.Id);
            await LoadInstructions();
        }
    }

    // --- Dictionary Tab ---

    private async Task LoadDictionary()
    {
        DictLoading.Visibility = Visibility.Visible;
        _allWords = (await VoiceService.Instance.ListWords())
            .Where(w => w.Type != "snippet").ToList();

        DispatcherHelper.RunOnUI(() =>
        {
            DictLoading.Visibility = Visibility.Collapsed;
            DictionaryList.ItemsSource = _allWords;
        });
    }

    private void DictSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = DictSearchBox.Text?.Trim();
        DictionaryList.ItemsSource = string.IsNullOrEmpty(q)
            ? _allWords
            : _allWords.Where(w => w.Word.Contains(q, StringComparison.OrdinalIgnoreCase)
                || w.Replacement.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async void AddWord_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Add Word", "Word:", "Replacement (optional):");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            await VoiceService.Instance.CreateOrUpdateWord(dialog.Result, dialog.Result2 ?? "", "vocabulary");
            await LoadDictionary();
        }
    }

    private async void DeleteWord_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DictionaryWord word)
        {
            await VoiceService.Instance.DeleteWord(word.Id);
            await LoadDictionary();
        }
    }

    // --- Snippets Tab ---

    private async Task LoadSnippets()
    {
        SnippetLoading.Visibility = Visibility.Visible;
        _allSnippets = await VoiceService.Instance.ListSnippets();

        DispatcherHelper.RunOnUI(() =>
        {
            SnippetLoading.Visibility = Visibility.Collapsed;
            SnippetsList.ItemsSource = _allSnippets;
        });
    }

    private void SnippetSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SnippetSearchBox.Text?.Trim();
        SnippetsList.ItemsSource = string.IsNullOrEmpty(q)
            ? _allSnippets
            : _allSnippets.Where(s => s.Word.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Replacement.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async void AddSnippet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Add Snippet", "Shortcut:", "Content:");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            await VoiceService.Instance.SaveSnippet(dialog.Result, dialog.Result2 ?? "");
            await LoadSnippets();
        }
    }

    private async void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DictionaryWord snippet)
        {
            await VoiceService.Instance.DeleteSnippet(snippet.Id);
            await LoadSnippets();
        }
    }
}

// --- Simple Input Dialog ---

public class InputDialog : Window
{
    public string Result { get; private set; } = "";
    public string? Result2 { get; private set; }

    private readonly TextBox _input1;
    private readonly TextBox? _input2;

    public InputDialog(string title, string label1, string? label2 = null)
    {
        Title = title;
        Width = 360; Height = label2 != null ? 240 : 180;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock { Text = label1, Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        _input1 = new TextBox { Background = new SolidColorBrush(Color.FromRgb(37, 41, 45)), Foreground = Brushes.White, Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
        stack.Children.Add(_input1);

        if (label2 != null)
        {
            stack.Children.Add(new TextBlock { Text = label2, Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 12, 0, 4) });
            _input2 = new TextBox { Background = new SolidColorBrush(Color.FromRgb(37, 41, 45)), Foreground = Brushes.White, Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            stack.Children.Add(_input2);
        }

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
        var okBtn = new Button { Content = "Save", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush(Color.FromRgb(0, 124, 236)), Foreground = Brushes.White };
        okBtn.Click += (s, e) => { Result = _input1.Text; Result2 = _input2?.Text; DialogResult = true; Close(); };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        stack.Children.Add(btnPanel);

        Content = stack;
    }
}
