using System.Windows;

using FileAuditor.WPF.ViewModels;
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
                "• Safe cleanup with dry-run mode\n\n" +
                "Built with .NET 8 and WPF",
                "About File Auditor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}

// Previous code commented out for reference:
//using System.Windows;

//using FileAuditor.WPF.ViewModels;
//// Aliases to avoid ambiguity with System.Windows.Forms
//using MessageBox = System.Windows.MessageBox;
//using MessageBoxButton = System.Windows.MessageBoxButton;
//using MessageBoxImage = System.Windows.MessageBoxImage;

//namespace FileAuditor.WPF
//{
//    public partial class MainWindow : Window
//    {
//        public MainWindow(MainViewModel viewModel)
//        {
//            InitializeComponent();
//            DataContext = viewModel;
//        }

//        private void Exit_Click(object sender, RoutedEventArgs e)
//        {
//            System.Windows.Application.Current.Shutdown();
//        }

//        private void About_Click(object sender, RoutedEventArgs e)
//        {
//            MessageBox.Show(
//               "File Auditor v1.0\n\n" +
//               "A comprehensive tool for auditing file systems including:\n" +
//               "• Local drives\n" +
//               "• Network paths\n" +
//               "• Box Drive folders\n\n" +
//               "Features:\n" +
//               "• Recursive scanning with depth control\n" +
//               "• Parallel processing\n" +
//               "• File type breakdown\n" +
//               "• Export to CSV/JSON\n" +
//               "• Scan history and comparison\n\n" +
//               "Built with .NET 8 and WPF",
//               "About File Auditor",
//               MessageBoxButton.OK,
//               MessageBoxImage.Information);
//        }
//    }
//}
