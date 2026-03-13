using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using FileAuditor.Core.Enums;
using FileAuditor.Core.Services;
using FileAuditor.WPF.ViewModels;
using FileAuditor.WPF.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Aliases to avoid ambiguity with System.Windows.Forms
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace FileAuditor.WPF
{
    public partial class MainWindow : Window
    {
        public MainViewModel MainViewModel { get; }
        /// <summary>ViewModel for the Delete tab (locked to Delete mode).</summary>
        public CleanupViewModel DeleteViewModel { get; }
        /// <summary>ViewModel for the Move to Folder tab (locked to MoveToFolder mode).</summary>
        public CleanupViewModel MoveViewModel { get; }

        public MainWindow(MainViewModel mainViewModel, IServiceProvider serviceProvider)
        {
            InitializeComponent();

            MainViewModel = mainViewModel;

            // Construct two independent CleanupViewModel instances, each locked to a mode.
            // We resolve their shared service dependencies from the container and pass the
            // initialMode parameter manually — the DI container cannot differentiate two
            // CleanupViewModel parameters of the same type.
            DeleteViewModel = new CleanupViewModel(
                serviceProvider.GetRequiredService<ICleanupService>(),
                serviceProvider.GetRequiredService<IScanHistoryService>(),
                serviceProvider.GetRequiredService<IExportService>(),
                serviceProvider.GetRequiredService<ILogger<CleanupViewModel>>(),
                CleanupOperationMode.Delete);

            MoveViewModel = new CleanupViewModel(
                serviceProvider.GetRequiredService<ICleanupService>(),
                serviceProvider.GetRequiredService<IScanHistoryService>(),
                serviceProvider.GetRequiredService<IExportService>(),
                serviceProvider.GetRequiredService<ILogger<CleanupViewModel>>(),
                CleanupOperationMode.MoveToFolder);

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
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 1;
            };
            helpWindow.ShowDialog();
        }

        private void DeleteHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 2;
            };
            helpWindow.ShowDialog();
        }

        private void MoveHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 3;
            };
            helpWindow.ShowDialog();
        }

        private void CliHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            helpWindow.Loaded += (s, args) =>
            {
                var tabControl = helpWindow.FindName("HelpTabControl") as System.Windows.Controls.TabControl;
                if (tabControl != null)
                    tabControl.SelectedIndex = 4;
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

                Directory.CreateDirectory(logsFolder);
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
                "This will reset all tabs (File Counter, Delete, Move to Folder) to their default settings.\n\n" +
                "All unsaved paths and results will be lost. Continue?",
                "Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                MainViewModel.ClearAllCommand.Execute(null);
                DeleteViewModel.ClearAllCommand.Execute(null);
                MoveViewModel.ClearAllCommand.Execute(null);
            }
        }

        /// <summary>
        /// Shared handler for Max Depth TextBoxes — allows only digit characters.
        /// </summary>
        private void MaxDepth_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        /// <summary>
        /// Shared paste handler for Max Depth TextBoxes — blocks non-integer paste content.
        /// </summary>
        private void MaxDepth_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string))!;
                if (!text.All(char.IsDigit))
                    e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
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
                "• Move to Folder: single-move or batch CSV mode\n" +
                "• Date quick-set (Today / Now buttons)\n" +
                "• Clear All to reset all tabs\n" +
                "• Command-line interface for automation\n\n" +
                "Built with .NET 8 and WPF\n" +
                "© 2026",
                "About File Auditor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
