using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class FilesView : UserControl
{
    private static readonly string[] AllowedExtensions =
        { "pdf", "docx", "md", "txt", "jpg", "jpeg", "png", "json", "csv", "xls", "xlsx" };
    private const int MaxFileSizeMB = 5;
    private const int MaxFilesAtOnce = 10;

    private bool _isUploading;
    private bool _isDeleting;
    private List<KnowledgeBaseFile> _allFiles = new();
    private DispatcherTimer? _toastTimer;

    public FilesView()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadFiles(showShimmer: true);
    }

    // --- File Loading ---

    private async Task LoadFiles(bool showShimmer = false)
    {
        if (showShimmer && _allFiles.Count == 0)
            ShimmerPanel.Visibility = Visibility.Visible;

        EmptyText.Visibility = Visibility.Collapsed;

        var files = await KnowledgeAgentService.Instance.ListChatFileUploads(limit: 100);

        DispatcherHelper.RunOnUI(() =>
        {
            ShimmerPanel.Visibility = Visibility.Collapsed;
            _allFiles = files;

            if (files.Count > 0)
            {
                RenderFileGroups(files);
                EmptyText.Visibility = Visibility.Collapsed;
            }
            else
            {
                FileGroupsList.Items.Clear();
                EmptyText.Visibility = Visibility.Visible;
            }
        });
    }

    private void RenderFileGroups(List<KnowledgeBaseFile> files)
    {
        FileGroupsList.Items.Clear();

        var grouped = files
            .OrderByDescending(f => f.CreatedAt)
            .GroupBy(f =>
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(f.CreatedAt).LocalDateTime.Date;
                if (date == DateTime.Today) return "Today";
                if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
                return date.ToString("MMMM d");
            });

        foreach (var group in grouped)
        {
            // Date header
            var header = new TextBlock
            {
                Text = group.Key,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = FindBrush("ThemeTextSecondary"),
                Margin = new Thickness(0, 8, 0, 6)
            };
            FileGroupsList.Items.Add(header);

            // Card container for the group
            var card = new Border
            {
                Background = FindBrush("ThemeCard"),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var cardPanel = new StackPanel();

            var fileList = group.ToList();
            for (int i = 0; i < fileList.Count; i++)
            {
                var file = fileList[i];
                cardPanel.Children.Add(CreateFileRow(file));

                // Divider between rows (not after last)
                if (i < fileList.Count - 1)
                {
                    cardPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = FindBrush("ThemeBorder"),
                        Opacity = 0.3,
                        Margin = new Thickness(12, 0, 12, 0)
                    });
                }
            }

            card.Child = cardPanel;
            FileGroupsList.Items.Add(card);
        }
    }

    private UIElement CreateFileRow(KnowledgeBaseFile file)
    {
        var ext = Path.GetExtension(file.OriginalFileName).TrimStart('.').ToLower();
        var (iconText, iconColor) = GetFileIconInfo(ext);

        var row = new Grid { Margin = new Thickness(12, 10, 12, 10), Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // File icon
        var iconBg = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ColorFromHex(iconColor)) { Opacity = 0.15 },
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconLabel = new TextBlock
        {
            Text = iconText, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(ColorFromHex(iconColor)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBg.Child = iconLabel;
        Grid.SetColumn(iconBg, 0);

        // File info
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(new TextBlock
        {
            Text = file.OriginalFileName,
            FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = FindBrush("ThemeTextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = ext.ToUpper(),
            FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = FindBrush("ThemeTextSecondary")
        });
        Grid.SetColumn(infoPanel, 1);

        // Delete button (visible on hover)
        var deleteBtn = new TextBlock
        {
            Text = "\U0001F5D1",
            FontSize = 14,
            Foreground = new SolidColorBrush(ColorFromHex("#FF4B59")),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Opacity = 0,
            Tag = file
        };
        deleteBtn.MouseLeftButtonDown += DeleteFileRow_Click;
        Grid.SetColumn(deleteBtn, 2);

        row.Children.Add(iconBg);
        row.Children.Add(infoPanel);
        row.Children.Add(deleteBtn);

        // Hover effect
        row.MouseEnter += (s, e) => deleteBtn.Opacity = 1;
        row.MouseLeave += (s, e) => deleteBtn.Opacity = 0;

        return row;
    }

    private static (string iconText, string color) GetFileIconInfo(string ext) => ext switch
    {
        "pdf" => ("PDF", "#FF4B59"),
        "doc" or "docx" => ("DOC", "#007CEC"),
        "png" or "jpg" or "jpeg" or "gif" => ("IMG", "#00CA48"),
        "mp3" or "m4a" or "wav" => ("AUD", "#FF4B59"),
        "zip" => ("ZIP", "#FFC600"),
        "xlsx" or "xls" or "csv" => ("XLS", "#00CA48"),
        "json" => ("JSON", "#FFC600"),
        "md" => ("MD", "#007CEC"),
        "txt" => ("TXT", "#878E92"),
        _ => ("FILE", "#878E92")
    };

    // --- Upload Zone Events ---

    private void UploadZone_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isUploading || _isDeleting) return;
        OpenFilePicker();
    }

    private void ChooseFiles_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_isUploading || _isDeleting) return;
        OpenFilePicker();
    }

    private void UploadZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            UploadZoneBorder.BorderBrush = new SolidColorBrush(ColorFromHex("#007CEC"));
        }
        e.Handled = true;
    }

    private void UploadZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void UploadZone_DragLeave(object sender, DragEventArgs e)
    {
        ResetUploadZoneBorder();
        e.Handled = true;
    }

    private async void UploadZone_Drop(object sender, DragEventArgs e)
    {
        ResetUploadZoneBorder();
        e.Handled = true;

        if (_isUploading || _isDeleting) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        await UploadFiles(files);
    }

    private void ResetUploadZoneBorder()
    {
        // Reset to dashed border using theme border color
        UploadZoneBorder.BorderBrush = FindBrush("ThemeBorder");
    }

    // --- File Picker ---

    private void OpenFilePicker()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Supported files|*.pdf;*.docx;*.txt;*.md;*.jpg;*.jpeg;*.png;*.json;*.csv;*.xls;*.xlsx|All files|*.*",
            Title = "Select files to upload",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            _ = UploadFiles(dialog.FileNames);
        }
    }

    // --- Upload Flow ---

    private async Task UploadFiles(string[] filePaths)
    {
        // Validate
        var validFiles = new List<string>();
        foreach (var path in filePaths.Take(MaxFilesAtOnce))
        {
            var ext = Path.GetExtension(path).TrimStart('.').ToLower();
            if (!AllowedExtensions.Contains(ext))
            {
                ShowToast($"Unsupported file type: .{ext}", isError: true);
                continue;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaxFileSizeMB * 1024 * 1024)
            {
                ShowToast($"File too large: {fileInfo.Name} (max {MaxFileSizeMB}MB)", isError: true);
                continue;
            }

            validFiles.Add(path);
        }

        if (filePaths.Length > MaxFilesAtOnce)
            ShowToast($"Maximum {MaxFilesAtOnce} files at once", isError: true);

        if (validFiles.Count == 0) return;

        // Show uploading state
        _isUploading = true;
        UploadContent.Visibility = Visibility.Collapsed;
        UploadingContent.Visibility = Visibility.Visible;

        var uploadedFileNames = new List<string>();

        try
        {
            foreach (var filePath in validFiles)
            {
                var success = await KnowledgeAgentService.Instance.UploadFile(filePath);
                if (success)
                    uploadedFileNames.Add(Path.GetFileName(filePath));
                else
                    ShowToast($"Failed to upload {Path.GetFileName(filePath)}", isError: true);
            }

            if (uploadedFileNames.Count > 0)
            {
                // Poll for file to appear in list
                await PollForUploadedFiles(uploadedFileNames);
            }
        }
        finally
        {
            _isUploading = false;
            UploadContent.Visibility = Visibility.Visible;
            UploadingContent.Visibility = Visibility.Collapsed;
        }
    }

    private async Task PollForUploadedFiles(List<string> fileNames)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            await Task.Delay(2000);

            var files = await KnowledgeAgentService.Instance.ListChatFileUploads(limit: 100);
            var found = fileNames.All(name =>
                files.Any(f => f.OriginalFileName.Equals(name, StringComparison.OrdinalIgnoreCase)));

            if (found)
            {
                DispatcherHelper.RunOnUI(() =>
                {
                    _allFiles = files;
                    RenderFileGroups(files);
                    EmptyText.Visibility = Visibility.Collapsed;
                    var count = fileNames.Count;
                    ShowToast(count == 1
                        ? $"'{fileNames[0]}' uploaded successfully"
                        : $"{count} files uploaded successfully", isError: false);
                });
                return;
            }
        }

        // Timeout — reload anyway
        await LoadFiles();
        DispatcherHelper.RunOnUI(() =>
        {
            ShowToast("Upload completed", isError: false);
        });
    }

    // --- Delete ---

    private async void DeleteFileRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_isDeleting || _isUploading) return;
        if (sender is not TextBlock tb || tb.Tag is not KnowledgeBaseFile file) return;

        _isDeleting = true;
        try
        {
            var success = await KnowledgeAgentService.Instance.DeleteChatFile(file.Id);
            if (success)
            {
                _allFiles.RemoveAll(f => f.Id == file.Id);
                DispatcherHelper.RunOnUI(() =>
                {
                    if (_allFiles.Count > 0)
                        RenderFileGroups(_allFiles);
                    else
                    {
                        FileGroupsList.Items.Clear();
                        EmptyText.Visibility = Visibility.Visible;
                    }
                    ShowToast("File deleted", isError: false);
                });
            }
            else
            {
                ShowToast("Failed to delete file", isError: true);
            }
        }
        finally
        {
            _isDeleting = false;
        }
    }

    // --- Toast ---

    private void ShowToast(string message, bool isError)
    {
        DispatcherHelper.RunOnUI(() =>
        {
            ToastIcon.Text = isError ? "\u274C" : "\u2705";
            ToastIcon.Foreground = isError
                ? new SolidColorBrush(ColorFromHex("#FF4B59"))
                : new SolidColorBrush(ColorFromHex("#00CA48"));
            ToastMessage.Text = message;
            ToastBorder.Visibility = Visibility.Visible;

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                ToastBorder.Visibility = Visibility.Collapsed;
            };
            _toastTimer.Start();
        });
    }

    private void CloseToast_Click(object sender, MouseButtonEventArgs e)
    {
        _toastTimer?.Stop();
        ToastBorder.Visibility = Visibility.Collapsed;
    }

    // --- Helpers ---

    private static Brush FindBrush(string key)
    {
        return Application.Current.Resources[key] as Brush
               ?? new SolidColorBrush(Colors.Gray);
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length switch
        {
            6 => Color.FromRgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16)),
            8 => Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16)),
            _ => Colors.Black
        };
    }
}
