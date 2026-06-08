using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BetterUMM.Services;
using BetterUMM.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using System.Threading.Tasks;

namespace BetterUMM
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();
                window.DataContext = new MainViewModel(window);
                desktop.MainWindow = window;
            }

            LoggerService.Log("Application started.");

            base.OnFrameworkInitializationCompleted();
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
            Dispatcher.UIThread.Post(() =>
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "오류",
                    $"예기치 않은 오류가 발생했습니다. 로그 파일을 확인해주세요.\n\n오류 메시지: {ex.Message}",
                    ButtonEnum.Ok,
                    Icon.Error);
                _ = box.ShowAsync();
            });
        }
    }
}
