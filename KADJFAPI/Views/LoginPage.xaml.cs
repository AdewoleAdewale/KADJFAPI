using KADJFAPI.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace KADJFAPI.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class LoginPage : ContentPage
    {
        // ── Static user session data ──────────────────────────────────────────
        public static string MyfullName { get; set; }
        public static string mymda { get; set; }
        public static string myemail { get; set; }
        public static string mycourt { get; set; }

        // ── State ─────────────────────────────────────────────────────────────
        private bool _isLoading = false;
        private bool _passwordVisible = false;   // tracks eye-toggle state
        private CancellationTokenSource _cancellationTokenSource;

        // Border colours (dark theme)
        private static readonly Color ColDefault = Color.FromHex("#1F4030");
        private static readonly Color ColFocused = Color.FromHex("#4D9E6A");
        private static readonly Color ColValid = Color.FromHex("#27AE60");
        private static readonly Color ColError = Color.FromHex("#E05252");

        // ─────────────────────────────────────────────────────────────────────
        //  CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────

        public LoginPage()
        {
            InitializeComponent();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAGE LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartEntranceAnimations();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cancellationTokenSource?.Cancel();
        }

        protected override bool OnBackButtonPressed()
        {
            try
            {
                if (_isLoading)
                {
                    _cancellationTokenSource?.Cancel();
                    return true;
                }
                return base.OnBackButtonPressed();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back button error: {ex.Message}");
                return base.OnBackButtonPressed();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ENTRANCE ANIMATIONS
        // ─────────────────────────────────────────────────────────────────────

        private async void StartEntranceAnimations()
        {
            try
            {
                // Reset
                HeaderSection.Opacity = 0;
                HeaderSection.TranslationY = -30;
                FormContainer.Opacity = 0;
                FormContainer.TranslationY = 60;
                FooterSection.Opacity = 0;
                FooterSection.TranslationY = 20;

                // Kick off orb rotation (fire-and-forget)
                _ = AnimateOrbs();

                // Staggered entrance
                await Task.Delay(150);
                await Task.WhenAll(
                    HeaderSection.FadeTo(1, 700, Easing.CubicOut),
                    HeaderSection.TranslateTo(0, 0, 700, Easing.CubicOut));

                // Seal pulse
                await SealFrame.ScaleTo(1.08, 250, Easing.CubicOut);
                await SealFrame.ScaleTo(1.00, 250, Easing.CubicIn);

                await Task.Delay(100);
                await Task.WhenAll(
                    FormContainer.FadeTo(1, 600, Easing.CubicOut),
                    FormContainer.TranslateTo(0, 0, 600, Easing.CubicOut));

                await AnimateFormElements();

                await Task.Delay(100);
                await Task.WhenAll(
                    FooterSection.FadeTo(1, 500, Easing.CubicOut),
                    FooterSection.TranslateTo(0, 0, 500, Easing.CubicOut));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Entrance animation error: {ex.Message}");
            }
        }

        private async Task AnimateOrbs()
        {
            try
            {
                await Task.WhenAll(
                    OrbTopRight.RotateTo(360, 30000),
                    OrbBottomLeft.RotateTo(-360, 40000));
            }
            catch { /* swallow — decorative only */ }
        }

        private async Task AnimateFormElements()
        {
            try
            {
                EmailSection.Opacity = 0;
                EmailSection.TranslationX = -40;
                PasswordSection.Opacity = 0;
                PasswordSection.TranslationX = 40;
                SignInButton.Opacity = 0;
                SignInButton.Scale = 0.88;

                var emailAnim = Task.WhenAll(
                    EmailSection.FadeTo(1, 380, Easing.CubicOut),
                    EmailSection.TranslateTo(0, 0, 380, Easing.CubicOut));

                await Task.Delay(120);

                var passAnim = Task.WhenAll(
                    PasswordSection.FadeTo(1, 380, Easing.CubicOut),
                    PasswordSection.TranslateTo(0, 0, 380, Easing.CubicOut));

                await Task.Delay(120);

                var btnAnim = Task.WhenAll(
                    SignInButton.FadeTo(1, 380, Easing.CubicOut),
                    SignInButton.ScaleTo(1, 380, Easing.BounceOut));

                await Task.WhenAll(emailAnim, passAnim, btnAnim);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Form elements animation: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PASSWORD VISIBILITY TOGGLE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Flips <c>Password.IsPassword</c> and swaps the eye icon.
        /// Wired to PasswordToggleBtn.Clicked in XAML.
        /// </summary>
        private async void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            try
            {
                _passwordVisible = !_passwordVisible;
                Password.IsPassword = !_passwordVisible;

                // Swap icon: open eye when visible, closed eye when hidden
                PasswordToggleBtn.Source = _passwordVisible ? "Openeyes" : "icons8eyes";

                // Micro-animation on the button
                await PasswordToggleBtn.ScaleTo(0.75, 80, Easing.CubicIn);
                await PasswordToggleBtn.ScaleTo(1.00, 80, Easing.CubicOut);

                // Keep focus on the entry so keyboard stays up
                Password.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Password toggle error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FOCUS EVENTS — border colour feedback
        // ─────────────────────────────────────────────────────────────────────

        private async void OnEmailFocused(object sender, FocusEventArgs e)
        {
            try
            {
                EmailContainer.BorderColor = ColFocused;
                EmailContainer.BorderThickness = 2;
                await EmailContainer.ScaleTo(1.015, 130, Easing.CubicOut);
            }
            catch { /* ignore */ }
        }

        private async void OnEmailUnfocused(object sender, FocusEventArgs e)
        {
            try
            {
                await EmailContainer.ScaleTo(1.0, 130, Easing.CubicOut);
                bool valid = IsValidEmail(Email.Text);
                EmailContainer.BorderColor = string.IsNullOrWhiteSpace(Email.Text)
                    ? ColDefault
                    : valid ? ColValid : ColError;
                EmailContainer.BorderThickness = 1;
            }
            catch { /* ignore */ }
        }

        private async void OnPasswordFocused(object sender, FocusEventArgs e)
        {
            try
            {
                PasswordContainer.BorderColor = ColFocused;
                PasswordContainer.BorderThickness = 2;
                await PasswordContainer.ScaleTo(1.015, 130, Easing.CubicOut);
            }
            catch { /* ignore */ }
        }

        private async void OnPasswordUnfocused(object sender, FocusEventArgs e)
        {
            try
            {
                await PasswordContainer.ScaleTo(1.0, 130, Easing.CubicOut);
                bool valid = IsValidPassword(Password.Text);
                PasswordContainer.BorderColor = string.IsNullOrWhiteSpace(Password.Text)
                    ? ColDefault
                    : valid ? ColValid : ColError;
                PasswordContainer.BorderThickness = 1;
            }
            catch { /* ignore */ }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  VALIDATION
        // ─────────────────────────────────────────────────────────────────────

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                return new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")
                    .IsMatch(email);
            }
            catch { return false; }
        }

        private static bool IsValidPassword(string password)
            => !string.IsNullOrWhiteSpace(password) && password.Length >= 6;

        /// <summary>
        /// Validates both fields, shows inline error labels, and shakes on failure.
        /// Returns <c>true</c> only when both are valid.
        /// </summary>
        private async Task<bool> ValidateInputs()
        {
            bool isValid = true;

            // ── Email ──────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(Email.Text))
            {
                SetFieldError(EmailContainer, EmailErrorLabel, "Email address is required");
                await ShakeAsync(EmailContainer);
                isValid = false;
            }
            else if (!IsValidEmail(Email.Text))
            {
                SetFieldError(EmailContainer, EmailErrorLabel, "Please enter a valid email address");
                await ShakeAsync(EmailContainer);
                isValid = false;
            }
            else
            {
                SetFieldValid(EmailContainer, EmailErrorLabel);
            }

            // ── Password ───────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(Password.Text))
            {
                SetFieldError(PasswordContainer, PasswordErrorLabel, "Password is required");
                await ShakeAsync(PasswordContainer);
                isValid = false;
            }
            else if (!IsValidPassword(Password.Text))
            {
                SetFieldError(PasswordContainer, PasswordErrorLabel,
                    "Password must be at least 6 characters");
                await ShakeAsync(PasswordContainer);
                isValid = false;
            }
            else
            {
                SetFieldValid(PasswordContainer, PasswordErrorLabel);
            }

            return isValid;
        }

        private void SetFieldError(
            Xamarin.Forms.PancakeView.PancakeView container,
            Label errorLabel,
            string message)
        {
            container.BorderColor = ColError;
            container.BorderThickness = 2;
            errorLabel.Text = message;
            errorLabel.IsVisible = true;
        }

        private void SetFieldValid(
            Xamarin.Forms.PancakeView.PancakeView container,
            Label errorLabel)
        {
            container.BorderColor = ColValid;
            container.BorderThickness = 1;
            errorLabel.IsVisible = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ANIMATION HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static async Task ShakeAsync(View element)
        {
            try
            {
                await element.TranslateTo(-9, 0, 45);
                await element.TranslateTo(9, 0, 45);
                await element.TranslateTo(-6, 0, 40);
                await element.TranslateTo(6, 0, 40);
                await element.TranslateTo(0, 0, 35);
            }
            catch { /* ignore */ }
        }

        private async Task AnimateButtonPress()
        {
            try
            {
                await SignInButton.ScaleTo(0.96, 90, Easing.CubicIn);
                await SignInButton.ScaleTo(1.00, 90, Easing.CubicOut);
            }
            catch { /* ignore */ }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LOADING STATE
        // ─────────────────────────────────────────────────────────────────────

        private async Task SetLoadingState(bool isLoading)
        {
            try
            {
                _isLoading = isLoading;

                LoadingIndicator.IsVisible = isLoading;
                LoadingIndicator.IsRunning = isLoading;
                SignInLabel.Text = isLoading ? "SIGNING IN..." : "SIGN IN";
                SignInButton.IsEnabled = !isLoading;
                Email.IsEnabled = !isLoading;
                Password.IsEnabled = !isLoading;
                PasswordToggleBtn.IsEnabled = !isLoading;

                if (isLoading)
                    await SignInButton.FadeTo(0.70, 150);
                else
                    await SignInButton.FadeTo(1.00, 150);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetLoadingState error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SIGN-IN BUTTON HANDLER
        // ─────────────────────────────────────────────────────────────────────

        private async void Button_Clicked(object sender, EventArgs e)
        {
            if (_isLoading) return;

            try
            {
                await AnimateButtonPress();

                if (!await ValidateInputs())
                    return;

                await SetLoadingState(true);

                _cancellationTokenSource = new CancellationTokenSource();
                await PerformLogin(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Cancelled — do nothing
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Button_Clicked error: {ex.Message}");
                await DisplayAlert("Error",
                    "An unexpected error occurred. Please try again.", "OK");
            }
            finally
            {
                await SetLoadingState(false);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LOGIN LOGIC
        // ─────────────────────────────────────────────────────────────────────

        private async Task PerformLogin(CancellationToken cancellationToken)
        {
            try
            {
                string emailVal = Email.Text?.Trim() ?? string.Empty;
                string passwordVal = Password.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(emailVal) || string.IsNullOrEmpty(passwordVal))
                {
                    await DisplayAlert("Error", "Please fill in all required fields.", "OK");
                    return;
                }

                var handler = new HttpClientHandler
                {
                    // ✅ Proper way for older Xamarin / .NET Standard
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,

                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                     System.Security.Authentication.SslProtocols.Tls11 |
                     System.Security.Authentication.SslProtocols.Tls,

                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(45);
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("KADJFAPI-App");

                    cancellationToken.ThrowIfCancellationRequested();

                    // === POST REQUEST WITH FORM DATA ===
                    var content = new FormUrlEncodedContent(new[]
                    {
                new KeyValuePair<string, string>("Email", emailVal),
                new KeyValuePair<string, string>("Password", passwordVal)
            });

                    using (var response = await client.GetAsync(ApiConfig.Login(emailVal, passwordVal), cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var jsonContent = await response.Content.ReadAsStringAsync();

                        // Log response for debugging
                        System.Diagnostics.Debug.WriteLine($"Response Status: {(int)response.StatusCode} {response.StatusCode}");
                        System.Diagnostics.Debug.WriteLine($"Response Content: {jsonContent}");

                        if (!response.IsSuccessStatusCode)
                        {
                            await DisplayAlert("Access Denied",
                                $"Server returned: {(int)response.StatusCode} - {response.StatusCode}\n\n" +
                                "Please contact support if this continues.", "OK");
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(jsonContent))
                        {
                            await DisplayAlert("Error", "Empty response from server.", "OK");
                            return;
                        }

                        var result = JsonConvert.DeserializeObject<LoginResponse>(jsonContent);

                        if (result == null)
                        {
                            await DisplayAlert("Error", "Invalid server response.", "OK");
                            return;
                        }

                        await ProcessLoginResponse(result);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                await DisplayAlert("Timeout", "Connection timed out. Check your internet.", "OK");
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Network Error: {ex.Message}");
                await DisplayAlert("Connection Error", "Unable to reach server. Check internet.", "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login Error: {ex.Message}\n{ex.StackTrace}");
                await DisplayAlert("Error", "An unexpected error occurred.", "OK");
            }
        }
        private async Task ProcessLoginResponse(LoginResponse result)
        {
            try
            {
                if (result.status_code == "00")
                {
                    // ── Success ──────────────────────────────────────────────
                    await AnimateSuccessState();
                    MyfullName = result.name;
                    mymda = result.mda;
                    myemail = result.email;
                    // new API returns mda but no separate court — fall back so mycourt is never null
                    mycourt = string.IsNullOrWhiteSpace(result.court) ? result.mda : result.court;

                    Application.Current.MainPage =
                        new NavigationPage(new Views.Dashboard());
                }
                else
                {
                    // ── Failure ──────────────────────────────────────────────
                    await AnimateErrorState();

                    string msg = !string.IsNullOrWhiteSpace(result.message)
                        ? result.message
                        : "Login failed. Please check your credentials and try again.";

                    await DisplayAlert("Login Failed", msg, "TRY AGAIN");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessLoginResponse error: {ex.Message}");
                await DisplayAlert("Error",
                    "An error occurred while processing the login response.", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SUCCESS / ERROR STATE ANIMATIONS
        // ─────────────────────────────────────────────────────────────────────

        private async Task AnimateSuccessState()
        {
            try
            {
                SignInLabel.Text = "✓  SIGNED IN";

                await Task.WhenAll(
                    SignInButton.ScaleTo(1.04, 180, Easing.BounceOut),
                    SignInButton.FadeTo(0.9, 180));

                await Task.Delay(400);
                await SignInButton.ScaleTo(1.0, 180, Easing.CubicOut);
            }
            catch { /* ignore */ }
        }

        private async Task AnimateErrorState()
        {
            try
            {
                EmailContainer.BorderColor = ColError;
                PasswordContainer.BorderColor = ColError;

                await ShakeAsync(FormContainer);

                await Task.Delay(900);
                ResetBorderColours();
            }
            catch { /* ignore */ }
        }

        private void ResetBorderColours()
        {
            EmailContainer.BorderColor = ColDefault;
            PasswordContainer.BorderColor = ColDefault;
            EmailContainer.BorderThickness = 1;
            PasswordContainer.BorderThickness = 1;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  OTHER HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private async void OnForgotPasswordTapped(object sender, EventArgs e)
        {
            try
            {
                await ForgotPasswordLabel.ScaleTo(0.9, 90);
                await ForgotPasswordLabel.ScaleTo(1.0, 90);

                await DisplayAlert("Forgot Password",
                    "Please contact support to reset your password:\n\n" +
                    "📞 +234-803-052-3208\n" +
                    "📧 support@kadunajudiciary.gov.ng",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ForgotPassword error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            try
            {
                await DisplayAlert("Support Contact",
                    "Need assistance? Reach out to our support team:\n\n" +
                    "📞 Phone: +234-803-052-3208\n" +
                    "📧 Email: support@kadunajudiciary.gov.ng\n" +
                    "🕐 Hours: Mon–Fri  8AM – 5PM",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Support tap error: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RESPONSE MODEL
    // ══════════════════════════════════════════════════════════════════════════

    public class LoginResponse
    {
        public string status_code { get; set; }
        public string message { get; set; }
        public string name { get; set; }
        public string mda { get; set; }
        public string email { get; set; }
        public string court { get; set; }
        public string super_agent { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PASSWORD TOGGLE TRIGGER (kept for backward-compat — no longer primary)
    // ══════════════════════════════════════════════════════════════════════════


    /// <summary>
    /// Legacy TriggerAction kept in case it is referenced elsewhere in the project.
    /// The primary toggle is now handled by <see cref="LoginPage.OnPasswordToggleClicked"/>.
    /// </summary>


    public class ShowPasswordTriggerAction : TriggerAction<ImageButton>
    {
        public string ShowIcon { get; set; }
        public string HideIcon { get; set; }
        public bool HidePassword { get; set; } = true;

        protected override async void Invoke(ImageButton sender)
        {
            try
            {
                HidePassword = !HidePassword;
                sender.Source = HidePassword ? HideIcon : ShowIcon;
                await sender.ScaleTo(0.8, 90);
                await sender.ScaleTo(1.0, 90);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowPasswordTriggerAction error: {ex.Message}");
            }
        }
    }
}