using Acr.UserDialogs;
using Android.Bluetooth;
using KADJFAPI.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace KADJFAPI.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Dashboard : ContentPage
    {
        // ── Data ──────────────────────────────────────────────────────────────
        private List<MDAData> enums2;
        private MDAData selectedCourt;
        private bool _isAnimating = false;

        // FIX: CancellationTokenSource for animations is kept, but OnDisappearing
        //      now cancels then disposes in two separate try/catch blocks so a
        //      double-dispose can never crash the app.
        private CancellationTokenSource _animationCts;

        private List<HistoryData> _allTransactions = new List<HistoryData>();

        // ── Animation constants ───────────────────────────────────────────────
        private const int STAGGER_DELAY = 100;
        private const int ANIMATION_DURATION = 600;
        private const int QUICK_ANIMATION = 200;
        private const int FLOATING_ANIMATION = 800;

        // ── Bluetooth printer (58 mm paper) ──────────────────────────────────
        private readonly BluetoothPrinterService _printer =
            new BluetoothPrinterService(use80mm: false);

        // ─────────────────────────────────────────────────────────────────────
        //  INNER DATA MODELS
        // ─────────────────────────────────────────────────────────────────────

        class HistoryData
        {
            public string taxId { get; set; }
            public string fullName { get; set; }
            public string paymentItem { get; set; }
            public string paymentReference { get; set; }
            public string amount { get; set; }
            public string dateRecorded { get; set; }
            public string email { get; set; }
            public string phoneNumber { get; set; }
            public string status { get; set; }
            public string mda { get; set; }
            public string payer_name { get; set; }
            public string payer_email { get; set; }
            public string date_of_payment { get; set; }
            public string sessionId { get; set; }

            public string ServiceNameTruncated =>
                paymentItem?.Length > 30 ? paymentItem.Substring(0, 30) : paymentItem;
        }

        class HistoryDataHeaderFooter
        {
            public List<HistoryData> HD { get; set; }
            public string Intro => $" You have Performed a total of {HD.Count} transactions recently";
            public string Summary => $" You have Performed a total of {HD.Count} transactions";
            public decimal Size => HD.Count;
        }

        internal class MDAData
        {
            public string name { get; set; }
            public string id { get; set; }
            public string code { get; set; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────

        public Dashboard()
        {
            InitializeComponent();
            SetupInitialState();
            InitializeStatsLabels();

            // FIX: create a fresh CTS — only Cancel() is called in OnDisappearing.
            _animationCts = new CancellationTokenSource();

            if (!_isAnimating)
            {
                _isAnimating = true;
                _ = StartEntryAnimations();
                _ = LoadDashboardData();
                _isAnimating = false;
            }
        }

        private void InitializeStatsLabels()
        {
            TotalTransactionsLabel = this.FindByName<Label>("TotalTransactionsLabel");
            PendingTransactionsLabel = this.FindByName<Label>("PendingTransactionsLabel");
            CompletedTransactionsLabel = this.FindByName<Label>("CompletedTransactionsLabel");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAGE LIFECYCLE
        //  FIX: Cancel and Dispose are in separate try/catch blocks.
        //       If Cancel throws (e.g. already cancelled), Dispose still runs,
        //       and we null out the reference so a second call is a no-op.
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try { _animationCts?.Cancel(); } catch { }
            try { _animationCts?.Dispose(); _animationCts = null; } catch { }
            try { _printer?.Dispose(); } catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INITIAL ANIMATION STATE
        // ─────────────────────────────────────────────────────────────────────

        private void SetupInitialState()
        {
            HeaderSection.Opacity = 0;
            HeaderSection.TranslationY = -100;

            QuickActionsSection.Opacity = 0;
            QuickActionsSection.TranslationY = 50;

            ServicesSection.Opacity = 0;
            ServicesSection.TranslationY = 50;

            RecentInvoicesSection.Opacity = 0;
            RecentInvoicesSection.TranslationY = 100;

            FloatingButtonsContainer.Opacity = 0;
            FloatingButtonsContainer.TranslationX = 100;

            UserNameLabel.Text = LoginPage.MyfullName?.ToUpper() ?? "ADMINISTRATOR";
            MdaLabel.Text = LoginPage.mymda?.ToUpper() ?? "YOBE JUDICIARY";

            WelcomeSection.Opacity = 0;
            DepartmentBadge.Opacity = 0;
            StatsGrid.Opacity = 0;
            ProfileFrame.Scale = 0;

            GenerateInvoiceCard.Scale = 0;
            VerifyRRRCard.Scale = 0;
            CheckHistoryCard.Scale = 0;

            CourtsServiceCard.Scale = 0;
            PaymentServiceCard.Scale = 0;
            PrinterServiceCard.Scale = 0;
            SupportServiceCard.Scale = 0;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ENTRY ANIMATIONS
        //  FIX: Helper that safely reads the token — returns a default if the
        //       CTS has already been disposed (happens when user navigates away
        //       before animations finish).
        // ─────────────────────────────────────────────────────────────────────

        private CancellationToken SafeToken
        {
            get
            {
                try { return _animationCts?.Token ?? CancellationToken.None; }
                catch { return CancellationToken.None; }
            }
        }

        private async Task StartEntryAnimations()
        {
            try
            {
                var token = SafeToken;

                var headerAnim = Task.WhenAll(
                    HeaderSection.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut),
                    HeaderSection.TranslateTo(0, 0, ANIMATION_DURATION, Easing.BounceOut));

                await Task.Delay(200, token);
                var headerElemsTask = AnimateHeaderElements(token);

                await Task.Delay(400, token);
                var sectionsTask = AnimateSections(token);

                await Task.Delay(800, token);
                _ = AnimateFloatingButtons(token);

                await Task.WhenAll(headerAnim, headerElemsTask, sectionsTask);
            }
            catch (OperationCanceledException) { /* page left — fine */ }
            catch (ObjectDisposedException) { /* CTS disposed — fine */ }
        }

        private async Task AnimateHeaderElements(CancellationToken token)
        {
            try
            {
                var welcomeTask = WelcomeSection.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut);
                await Task.Delay(STAGGER_DELAY, token);

                var badgeTask = DepartmentBadge.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut);
                await Task.Delay(STAGGER_DELAY, token);

                StatsGrid.Scale = 0.8;
                var statsTask = Task.WhenAll(
                    StatsGrid.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut),
                    StatsGrid.ScaleTo(1, ANIMATION_DURATION, Easing.BounceOut));
                await Task.Delay(STAGGER_DELAY, token);

                var profileTask = ProfileFrame.ScaleTo(1, FLOATING_ANIMATION, Easing.BounceOut);
                await Task.WhenAll(welcomeTask, badgeTask, statsTask, profileTask);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task AnimateSections(CancellationToken token)
        {
            try
            {
                var quickActionsTask = Task.WhenAll(
                    QuickActionsSection.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut),
                    QuickActionsSection.TranslateTo(0, 0, ANIMATION_DURATION, Easing.CubicOut));

                await Task.Delay(200, token);
                var quickCardsTask = AnimateQuickActionCards(token);

                await Task.Delay(300, token);
                var servicesTask = Task.WhenAll(
                    ServicesSection.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut),
                    ServicesSection.TranslateTo(0, 0, ANIMATION_DURATION, Easing.CubicOut));

                await Task.Delay(200, token);
                var serviceCardsTask = AnimateServiceCards(token);

                await Task.Delay(400, token);
                var invoicesTask = Task.WhenAll(
                    RecentInvoicesSection.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut),
                    RecentInvoicesSection.TranslateTo(0, 0, ANIMATION_DURATION, Easing.BounceOut));

                await Task.WhenAll(
                    quickActionsTask, quickCardsTask,
                    servicesTask, serviceCardsTask,
                    invoicesTask);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task AnimateQuickActionCards(CancellationToken token)
        {
            try
            {
                var cards = new[] { GenerateInvoiceCard, VerifyRRRCard, CheckHistoryCard };
                for (int i = 0; i < cards.Length; i++)
                {
                    if (token.IsCancellationRequested) break;
                    _ = cards[i].ScaleTo(1, ANIMATION_DURATION, Easing.BounceOut);
                    if (i < cards.Length - 1) await Task.Delay(STAGGER_DELAY, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task AnimateServiceCards(CancellationToken token)
        {
            try
            {
                var cards = new[]
                {
                    CourtsServiceCard, PaymentServiceCard,
                    PrinterServiceCard, SupportServiceCard
                };
                for (int i = 0; i < cards.Length; i++)
                {
                    if (token.IsCancellationRequested) break;
                    _ = cards[i].ScaleTo(1, ANIMATION_DURATION, Easing.BounceOut);
                    if (i < cards.Length - 1) await Task.Delay(STAGGER_DELAY, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task AnimateFloatingButtons(CancellationToken token)
        {
            try
            {
                await Task.WhenAll(
                    FloatingButtonsContainer.FadeTo(1, FLOATING_ANIMATION, Easing.CubicOut),
                    FloatingButtonsContainer.TranslateTo(0, 0, FLOATING_ANIMATION, Easing.BounceOut));

                _ = StartFloatingButtonBreathingAnimation(token);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task StartFloatingButtonBreathingAnimation(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.WhenAll(
                        PrintFloatingButton.ScaleTo(1.1, 2000, Easing.SinInOut),
                        LogoutFloatingButton.ScaleTo(1.1, 2200, Easing.SinInOut));

                    if (token.IsCancellationRequested) break;

                    await Task.WhenAll(
                        PrintFloatingButton.ScaleTo(1.0, 2000, Easing.SinInOut),
                        LogoutFloatingButton.ScaleTo(1.0, 2200, Easing.SinInOut));

                    if (token.IsCancellationRequested) break;
                    await Task.Delay(3000, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Safe — reset scale on the UI thread
                Device.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        PrintFloatingButton.Scale = 1.0;
                        LogoutFloatingButton.Scale = 1.0;
                    }
                    catch { /* view may already be gone */ }
                });
            }
            catch (ObjectDisposedException) { /* CTS disposed after cancel — safe to ignore */ }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  DASHBOARD DATA
        // ─────────────────────────────────────────────────────────────────────

        private async Task LoadDashboardData()
        {
            try { await LoadRecentTransaction(); }
            catch { await DisplayAlert("Error", "Failed to load dashboard data. Please try again.", "OK"); }
        }

        private async Task LoadRecentTransaction()
        {
            using (UserDialogs.Instance.Loading("Fetching Recent Transactions, Please Wait...", null, null, true))
            {
                await Task.Delay(1000);

                // was: "https://kadjud.osoftpay.net/Api/KadunaJFAPI/InvoiceHistory?Email=" + LoginPage.myemail;
                string url = ApiConfig.InvoiceHistory(LoginPage.mymda);

                try
                {
                    await Task.Delay(1000);
                    var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            (sender, certificate, chain, sslPolicyErrors) => true,
                        SslProtocols =
                            System.Security.Authentication.SslProtocols.Tls12 |
                            System.Security.Authentication.SslProtocols.Tls |
                            System.Security.Authentication.SslProtocols.Tls11
                    };

                    // FIX: Timeout on HttpClient — no CancellationTokenSource required.
                    using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                    using (var response = await client.GetAsync(url))
                    using (var content = response.Content)
                    {
                        var json = await content.ReadAsStringAsync();
                        var items = JsonConvert.DeserializeObject<List<HistoryData>>(json);

                        _allTransactions = items ?? new List<HistoryData>();
                        UpdateStatsDisplay();

                        var sorted = items.OrderBy(x => x.dateRecorded).Take(10);

                        listView.Opacity = 0;
                        listView.ItemsSource = sorted;
                        await listView.FadeTo(1, 600, Easing.CubicOut);
                    }
                }
                catch (TaskCanceledException)
                {
                    // HttpClient timeout
                    UserDialogs.Instance.Toast(
                        "Loading timed out. Please check your internet connection.",
                        TimeSpan.FromSeconds(8));
                    UpdateStatsDisplay();
                }
                catch (Exception exe)
                {
                    UserDialogs.Instance.Toast(
                        "Failed to load recent transactions. Please check your internet connection.",
                        TimeSpan.FromSeconds(8));
                    LogError("LoadRecentTransaction", exe);
                    UpdateStatsDisplay();
                }
            }
        }

        private void UpdateStatsDisplay()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (_allTransactions != null && _allTransactions.Any())
                    {
                        int total = _allTransactions.Count;
                        int completed = _allTransactions.Count(t =>
                            !string.IsNullOrEmpty(t.status) &&
                            (t.status.ToLower().Contains("paid") ||
                             t.status.ToLower().Contains("completed") ||
                             t.status.ToLower().Contains("successful")));

                        int pending = _allTransactions.Count(t =>
                            !string.IsNullOrEmpty(t.status) &&
                            (t.status.ToLower().Contains("pending") ||
                             t.status.ToLower().Contains("processing") ||
                             t.status.ToLower().Contains("initiated")));

                        if (TotalTransactionsLabel != null) TotalTransactionsLabel.Text = total.ToString();
                        if (PendingTransactionsLabel != null) PendingTransactionsLabel.Text = pending.ToString();
                        if (CompletedTransactionsLabel != null) CompletedTransactionsLabel.Text = completed.ToString();
                    }
                    else
                    {
                        if (TotalTransactionsLabel != null) TotalTransactionsLabel.Text = "0";
                        if (PendingTransactionsLabel != null) PendingTransactionsLabel.Text = "0";
                        if (CompletedTransactionsLabel != null) CompletedTransactionsLabel.Text = "0";
                    }

                    AnimateStatsUpdate();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatsDisplay error: {ex.Message}");
            }
        }

        private async void AnimateStatsUpdate()
        {
            try
            {
                if (TotalTransactionsLabel != null)
                {
                    await TotalTransactionsLabel.ScaleTo(1.2, 200, Easing.CubicOut);
                    await TotalTransactionsLabel.ScaleTo(1.0, 200, Easing.CubicOut);
                }
                await Task.Delay(100);
                if (PendingTransactionsLabel != null)
                {
                    await PendingTransactionsLabel.ScaleTo(1.2, 200, Easing.CubicOut);
                    await PendingTransactionsLabel.ScaleTo(1.0, 200, Easing.CubicOut);
                }
                await Task.Delay(100);
                if (CompletedTransactionsLabel != null)
                {
                    await CompletedTransactionsLabel.ScaleTo(1.2, 200, Easing.CubicOut);
                    await CompletedTransactionsLabel.ScaleTo(1.0, 200, Easing.CubicOut);
                }
            }
            catch (Exception ex)
            { System.Diagnostics.Debug.WriteLine($"AnimateStatsUpdate error: {ex.Message}"); }
        }

        public async Task RefreshStats() => await LoadRecentTransaction();

        // ─────────────────────────────────────────────────────────────────────
        //  TEST PRINT
        //  FIX: No CancellationTokenSource — Task.WhenAny provides the 30 s timeout.
        // ─────────────────────────────────────────────────────────────────────

        private async Task CallPrinterAsync()
        {
            try
            {
                using (UserDialogs.Instance.Loading("Connecting to Printer...", null, null, true))
                {
                    var printTask = _printer.PrintTestPageAsync();
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                    var finished = await Task.WhenAny(printTask, timeoutTask);

                    if (finished == timeoutTask)
                    {
                        await DisplayAlert("Printer Error",
                            "Print timed out. Check that the printer is powered on and paired.", "OK");
                        return;
                    }

                    await printTask;   // surface any PrinterException
                }

                await DisplayAlert("Print Test",
                    "Test page sent successfully. Check your printer output.", "OK");
            }
            catch (PrinterException pex)
            {
                await DisplayAlert("Printer Error", pex.Message, "OK");
                System.Diagnostics.Debug.WriteLine($"[Dashboard] PrinterException: {pex}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Printer Error",
                    "Could not connect to printer. Ensure it is switched on and paired.", "OK");
                LogError("CallPrinterAsync", ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  NAVIGATION HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private async void TapGestureRecognizer_Tapped_3(object sender, EventArgs e)
        {
            await AnimateCardPress((View)sender);
            using (UserDialogs.Instance.Loading("Connecting, Please Wait...", null, null, true))
            {
                await Task.Delay(500);
                await Navigation.PushAsync(new Views.Verify());
            }
        }

        private async void TapGestureRecognizer_Tapped_4(object sender, EventArgs e)
        {
            await AnimateCardPress((View)sender);
            await Navigation.PushModalAsync(new Payment());
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            try
            {
                if (sender is Image img) await AnimateImagePress(img);

                bool confirmed = await DisplayAlert(
                    "Logout Confirmation",
                    "Are you sure you want to logout?",
                    "Yes", "No");

                if (confirmed)
                {
                    Preferences.Remove("IsLoggedIn");
                    Preferences.Remove("UserToken");
                    Xamarin.Forms.Application.Current.MainPage =
                        new Xamarin.Forms.NavigationPage(new LoginPage());
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FLOATING BUTTON HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            var view = sender as View;
            if (view != null)
            {
                await AnimateButtonPress(view);
                await AnimateFloatingButton(view);
            }
            await CallPrinterAsync();
        }

        private async void TapGestureRecognizer_Tapped_2(object sender, EventArgs e)
        {
            var view = sender as View;
            if (view != null) await AnimateFloatingButton(view);

            var result = await DisplayAlert("Logout",
                "Are you sure you want to logout?", "Yes", "Cancel");

            if (result)
            {
                await this.FadeTo(0, 500, Easing.CubicIn);
                Preferences.Remove("IsLoggedIn");
                Preferences.Remove("UserToken");
                Xamarin.Forms.Application.Current.MainPage =
                    new Xamarin.Forms.NavigationPage(new LoginPage());
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SUPPORT
        // ─────────────────────────────────────────────────────────────────────

        private void OnCourtSelected(object sender, EventArgs e)
        {
            var picker = (Picker)sender;
            if (picker.SelectedIndex > 0)
                selectedCourt = enums2[picker.SelectedIndex - 1];
        }

        private async void LoadDashboard()
        {
            await AnimateHeaderElements();
            await LoadRecentTransaction();
            await AnimateSections();
        }

        private async Task AnimateHeaderElements()
        {
            await WelcomeLabel.FadeTo(1, 800, Easing.CubicOut); await Task.Delay(200);
            await UserNameLabel.FadeTo(1, 600, Easing.CubicOut); await Task.Delay(100);
            await MdaLabel.FadeTo(1, 600, Easing.CubicOut);
            await ProfileFrame.ScaleTo(1, 800, Easing.BounceOut);
        }

        private async Task AnimateSections()
        {
            await Task.WhenAll(
                QuickActionsSection.FadeTo(1, 800, Easing.CubicOut),
                QuickActionsSection.TranslateTo(0, 0, 800, Easing.CubicOut));
        }

        private async void OnBookConsultationClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushModalAsync(new Payment());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navigation Error", $"Unable to open payment page: {ex.Message}", "OK");
            }
        }

        private async void OnSupportClicked(object sender, EventArgs e)
        {
            if (sender is Button btn) await AnimateButtonPress(btn);

            var action = await DisplayActionSheet("Contact Support", "Cancel", null,
                "📞 Call Support",
                "💬 WhatsApp Support",
                "📧 Email Support",
                "ℹ️ About App");

            switch (action)
            {
                case "📞 Call Support": await CallSupport(); break;
                case "💬 WhatsApp Support": await WhatsAppSupport(); break;
                case "📧 Email Support": await EmailSupport(); break;
                case "ℹ️ About App": await ShowAboutInfo(); break;
            }
        }

        private async Task CallSupport()
        {
            try { PhoneDialer.Open("+2348030523208"); }
            catch { await DisplayAlert("Error", "Unable to make call. Dial +234-803-052-3208.", "OK"); }
        }

        private async Task WhatsAppSupport()
        {
            try
            {
                var msg = Uri.EscapeDataString(
                    $"Hello, I need support with KadunaJudiciary app. User: {LoginPage.MyfullName}");
                await Launcher.OpenAsync($"https://wa.me/2348030523208?text={msg}");
            }
            catch { await DisplayAlert("Error", "Unable to open WhatsApp. Contact +234-803-052-3208.", "OK"); }
        }

        private async Task EmailSupport()
        {
            try
            {
                await Email.ComposeAsync(new EmailMessage
                {
                    Subject = "KadunaJudiciary App Support Request",
                    Body = $"Hello Support,\n\nUser: {LoginPage.MyfullName}\n" +
                              $"MDA: {LoginPage.mymda}\n" +
                              $"Date: {DateTime.Now:dd MMM yyyy HH:mm}\n\n" +
                              "[Describe your issue here]",
                    To = new List<string> { "support@kadunajudiciary.ng" }
                });
            }
            catch { await DisplayAlert("Error", "Cannot open email. Write to support@kadunajudiciary.ng.", "OK"); }
        }

        private async Task ShowAboutInfo()
        {
            await DisplayAlert("About KadunaJudiciary",
                $"KadunaJudiciary Mobile App\nVersion: 3.0.0\n" +
                $"Powered by OSOFTPAY\n\n" +
                $"Support:\n📞 +234-803-052-3208\n📧 support@kadunajudiciary.ng",
                "OK");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ANIMATION HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private async Task AnimateButtonPress(View button)
        {
            await button.ScaleTo(0.95, 100, Easing.CubicOut);
            await button.ScaleTo(1.0, 100, Easing.CubicOut);
        }

        private async Task AnimateCardPress(View card)
        {
            await Task.WhenAll(card.ScaleTo(0.98, 100, Easing.CubicOut), card.FadeTo(0.8, 100, Easing.CubicOut));
            await Task.WhenAll(card.ScaleTo(1.0, 100, Easing.CubicOut), card.FadeTo(1.0, 100, Easing.CubicOut));
        }

        private async Task AnimateImagePress(View image)
        {
            await image.RotateTo(15, 100, Easing.CubicOut);
            await image.RotateTo(0, 100, Easing.CubicOut);
        }

        private async Task AnimateFloatingButton(View button)
        {
            await Task.WhenAll(button.ScaleTo(0.85, 100, Easing.CubicOut), button.FadeTo(0.7, 100, Easing.CubicOut));
            await Task.WhenAll(button.ScaleTo(1.1, 100, Easing.CubicOut), button.FadeTo(1.0, 100, Easing.CubicOut));
            await button.ScaleTo(1.0, 100, Easing.CubicOut);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BACK BUTTON
        // ─────────────────────────────────────────────────────────────────────

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var result = await DisplayAlert(
                    "NOTIFICATION", "Do you really want to exit?", "Yes", "No");
                if (result) System.Environment.Exit(0);
            });
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LISTVIEW — TRANSACTION HISTORY
        // ─────────────────────────────────────────────────────────────────────

        private async void listView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            try
            {
                if (e.SelectedItem is HistoryData t)
                    await ShowTransactionDetails(t);

                ((ListView)sender).SelectedItem = null;
            }
            catch (Exception ex) { LogError("listView_ItemSelected", ex); }
        }

        private async Task ShowTransactionDetails(HistoryData t)
        {
            try
            {
                var details =
                    $"Payer Name: {t.payer_name}\n" +
                    $"Amount: {t.amount}\n" +
                    $"Reference: {t.paymentReference}\n" +
                    $"Date: {t.dateRecorded}\n" +
                    $"Payer Email: {t.payer_email}\n" +
                    $"Tax ID: {t.taxId}\n" +
                    $"Date Of Payment: {t.date_of_payment}\n" +
                    $"Payment Item: {t.paymentItem}\n" +
                    $"Phone: {t.phoneNumber}\n" +
                    $"Status: {t.status}\n" +
                    $"MDA: {t.mda}";

                await DisplayAlert("Transaction Details", details, "OK");
            }
            catch (Exception ex) { LogError("ShowTransactionDetails", ex); }
        }

        private async void TapGestureRecognizer_Tapped_6(object sender, EventArgs e)
        {
            try
            {
                string token = string.Empty;

                if (sender is View v && v.BindingContext is HistoryData ht)
                    token = ht.paymentReference;
                else if (sender is Label lbl)
                    token = lbl.Text;
                else if (sender is View sv)
                {
                    var parent = sv.Parent;
                    while (parent != null)
                    {
                        if (parent.BindingContext is HistoryData pht)
                        { token = pht.paymentReference; break; }
                        parent = parent.Parent;
                    }
                }

                if (!string.IsNullOrEmpty(token))
                {
                    await Clipboard.SetTextAsync(token);
                    UserDialogs.Instance.Toast(
                        $"Invoice number copied to clipboard: {token}",
                        TimeSpan.FromSeconds(3));

                    if (sender is Label labelSender)
                    {
                        var original = labelSender.TextColor;
                        labelSender.TextColor = Color.Red;
                        await Task.Delay(500);
                        labelSender.TextColor = original;
                    }
                }
                else
                {
                    UserDialogs.Instance.Toast("No invoice number found to copy.",
                        TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception ex)
            {
                UserDialogs.Instance.Toast($"Failed to copy: {ex.Message}", TimeSpan.FromSeconds(3));
            }
        }

        private async void TapGestureRecognizer_Tapped_5(object sender, EventArgs e)
        {
            await AnimateCardPress((View)sender);
            await Navigation.PushAsync(new Generate());
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            await DisplayAlert("Court",
                $"Logged-in user is assigned to: {LoginPage.mycourt}", "OK");
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            await AnimateCardPress((View)sender);
            await Navigation.PushAsync(new History());
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UTILITIES
        // ─────────────────────────────────────────────────────────────────────

        private async Task ShowSuccessMessage(string message)
            => await DisplayAlert("Success", message, "OK");

        private async Task ShowErrorMessage(string message)
            => await DisplayAlert("Error", message, "OK");

        private async Task ShowInfoMessage(string title, string message)
            => await DisplayAlert(title, message, "OK");

        private async Task PrintToDevice(BluetoothDevice device, string printText)
        {
            try
            {
                using (var socket = device.CreateRfcommSocketToServiceRecord(
                    Java.Util.UUID.FromString("00001101-0000-1000-8000-00805f9b34fb")))
                {
                    await socket.ConnectAsync();
                    if (socket.IsConnected)
                    {
                        byte[] buffer = Encoding.UTF8.GetBytes(printText);
                        await Task.Delay(1000);
                        await socket.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        socket.Close();
                        await DisplayAlert("Success", "Print job completed successfully!", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Warning",
                            "Unable to connect to printer. Check your Bluetooth connection.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error",
                    "Printer connection failed. Ensure printer is on and paired.", "OK");
            }
        }

        private async Task HandleDocumentUpload()
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select Document to Upload",
                    FileTypes = FilePickerFileType.Pdf
                });

                if (result != null)
                    await DisplayAlert("Document Selected",
                        $"Selected: {result.FileName}\nCourt: {selectedCourt?.name}",
                        "OK");
            }
            catch
            {
                await DisplayAlert("Error", "Unable to select file. Please try again.", "OK");
            }
        }

        private async Task HandleNewFiling()
            => await DisplayAlert("New Filing",
                $"Creating new filing for {selectedCourt?.name}", "OK");

        private async Task HandleFilingStatus()
            => await DisplayAlert("Filing Status",
                $"Checking filing status for {selectedCourt?.name}", "OK");

        private async Task HandleDocumentTemplates()
        {
            var template = await DisplayActionSheet("Document Templates", "Cancel", null,
                "Motion Template", "Affidavit Template",
                "Notice Template", "Petition Template");

            if (template != "Cancel" && !string.IsNullOrEmpty(template))
                await DisplayAlert("Template Selected",
                    $"Opening {template} for {selectedCourt?.name}", "OK");
        }

        private void LogError(string method, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] {method}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[STACK] {ex.StackTrace}");
            }
            catch { /* suppress logging errors */ }
        }
    }
}