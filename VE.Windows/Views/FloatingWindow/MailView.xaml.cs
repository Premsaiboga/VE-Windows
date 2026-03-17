using System.Windows;
using System.Windows.Controls;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class MailView : UserControl
{
    private List<ConnectedEmailAccount> _accounts = new();
    private ConnectedEmailAccount? _selectedAccount;
    private List<EmailCategory> _categories = new();
    private ProactiveEmailConfig? _config;
    private Timer? _saveDebounce;
    private bool _isLoadingSettings;

    public MailView()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadData();
    }

    private async Task LoadData()
    {
        LoadingText.Visibility = Visibility.Visible;
        NoAccountPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;

        _accounts = await IntentMailService.Instance.GetConnectedEmailAccounts();

        DispatcherHelper.RunOnUI(() =>
        {
            LoadingText.Visibility = Visibility.Collapsed;

            if (_accounts.Count == 0)
            {
                NoAccountPanel.Visibility = Visibility.Visible;
                return;
            }

            AccountSelector.Items.Clear();
            foreach (var acc in _accounts)
                AccountSelector.Items.Add($"{acc.ProviderName}: {acc.Email}");
            AccountSelector.SelectedIndex = 0;

            SettingsPanel.Visibility = Visibility.Visible;
        });
    }

    private async void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AccountSelector.SelectedIndex < 0 || AccountSelector.SelectedIndex >= _accounts.Count) return;
        _selectedAccount = _accounts[AccountSelector.SelectedIndex];
        await LoadAccountSettings();
    }

    private async Task LoadAccountSettings()
    {
        if (_selectedAccount == null) return;
        _isLoadingSettings = true;

        var configTask = IntentMailService.Instance.GetProactiveEmailConfig(_selectedAccount.Id);
        var categoriesTask = IntentMailService.Instance.GetEmailCategories(_selectedAccount.Id);
        await Task.WhenAll(configTask, categoriesTask);

        _config = configTask.Result;
        _categories = categoriesTask.Result;

        DispatcherHelper.RunOnUI(() =>
        {
            IntentEmailToggle.IsChecked = _config?.IsActive ?? false;
            ProactiveToggle.IsChecked = _config?.ProactiveRhythm ?? false;
            DraftRepliesToggle.IsChecked = _config?.IsEnabled ?? false;
            ReplyFrequencyCombo.SelectedIndex = _config?.ReplyFrequency ?? 0;
            MarketingFilterCombo.SelectedIndex = _config?.MarketingIrrelevanceRatio ?? 0;
            DraftPromptBox.Text = _config?.Prompt ?? "";

            CategoriesList.ItemsSource = null;
            CategoriesList.ItemsSource = _categories;

            _isLoadingSettings = false;
        });
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e) => DebounceSave();
    private void Setting_Changed(object sender, SelectionChangedEventArgs e) => DebounceSave();
    private void Category_Changed(object sender, RoutedEventArgs e) => DebounceSave();
    private void Prompt_Changed(object sender, TextChangedEventArgs e) => DebounceSave();

    private void DebounceSave()
    {
        if (_isLoadingSettings || _selectedAccount == null || _config == null) return;
        _saveDebounce?.Dispose();
        _saveDebounce = new Timer(_ => _ = SaveSettings(), null, TimeSpan.FromMilliseconds(1500), Timeout.InfiniteTimeSpan);
    }

    private async Task SaveSettings()
    {
        if (_selectedAccount == null || _config == null) return;

        DispatcherHelper.RunOnUI(() =>
        {
            _config.IsActive = IntentEmailToggle.IsChecked ?? false;
            _config.ProactiveRhythm = ProactiveToggle.IsChecked ?? false;
            _config.IsEnabled = DraftRepliesToggle.IsChecked ?? false;
            _config.ReplyFrequency = ReplyFrequencyCombo.SelectedIndex;
            _config.MarketingIrrelevanceRatio = MarketingFilterCombo.SelectedIndex;
            _config.Prompt = DraftPromptBox.Text;
        });

        await IntentMailService.Instance.UpdateDraftPromptSettings(_selectedAccount.Id, _config);
        await IntentMailService.Instance.UpdateEmailCategories(_selectedAccount.Id, _selectedAccount.App, _categories);

        FileLogger.Instance.Info("MailView", "Settings saved");
    }

    private async void ConnectGmail_Click(object sender, RoutedEventArgs e)
    {
        var url = await ConnectorsService.Instance.ConnectIntegration("google");
        if (url != null) AppURLs.OpenUrl(url);
    }

    private async void ConnectOutlook_Click(object sender, RoutedEventArgs e)
    {
        var url = await ConnectorsService.Instance.ConnectIntegration("outlookMail");
        if (url != null) AppURLs.OpenUrl(url);
    }
}
