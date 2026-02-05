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
            // Select the Scan Help tab (index 1)
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

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "File Auditor v1.0\n\n" +
                "A comprehensive tool for auditing and cleaning file systems including:\n" +
                "• Local drives\n" +
                "• Network paths\n" +
                "• Box Drive folders\n\n" +
                "Features:\n" +
                "• Recursive scanning with depth control\n" +
                "• Parallel processing\n" +
                "• File type breakdown\n" +
                "• Export to CSV/JSON\n" +
                "• Scan history and comparison\n" +
                "• Safe cleanup with dry-run mode\n" +
                "• Command-line interface for automation\n\n" +
                "Built with .NET 8 and WPF\n" +
                "© 2026",
                "About File Auditor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}