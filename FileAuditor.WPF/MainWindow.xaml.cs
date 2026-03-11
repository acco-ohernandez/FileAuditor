using System.IO;
using System.Windows;

using FileAuditor.WPF.ViewModels;
using FileAuditor.WPF.Views;
// Aliases to avoid ambiguity with System.Windows.Forms
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace FileAuditor.WPF
{
    public partial class MainWindow : Window
    {
        public MainViewModel MainViewModel { get; }
        public CleanupViewModel CleanupViewModel { get; }

        public MainWindow(MainViewModel mainViewModel, CleanupViewModel cleanupViewModel)
        {
            InitializeComponent();

            MainViewModel = mainViewModel;
            CleanupViewModel = cleanupViewModel;

            DataContext = MainViewModel;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void HelpContents_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            helpWindow.ShowDialog();
        }

        private void ScanHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            // Select the File Counter tab (index 1)
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 1;
            };
            helpWindow.ShowDialog();
        }

        private void CleanupHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            // Select the Cleanup Help tab (index 2)
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 2;
            };
            helpWindow.ShowDialog();
        }

        private void CliHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            // Select the CLI Usage tab (index 3)
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 3;
            };
            helpWindow.ShowDialog();
        }

        private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FileAuditor", "Logs");

                // Create the folder if it doesn't exist
                Directory.CreateDirectory(logsFolder);

                // Open in Windows Explorer
                System.Diagnostics.Process.Start("explorer.exe", logsFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open logs folder:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will reset both the File Counter and Cleanup tabs to their default settings.\n\n" +
                "All unsaved paths and results will be lost. Continue?",
                "Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                MainViewModel.ClearAllCommand.Execute(null);
                CleanupViewModel.ClearAllCommand.Execute(null);
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "File Auditor v1.6\n\n" +
                "By: Orlando R Hernandez\n\n" +
                "A comprehensive tool for auditing and cleaning file systems including:\n" +
                "• Local drives\n" +
                "• Network paths\n" +
                "• Box Drive folders\n\n" +
                "Features:\n" +
                "• Recursive scanning with depth control\n" +
                "• Parallel processing\n" +
                "• File type breakdown (case-insensitive filter)\n" +
                "• Export to CSV/JSON\n" +
                "• Scan history and comparison\n" +
                "• Safe cleanup with dry-run mode\n" +
                "• Move to Folder (per-path destination)\n" +
                "• Date quick-set (Today / Now buttons)\n" +
                "• Clear All to reset the form\n" +
                "• Command-line interface for automation\n\n" +
                "Built with .NET 8 and WPF\n" +
                "© 2026",
                "About File Auditor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}