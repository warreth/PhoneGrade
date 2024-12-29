using Avalonia.Controls;
using AutoDymoLabelApp.UI.ViewModels;

namespace AutoDymoLabel.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel(); // Set the ViewModel as DataContext
        }
    }
}