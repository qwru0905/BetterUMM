using System.Windows;
using BetterUMM.Services;
using System;
using System.Threading.Tasks;

namespace BetterUMM
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Hook into unhandled exception events
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            LoggerService.Log("Application started.");
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LoggerService.LogException(e.Exception, "DispatcherUnhandledException");
            ShowExceptionMessageBox(e.Exception);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LoggerService.LogException(ex, "CurrentDomain_UnhandledException");
                ShowExceptionMessageBox(ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LoggerService.LogException(e.Exception, "TaskScheduler_UnobservedTaskException");
            e.SetObserved();
        }

        private void ShowExceptionMessageBox(Exception ex)
        {
            MessageBox.Show($"예기치 않은 오류가 발생했습니다. 로그 파일을 확인해주세요.\n\n오류 메시지: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
