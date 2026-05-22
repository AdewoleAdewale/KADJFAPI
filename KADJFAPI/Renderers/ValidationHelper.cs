using System;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace KADJFAPI.Renderers
{
    public static class ValidationHelper
    {
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsStrongPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return false;

            bool hasUpper = false, hasLower = false, hasDigit = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;

                if (hasUpper && hasLower && hasDigit) return true;
            }

            return false;
        }

        public static string GetPasswordStrengthMessage(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return "Password is required";

            if (password.Length < 6)
                return "Password must be at least 6 characters";

            if (password.Length < 8)
                return "Password should be at least 8 characters for better security";

            if (!IsStrongPassword(password))
                return "Consider using uppercase, lowercase, and numbers for stronger security";

            return "Strong password";
        }
    }

    /// <summary>
    /// Animation helper class
    /// </summary>
    public static class AnimationHelper
    {
        public static async Task PulseAnimation(View view, uint duration = 300)
        {
            try
            {
                await view.ScaleTo(1.1, duration / 2, Easing.CubicOut);
                await view.ScaleTo(1, duration / 2, Easing.CubicIn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pulse animation error: {ex.Message}");
            }
        }

        public static async Task ShakeAnimation(View view, uint duration = 300)
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    await view.TranslateTo(-10, 0, duration / 6);
                    await view.TranslateTo(10, 0, duration / 6);
                }
                await view.TranslateTo(0, 0, duration / 6);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shake animation error: {ex.Message}");
            }
        }

        public static async Task SlideInFromLeft(View view, uint duration = 500)
        {
            try
            {
                view.TranslationX = -view.Width;
                view.Opacity = 0;

                var translateTask = view.TranslateTo(0, 0, duration, Easing.CubicOut);
                var fadeTask = view.FadeTo(1, duration, Easing.Linear);

                await Task.WhenAll(translateTask, fadeTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slide in animation error: {ex.Message}");
            }
        }

        public static async Task SlideInFromRight(View view, uint duration = 500)
        {
            try
            {
                view.TranslationX = view.Width;
                view.Opacity = 0;

                var translateTask = view.TranslateTo(0, 0, duration, Easing.CubicOut);
                var fadeTask = view.FadeTo(1, duration, Easing.Linear);

                await Task.WhenAll(translateTask, fadeTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slide in animation error: {ex.Message}");
            }
        }

        public static async Task FadeInUp(View view, uint duration = 600)
        {
            try
            {
                view.TranslationY = 50;
                view.Opacity = 0;

                var translateTask = view.TranslateTo(0, 0, duration, Easing.CubicOut);
                var fadeTask = view.FadeTo(1, duration, Easing.Linear);

                await Task.WhenAll(translateTask, fadeTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fade in up animation error: {ex.Message}");
            }
        }

        public static async Task SuccessBounce(View view, uint duration = 400)
        {
            try
            {
                await view.ScaleTo(1.2, duration / 2, Easing.CubicOut);
                await view.ScaleTo(1, duration / 2, Easing.BounceOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Success bounce animation error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Network helper class
    /// </summary>
    public static class NetworkHelper
    {
        public static bool IsNetworkAvailable()
        {
            try
            {
                return Xamarin.Essentials.Connectivity.NetworkAccess == Xamarin.Essentials.NetworkAccess.Internet;
            }
            catch
            {
                return true; // Assume available if we can't check
            }
        }

        public static async Task<bool> CheckServerReachability(string url)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = await client.GetAsync(url);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// UI State helper
    /// </summary>
    public static class UIStateHelper
    {
        public static void SetInputError(View container, bool hasError)
        {
            try
            {
                if (container is Xamarin.Forms.PancakeView.PancakeView pancakeView)
                {
                    pancakeView.BorderColor = hasError
                        ? Color.FromHex("#E74C3C")
                        : Color.FromHex("#BDC3C7");

                    pancakeView.BorderThickness = 2;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UI state error: {ex.Message}");
            }
        }

        public static void SetInputSuccess(View container)
        {
            try
            {
                if (container is Xamarin.Forms.PancakeView.PancakeView pancakeView)
                {
                    pancakeView.BackgroundColor = Color.FromHex("#27AE60");
                    pancakeView.BorderThickness = 2;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UI success state error: {ex.Message}");
            }
        }

        public static void ResetInputState(View container)
        {
            try
            {
                if (container is Xamarin.Forms.PancakeView.PancakeView pancakeView)
                {
                    pancakeView.BorderColor = Color.FromHex("#BDC3C7");
                    pancakeView.BorderThickness = 1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UI reset state error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Security helper class
    /// </summary>
    public static class SecurityHelper
    {
        public static string SanitizeInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            return input.Trim()
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;")
                       .Replace("&", "&amp;")
                       .Replace("\"", "&quot;")
                       .Replace("'", "&#x27;");
        }

        public static bool IsInputSafe(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Check for common injection patterns
            string[] dangerousPatterns = {
                "<script", "javascript:", "onload=", "onerror=",
                "eval(", "exec(", "system(", "cmd(",
                "drop table", "delete from", "insert into",
                "update set", "union select"
            };

            string lowerInput = input.ToLower();
            foreach (string pattern in dangerousPatterns)
            {
                if (lowerInput.Contains(pattern))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Local storage helper (using Xamarin.Essentials.Preferences)
    /// </summary>
    public static class StorageHelper
    {
        private const string REMEMBER_EMAIL_KEY = "remember_email";
        private const string LAST_LOGIN_EMAIL_KEY = "last_login_email";

        public static void SaveLastLoginEmail(string email)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    Xamarin.Essentials.Preferences.Set(LAST_LOGIN_EMAIL_KEY, email);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save email error: {ex.Message}");
            }
        }

        public static string GetLastLoginEmail()
        {
            try
            {
                return Xamarin.Essentials.Preferences.Get(LAST_LOGIN_EMAIL_KEY, string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Get email error: {ex.Message}");
                return string.Empty;
            }
        }

        public static void SetRememberEmail(bool remember)
        {
            try
            {
                Xamarin.Essentials.Preferences.Set(REMEMBER_EMAIL_KEY, remember);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Set remember email error: {ex.Message}");
            }
        }

        public static bool GetRememberEmail()
        {
            try
            {
                return Xamarin.Essentials.Preferences.Get(REMEMBER_EMAIL_KEY, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Get remember email error: {ex.Message}");
                return false;
            }
        }

        public static void ClearStoredCredentials()
        {
            try
            {
                Xamarin.Essentials.Preferences.Remove(LAST_LOGIN_EMAIL_KEY);
                Xamarin.Essentials.Preferences.Remove(REMEMBER_EMAIL_KEY);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clear credentials error: {ex.Message}");
            }
        }
    }
}