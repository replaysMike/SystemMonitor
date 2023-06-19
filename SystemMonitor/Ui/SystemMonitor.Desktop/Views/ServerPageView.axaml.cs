using Avalonia.Controls;
using Avalonia.Controls.Selection;
using LightInject;
using System;
using System.Diagnostics;
using Avalonia;
using SystemMonitor.Desktop.Models;
using SystemMonitor.Desktop.ViewModels;

namespace SystemMonitor.Desktop.Views
{
    public partial class ServerPageView : UserControl
    {
        public ServerPageViewModel ViewModel => (ServerPageViewModel?)DataContext ?? throw new InvalidOperationException("DataContext was not set");

        public ServerPageView()
        {
            InitializeComponent();

            if (Design.IsDesignMode)
            {
                DataContext = new ServerPageViewModel();
            }
        }

        private void Selection_SelectionChanged2(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext != null && ViewModel.Selection.SelectedItem != null)
            {
                // open the modal window to view the graphs for the selected server
                var server = ViewModel.Selection.SelectedItem;
                var window = App.Container.GetInstance<MainWindow>();
                var control = Page.ContainerFromItem(ViewModel.Selection.SelectedItem);
                if (control != null)
                {
                    var pointOnScreen = control.PointToScreen(new Point());
                    var pointRelativeToWindow = control.TranslatePoint(new Point(), this);
                    window.AddServerViewModal(server, pointRelativeToWindow);
                }
            }
        }
    }
}
