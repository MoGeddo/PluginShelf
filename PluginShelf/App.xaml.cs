using System.Windows;
using PluginShelf.Services;

namespace PluginShelf;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isArabic = SettingsService.Load().Language != "en";
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--apply-plan", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var result = await QuarantineService.ExecutePlanFileAsync(e.Args[1]);
                var message = result.Errors.Count == 0
                    ? (isArabic
                        ? $"تم نقل {result.Moved.Count} عنصرًا إلى الحجر القابل للاسترجاع.\nيمكن استرجاعها من PluginShelf."
                        : $"Quarantined {result.Moved.Count} item(s).\nThey can be restored from PluginShelf.")
                    : (isArabic
                        ? $"تم نقل {result.Moved.Count} عنصرًا، وتعذر نقل {result.Errors.Count}.\n\n{string.Join("\n", result.Errors.Take(8))}"
                        : $"Quarantined {result.Moved.Count} item(s); {result.Errors.Count} could not be moved.\n\n{string.Join("\n", result.Errors.Take(8))}");
                MessageBox.Show(message, "PluginShelf", MessageBoxButton.OK,
                    result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                Shutdown(result.Errors.Count == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                MessageBox.Show(isArabic
                        ? $"تعذر إكمال العملية الموافق عليها.\n\n{ex.Message}"
                        : $"The approved operation could not be completed.\n\n{ex.Message}",
                    "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--restore-plan", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var result = await QuarantineService.ExecuteRestorePlanFileAsync(e.Args[1]);
                var message = result.Errors.Count == 0
                    ? (isArabic ? $"تم استرجاع {result.Restored} عنصرًا." : $"Restored {result.Restored} item(s).")
                    : (isArabic
                        ? $"تم استرجاع {result.Restored} عنصرًا، وتعذر استرجاع {result.Errors.Count}.\n\n{string.Join("\n", result.Errors.Take(8))}"
                        : $"Restored {result.Restored} item(s); {result.Errors.Count} could not be restored.\n\n{string.Join("\n", result.Errors.Take(8))}");
                MessageBox.Show(message, "PluginShelf", MessageBoxButton.OK,
                    result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                Shutdown(result.Errors.Count == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                MessageBox.Show(isArabic
                        ? $"تعذر إكمال الاسترجاع.\n\n{ex.Message}"
                        : $"The restore operation could not be completed.\n\n{ex.Message}",
                    "PluginShelf", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
