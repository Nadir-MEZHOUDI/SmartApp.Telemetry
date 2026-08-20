using System.Windows;
using SmartApp.Telemetry.Client;

namespace SmartApp.Telemetry.Sample.Wpf;

public partial class MainWindow : Window
{
    private readonly ITelemetryClient telemetry;

    public MainWindow()
    {
        InitializeComponent();
        telemetry = ((App)Application.Current).Telemetry;
    }

    private void OnExportPdfClicked(object sender, RoutedEventArgs e)
    {
        telemetry.TrackFeatureUsed("ExportPdf");
        Status("Sent feature_used: ExportPdf");
    }

    private void OnBackupClicked(object sender, RoutedEventArgs e)
    {
        telemetry.TrackFeatureUsed("Backup");
        Status("Sent feature_used: Backup");
    }

    private void OnThrowClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            throw new InvalidOperationException("Demo exception from the WPF sample.");
        }
        catch (Exception exception)
        {
            telemetry.TrackException(exception, new { source = "MainWindow.OnThrowClicked" });
            Status("Sent exception report.");
        }
    }

    private static void Status(string message) =>
        MessageBox.Show(message, "Telemetry", MessageBoxButton.OK, MessageBoxImage.Information);
}
