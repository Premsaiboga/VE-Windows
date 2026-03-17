using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class FilesView : UserControl
{
    public FilesView()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadFiles();
    }

    private async Task LoadFiles()
    {
        LoadingText.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;

        var files = await KnowledgeAgentService.Instance.ListKnowledgeBaseFiles();

        DispatcherHelper.RunOnUI(() =>
        {
            LoadingText.Visibility = Visibility.Collapsed;

            if (files.Count > 0)
            {
                FilesList.ItemsSource = files;
            }
            else
            {
                EmptyText.Visibility = Visibility.Visible;
            }
        });
    }

    private async void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Add URL", "Enter URL to index:");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            var success = await KnowledgeAgentService.Instance.UploadUrl(dialog.Result);
            if (success) await LoadFiles();
        }
    }

    private async void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var openFile = new OpenFileDialog
        {
            Filter = "Documents|*.pdf;*.txt;*.csv;*.json|All files|*.*",
            Title = "Select file to upload"
        };

        if (openFile.ShowDialog() == true)
        {
            var success = await KnowledgeAgentService.Instance.UploadFile(openFile.FileName);
            if (success) await LoadFiles();
        }
    }

    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is KnowledgeBaseFile file)
        {
            await KnowledgeAgentService.Instance.DeleteFile(file.Id);
            await LoadFiles();
        }
    }
}
