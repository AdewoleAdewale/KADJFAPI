using Acr.UserDialogs;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace KADJFAPI.Renderers
{
    public static class ErrorHandler
    {
        private static bool _isDebugMode = true; // Set based on your build configuration

        public static async Task HandleErrorAsync(string title, Exception exception, Page page = null)
        {
            var userMessage = GetUserFriendlyMessage(exception);
            LogError(title, exception);
            await ShowUserErrorAsync(title, userMessage, page);
        }

        public static async Task HandleErrorAsync(string title, string message, Exception exception = null, Page page = null)
        {
            LogError(title, exception ?? new Exception(message));
            await ShowUserErrorAsync(title, message, page);
        }

        public static string GetUserFriendlyMessage(Exception exception)
        {
            // Using traditional switch statement instead of switch expressions
            switch (exception)
            {
                case System.Net.Http.HttpRequestException httpEx when httpEx.Message.ToLower().Contains("timeout"):
                    return "Connection timed out. Please check your internet connection and try again.";

                case System.Net.Http.HttpRequestException httpEx when httpEx.Message.ToLower().Contains("dns"):
                    return "Unable to connect to server. Please check your internet connection.";

                case System.Net.Http.HttpRequestException _:
                    return "Network connection error. Please check your internet connection and try again.";

                case System.Threading.Tasks.TaskCanceledException cancelEx when cancelEx.InnerException is System.TimeoutException:
                    return "The request timed out. Please try again later.";

                case System.Threading.Tasks.TaskCanceledException _:
                    return "Operation was cancelled or timed out.";

                case Newtonsoft.Json.JsonException _:
                    return "Invalid data received from server. Please contact support if this persists.";

                case UnauthorizedAccessException _:
                    return "Authentication failed. Please log in again.";

                case System.Net.WebException webEx when webEx.Status == System.Net.WebExceptionStatus.ConnectFailure:
                    return "Cannot connect to server. Please check your internet connection.";

                case System.Net.WebException webEx when webEx.Status == System.Net.WebExceptionStatus.Timeout:
                    return "Connection timed out. Please try again.";

                case System.Net.WebException _:
                    return "Network error occurred. Please try again later.";

                case ArgumentException argEx when argEx.Message.ToLower().Contains("mda"):
                    return "Invalid MDA parameter. Please contact support.";

                case NullReferenceException _:
                    return "An unexpected error occurred. Please restart the app and try again.";

                case OutOfMemoryException _:
                    return "The app is running low on memory. Please close other apps and try again.";

                default:
                    return "An unexpected error occurred. Please try again later.";
            }
        }

        private static void LogError(string title, Exception exception)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}";

                if (exception != null)
                {
                    logMessage += $"\nException: {exception.GetType().Name}";
                    logMessage += $"\nMessage: {exception.Message}";

                    if (_isDebugMode)
                    {
                        logMessage += $"\nStackTrace: {exception.StackTrace}";

                        if (exception.InnerException != null)
                        {
                            logMessage += $"\nInner Exception: {exception.InnerException.GetType().Name}";
                            logMessage += $"\nInner Message: {exception.InnerException.Message}";
                        }
                    }
                }

                // Log to debug output
                Debug.WriteLine(logMessage);

                // Log to console
                Console.WriteLine(logMessage);

                // Here you could also log to a file, crash reporting service, etc.
                // await LogToFileAsync(logMessage);
                // await LogToCrashReportingServiceAsync(title, exception);
            }
            catch (Exception logEx)
            {
                // Don't let logging errors crash the app
                Debug.WriteLine($"Logging error: {logEx.Message}");
            }
        }

        private static async Task ShowUserErrorAsync(string title, string message, Page page)
        {
            try
            {
                // Try to show alert on the specific page first
                if (page != null)
                {
                    await page.DisplayAlert(title, message, "OK");
                    return;
                }

                // Try to show alert on current page
                var currentPage = Application.Current?.MainPage;
                if (currentPage != null)
                {
                    await currentPage.DisplayAlert(title, message, "OK");
                    return;
                }

                // Fallback to toast
                UserDialogs.Instance.Toast(message, TimeSpan.FromSeconds(5));
            }
            catch (Exception alertEx)
            {
                try
                {
                    // Last resort - simple toast
                    UserDialogs.Instance.Toast($"{title}: {message}", TimeSpan.FromSeconds(3));
                }
                catch (Exception toastEx)
                {
                    // Log but don't crash
                    Debug.WriteLine($"Failed to show error to user: {toastEx.Message}");
                }
            }
        }

        public static async Task<bool> ShowRetryPromptAsync(string title, string message, Page page = null)
        {
            try
            {
                var targetPage = page ?? Application.Current?.MainPage;
                if (targetPage != null)
                {
                    return await targetPage.DisplayAlert(title, message, "Retry", "Cancel");
                }

                // Fallback - assume user wants to retry
                UserDialogs.Instance.Toast(message, TimeSpan.FromSeconds(3));
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show retry prompt: {ex.Message}");
                return false;
            }
        }

        public static void ShowToast(string message, ToastType type = ToastType.Info)
        {
            try
            {
                var duration = type == ToastType.Error ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(3);
                UserDialogs.Instance.Toast(message, duration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show toast: {ex.Message}");
            }
        }

        public static async Task<string> ShowActionSheetAsync(string title, string cancel, string destruction, params string[] buttons)
        {
            try
            {
                var currentPage = Application.Current?.MainPage;
                if (currentPage != null)
                {
                    return await currentPage.DisplayActionSheet(title, cancel, destruction, buttons);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show action sheet: {ex.Message}");
            }

            return cancel;
        }

        public static bool IsNetworkException(Exception exception)
        {
            return exception is System.Net.Http.HttpRequestException ||
                   exception is System.Net.WebException ||
                   exception is System.Threading.Tasks.TaskCanceledException ||
                   (exception?.InnerException != null && IsNetworkException(exception.InnerException));
        }

        public static bool IsTemporaryError(Exception exception)
        {
            if (IsNetworkException(exception))
                return true;

            if (exception is System.TimeoutException)
                return true;

            if (exception?.Message?.ToLower().Contains("timeout") == true)
                return true;

            if (exception?.Message?.ToLower().Contains("temporary") == true)
                return true;

            return false;
        }

        public static void SetDebugMode(bool isDebug)
        {
            _isDebugMode = isDebug;
        }

        // Extension method for easy error handling
        public static async Task<T> HandleAsync<T>(Func<Task<T>> operation, T defaultValue = default(T), Page page = null, string operationName = "Operation")
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                await HandleErrorAsync($"{operationName} Failed", ex, page);
                return defaultValue;
            }
        }

        public static async Task HandleAsync(Func<Task> operation, Page page = null, string operationName = "Operation")
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                await HandleErrorAsync($"{operationName} Failed", ex, page);
            }
        }
    }

    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    // Custom exception classes for better error categorization
    public class NetworkException : Exception
    {
        public NetworkException(string message) : base(message) { }
        public NetworkException(string message, Exception innerException) : base(message, innerException) { }
    }
}