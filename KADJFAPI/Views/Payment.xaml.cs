using Acr.UserDialogs;
using KADJFAPI.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace KADJFAPI.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Payment : ContentPage
    {
        // ── UI state ──────────────────────────────────────────────────────────
        private bool _isAnimating = false;
        private bool _isVerifying = false;
        private bool _isDragging = false;
        private bool _isPrinting = false;   // guard against duplicate prints
        private double _initialTranslationY = 0;
        private double _dragStartY = 0;

        // ── Validation constants ──────────────────────────────────────────────
        private const int TOKEN_MIN_LENGTH = 6;
        private const int TOKEN_MAX_LENGTH = 30;
        private const string TOKEN_PATTERN = @"^[0-9]{6,20}$";
        private const double DRAG_THRESHOLD = 100;
        private const double AUTO_CLOSE_TIMEOUT = 5 * 60 * 1000;   // 5 min

        // ── Auto-close ────────────────────────────────────────────────────────
        private System.Timers.Timer _autoCloseTimer;
        private bool _isSheetClosed = false;

        // ── Reprint state ─────────────────────────────────────────────────────
        private ReceiptData _lastReceiptData = null;

        // ─────────────────────────────────────────────────────────────────────
        //  CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────

        public Payment()
        {
            try
            {
                InitializeComponent();
                InitializeSheet();
                SetupAutoCloseTimer();
                InitializePaymentMethods();
            }
            catch (Exception ex) { HandleException(ex, "Constructor"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INIT
        // ─────────────────────────────────────────────────────────────────────

        private void InitializePaymentMethods()
        {
            try { paymentmethod.SelectedIndex = 0; }
            catch (Exception ex) { HandleException(ex, "InitializePaymentMethods"); }
        }

        private void SetupAutoCloseTimer()
        {
            try
            {
                _autoCloseTimer = new System.Timers.Timer(AUTO_CLOSE_TIMEOUT);
                _autoCloseTimer.Elapsed += OnAutoCloseTimerElapsed;
                _autoCloseTimer.AutoReset = false;
                _autoCloseTimer.Start();
            }
            catch (Exception ex) { HandleException(ex, "SetupAutoCloseTimer"); }
        }

        private async void OnAutoCloseTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (!_isSheetClosed && string.IsNullOrWhiteSpace(inputtoken.Text))
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await ShowToast("Session expired. Please try again.", false);
                        await DismissSheet();
                    });
            }
            catch (Exception ex) { HandleException(ex, "OnAutoCloseTimerElapsed"); }
        }

        private void StopAutoCloseTimer()
        {
            try { _autoCloseTimer?.Stop(); _autoCloseTimer?.Dispose(); _autoCloseTimer = null; }
            catch (Exception ex) { HandleException(ex, "StopAutoCloseTimer"); }
        }

        private void ResetAutoCloseTimer()
        {
            try
            {
                if (_autoCloseTimer != null && !_isSheetClosed)
                { _autoCloseTimer.Stop(); _autoCloseTimer.Start(); }
            }
            catch (Exception ex) { HandleException(ex, "ResetAutoCloseTimer"); }
        }

        private async void InitializeSheet()
        {
            try
            {
                this.Opacity = 0;
                await this.FadeTo(1, 300, Easing.CubicOut);
                await Task.Delay(100);
                await AnimateSheetIn();
                SetupDragGesture();
                await Task.Delay(300);
                inputtoken.Focus();
            }
            catch (Exception ex) { HandleException(ex, "InitializeSheet"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAGE LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnAppearing()
        {
            base.OnAppearing();
            try { ResetFormState(); _isSheetClosed = false; }
            catch (Exception ex) { HandleException(ex, "OnAppearing"); }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                _isSheetClosed = true;
                StopAutoCloseTimer();
                // No _printer to dispose — PrintJobManager is app-scoped
            }
            catch (Exception ex) { HandleException(ex, "OnDisappearing"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FORM STATE
        // ─────────────────────────────────────────────────────────────────────

        private void ResetFormState()
        {
            try
            {
                inputtoken.Text = string.Empty;
                paymentmethod.SelectedIndex = 0;
                VerifyButton.IsEnabled = false;
                _lastReceiptData = null;
                HideReprintButton();
                HideMessage();
                ResetInputFieldStyle();
            }
            catch (Exception ex) { HandleException(ex, "ResetFormState"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHEET ANIMATIONS
        // ─────────────────────────────────────────────────────────────────────

        private async Task AnimateSheetIn()
        {
            try
            {
                if (_isAnimating) return;
                _isAnimating = true;
                await SheetFrame.TranslateTo(0, 0, 400, Easing.SpringOut);
                _isAnimating = false;
            }
            catch (Exception ex) { _isAnimating = false; HandleException(ex, "AnimateSheetIn"); }
        }

        private async Task AnimateSheetOut()
        {
            try
            {
                if (_isAnimating) return;
                _isAnimating = true;
                await SheetFrame.TranslateTo(0, 400, 200, Easing.CubicIn);
                _isAnimating = false;
            }
            catch (Exception ex) { _isAnimating = false; HandleException(ex, "AnimateSheetOut"); }
        }

        private async void OnBackgroundTapped(object sender, EventArgs e)
        {
            try { if (!_isDragging && !_isAnimating) await DismissSheet(); }
            catch (Exception ex) { HandleException(ex, "OnBackgroundTapped"); }
        }

        private void OnSheetTapped(object sender, EventArgs e) => ResetAutoCloseTimer();

        // ─────────────────────────────────────────────────────────────────────
        //  INPUT EVENTS
        // ─────────────────────────────────────────────────────────────────────

        private void OnTokenTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var token = e.NewTextValue ?? string.Empty;
                bool isValid = ValidateTokenFormat(token);
                bool hasPM = paymentmethod.SelectedIndex >= 0;

                VerifyButton.IsEnabled = isValid && hasPM && !_isVerifying;
                UpdateInputFieldStyle(isValid, token.Length > 0);

                if (token.Length > 0) { HideMessage(); ResetAutoCloseTimer(); }
            }
            catch (Exception ex) { HandleException(ex, "OnTokenTextChanged"); }
        }

        private void OnPaymentMethodChanged(object sender, EventArgs e)
        {
            try
            {
                var token = inputtoken.Text ?? string.Empty;
                bool isValid = ValidateTokenFormat(token);
                bool hasPM = paymentmethod.SelectedIndex >= 0;
                VerifyButton.IsEnabled = isValid && hasPM && !_isVerifying;
                ResetAutoCloseTimer();
            }
            catch (Exception ex) { HandleException(ex, "OnPaymentMethodChanged"); }
        }

        private void OnTokenCompleted(object sender, EventArgs e)
        {
            try
            {
                if (VerifyButton.IsEnabled) OnVerifyTokenClicked(sender, e);
                ResetAutoCloseTimer();
            }
            catch (Exception ex) { HandleException(ex, "OnTokenCompleted"); }
        }

        private bool ValidateTokenFormat(string token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token)) return false;
                if (token.Length < TOKEN_MIN_LENGTH || token.Length > TOKEN_MAX_LENGTH) return false;
                return Regex.IsMatch(token, TOKEN_PATTERN);
            }
            catch (Exception ex) { HandleException(ex, "ValidateTokenFormat"); return false; }
        }

        private void UpdateInputFieldStyle(bool isValid, bool hasContent)
        {
            try
            {
                if (!hasContent)
                {
                    TokenInputFrame.BorderColor = Color.LightGray;
                    TokenInputFrame.BackgroundColor = Color.FromHex("#F8F9FA");
                }
                else if (isValid)
                {
                    TokenInputFrame.BorderColor = Color.ForestGreen;
                    TokenInputFrame.BackgroundColor = Color.FromHex("#E8F5E8");
                }
                else
                {
                    TokenInputFrame.BorderColor = Color.Red;
                    TokenInputFrame.BackgroundColor = Color.FromHex("#FFF0F0");
                }
            }
            catch (Exception ex) { HandleException(ex, "UpdateInputFieldStyle"); }
        }

        private void ResetInputFieldStyle()
        {
            try
            {
                TokenInputFrame.BorderColor = Color.LightGray;
                TokenInputFrame.BackgroundColor = Color.FromHex("#F8F9FA");
            }
            catch (Exception ex) { HandleException(ex, "ResetInputFieldStyle"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAYMENT PROCESSING
        // ─────────────────────────────────────────────────────────────────────

        private async void OnVerifyTokenClicked(object sender, EventArgs e)
        {
            ResetAutoCloseTimer();
            await ProcessPayment();
        }

        private async Task ProcessPayment()
        {
            try
            {
                if (_isVerifying) return;

                var token = inputtoken.Text?.Trim();
                var selectedPaymentMethod = paymentmethod.SelectedItem?.ToString();

                if (string.IsNullOrWhiteSpace(token))
                {
                    ShowErrorMessage("Please enter an Invoice number");
                    return;
                }
                if (!ValidateTokenFormat(token))
                {
                    ShowErrorMessage("Invalid Invoice format. Please check and try again.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(selectedPaymentMethod))
                {
                    ShowErrorMessage("Please select a payment method");
                    return;
                }

                _isVerifying = true;
                SetLoadingState(true);

                try { await CreateContractAsync(); }
                catch (Exception ex)
                {
                    await ShowToast(string.Format("Error processing payment: {0}", ex.Message), false);
                }
            }
            catch (Exception ex) { HandleException(ex, "ProcessPayment"); }
            finally
            {
                _isVerifying = false;
                SetLoadingState(false);
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e) => await DismissSheet();

        private void SetLoadingState(bool isLoading)
        {
            try
            {
                LoadingIndicator.IsVisible = isLoading;
                LoadingIndicator.IsRunning = isLoading;

                var token = inputtoken.Text ?? string.Empty;
                bool hasPM = paymentmethod.SelectedIndex >= 0;

                VerifyButton.IsEnabled = !isLoading && ValidateTokenFormat(token) && hasPM;
                CancelButton.IsEnabled = !isLoading;
                inputtoken.IsEnabled = !isLoading;
                paymentmethod.IsEnabled = !isLoading;
                VerifyButton.Text = isLoading ? "Processing Payment..." : "Process Payment";
            }
            catch (Exception ex) { HandleException(ex, "SetLoadingState"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  API CALL
        // ─────────────────────────────────────────────────────────────────────

        private async Task CreateContractAsync()
        {
            try
            {
                StopAutoCloseTimer();

                if (string.IsNullOrWhiteSpace(LoginPage.myemail))
                {
                    await ShowToast("ERROR: USER EMAIL NOT FOUND. PLEASE LOG IN AGAIN.", false);
                    return;
                }

                var selectedPaymentMethod = paymentmethod.SelectedItem?.ToString() ?? "Cash";
                string invoiceToken = inputtoken.Text?.Trim() ?? "";

                var payload = new
                {
                    invoice = invoiceToken,
                    mode_of_payment = selectedPaymentMethod,
                    email = LoginPage.myemail
                };

                var jsonContent = JsonConvert.SerializeObject(payload);

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        (sender2, certificate, chain, sslPolicyErrors) => true,
                    SslProtocols =
                        System.Security.Authentication.SslProtocols.Tls12 |
                        System.Security.Authentication.SslProtocols.Tls |
                        System.Security.Authentication.SslProtocols.Tls11,
                    CheckCertificateRevocationList = false,
                    UseDefaultCredentials = false,
                    AutomaticDecompression =
                        System.Net.DecompressionMethods.GZip |
                        System.Net.DecompressionMethods.Deflate
                };

                using (var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
                {
                    var req = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://kadjud.osoftpay.net/Api/KadunaJFAPI/Make_payment")
                    {
                        Content = new System.Net.Http.StringContent(
                            jsonContent, System.Text.Encoding.UTF8, "application/json")
                    };
                    req.Headers.Add("Accept", "application/json");

                    var response = await httpClient.SendAsync(req);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine(
                        string.Format("[Payment] Response ({0}): {1}", response.StatusCode, responseContent));

                    if (!response.IsSuccessStatusCode)
                    {
                        await ShowToast(string.Format("Server error: {0}", response.StatusCode), false);
                        return;
                    }

                    var settings = new JsonSerializerSettings
                    { MissingMemberHandling = MissingMemberHandling.Ignore };

                    var contractResponse = JsonConvert.DeserializeObject<VerifyTokenResponse>(
                        responseContent, settings);

                    if (contractResponse == null)
                    {
                        await ShowToast("Failed to parse server response.", false);
                        return;
                    }

                    var status = (contractResponse.status ?? "").Trim();
                    bool isSuccess = status == "00"
                                  || string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(status, "successful", StringComparison.OrdinalIgnoreCase);

                    if (isSuccess)
                    {
                        UserDialogs.Instance.Toast(
                            string.Format("Payment processed successfully for Invoice: {0}", invoiceToken),
                            TimeSpan.FromSeconds(3));

                        ShowSuccessMessage(string.Format("Payment successful! Invoice: {0}", invoiceToken));

                        var receipt = BuildReceiptData(contractResponse, invoiceToken);
                        _lastReceiptData = receipt;

                        await AttemptPrintAsync(receipt, isReprint: false);

                        await Task.Delay(5000);
                        await DismissSheet();
                    }
                    else
                    {
                        UserDialogs.Instance.Toast(
                            string.Format("Payment failed: {0}", contractResponse.message),
                            TimeSpan.FromSeconds(3));
                        ShowErrorMessage(contractResponse.message ?? "Transaction failed.");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                await ShowToast("Request timed out. Please check your internet connection.", false);
            }
            catch (HttpRequestException ex)
            {
                await ShowToast(string.Format("Network error: {0}", ex.Message), false);
            }
            catch (JsonException ex)
            {
                await ShowToast(string.Format("Data format error: {0}", ex.Message), false);
            }
            catch (Exception ex)
            {
                await ShowToast(string.Format("Unexpected error: {0}", ex.Message), false);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RECEIPT BUILDER
        // ─────────────────────────────────────────────────────────────────────

        private ReceiptData BuildReceiptData(VerifyTokenResponse resp, string invoiceToken,
            bool isReprint = false)
        {
            decimal amount = resp.amount;

            var items = new List<ReceiptItem>();

            if (!string.IsNullOrWhiteSpace(resp.payment_item))
                items.Add(new ReceiptItem
                { Description = resp.payment_item, Amount = amount, SubText = null });

            if (!string.IsNullOrWhiteSpace(resp.payer_name))
                items.Add(new ReceiptItem
                { Description = "MDA", Amount = 0m, SubText = LoginPage.mymda });

            if (!string.IsNullOrWhiteSpace(resp.payer_name))
                items.Add(new ReceiptItem
                { Description = "Payer Name", Amount = 0m, SubText = resp.payer_name });

            if (!string.IsNullOrWhiteSpace(resp.court))
                items.Add(new ReceiptItem
                { Description = "Court", Amount = 0m, SubText = resp.court });

            if (!string.IsNullOrWhiteSpace(resp.lga))
                items.Add(new ReceiptItem
                { Description = "LGA", Amount = 0m, SubText = resp.lga });

            if (!string.IsNullOrWhiteSpace(resp.case_number))
                items.Add(new ReceiptItem
                { Description = "Case Number", Amount = 0m, SubText = resp.case_number });

            if (resp.date_of_payment != default)
                items.Add(new ReceiptItem
                {
                    Description = "Paid On",
                    Amount = 0m,
                    SubText = resp.date_of_payment.ToString("dd MMM yyyy HH:mm")
                });

            string barcodeValue = !string.IsNullOrWhiteSpace(resp.case_number)
                ? resp.case_number
                : invoiceToken;

            return new ReceiptData
            {
                StoreName = "KADUNA STATE JUDICIARY",
                StorePhone = "Contact us: +234 803 052 3208, +234 907 070 1616",
                ReceiptNumber = invoiceToken,
                AgentName = LoginPage.MyfullName ?? "N/A",
                CollectionPoint = resp.court ?? "N/A",
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = amount,
                FooterLine1 = isReprint ? "*** REPRINTED PAYMENT RECEIPT ***" : "Thank You!",
                FooterLine2 = isReprint
                                    ? string.Format("Reprinted: {0:dd MMM yyyy HH:mm} | POWERED BY OSOFTPAY",
                                        DateTime.Now)
                                    : "POWERED BY OSOFTPAY",
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PRINT  –  uses PrintJobManager (durable, guarded, no duplicates)
        // ─────────────────────────────────────────────────────────────────────

        private async Task AttemptPrintAsync(ReceiptData receipt, bool isReprint)
        {
            if (_isPrinting)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Payment] Print already in progress – skipped duplicate call.");
                return;
            }
            _isPrinting = true;

            try
            {
                bool granted = await BluetoothPermissionHelper.RequestAsync();
                if (!granted)
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast("Bluetooth permission denied.",
                            TimeSpan.FromSeconds(6));
                        ShowReprintButton();
                    });
                    return;
                }

                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.ChunkStarted:
                                UserDialogs.Instance.Toast(
                                    string.Format("Printing {0}…", p.ChunkName),
                                    TimeSpan.FromSeconds(2));
                                break;

                            case PrintProgressStatus.ChunkRetrying:
                                UserDialogs.Instance.Toast(
                                    string.Format("Reconnecting… retrying {0} (#{1})",
                                        p.ChunkName, p.AttemptNumber),
                                    TimeSpan.FromSeconds(2));
                                break;

                            case PrintProgressStatus.SessionCompleted:
                                HideReprintButton();
                                UserDialogs.Instance.Toast(
                                    isReprint ? "Receipt reprinted successfully!" : "Receipt printed.",
                                    TimeSpan.FromSeconds(4));
                                break;

                            case PrintProgressStatus.ChunkFailed:
                                ShowReprintButton();
                                UserDialogs.Instance.Toast(
                                    string.Format("Could not print {0}.", p.ChunkName),
                                    TimeSpan.FromSeconds(5));
                                break;
                        }
                    }));

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);

                    // Success — remove from store so ResumeUnfinished never replays it
                    await App.PrintJobManager.DeleteJobAsync(job.JobId);

                    Device.BeginInvokeOnMainThread(HideReprintButton);
                }
                catch (PrinterException pex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        string.Format("[Payment] PrinterException: {0}", pex));
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast(
                            string.Format("Print failed: {0}. Tap Reprint to try again.", pex.Message),
                            TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                catch (OperationCanceledException)
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast(
                            "Print timed out. Tap Reprint Receipt when printer is ready.",
                            TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        string.Format("[Payment] Print error: {0}", ex));
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast(
                            "Printer not connected. Tap Reprint Receipt to try again.",
                            TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                finally
                {
                    cts.Dispose();
                }
            }
            finally
            {
                _isPrinting = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  REPRINT
        // ─────────────────────────────────────────────────────────────────────

        private async void OnReprintReceiptClicked(object sender, EventArgs e)
        {
            if (_lastReceiptData == null)
            {
                UserDialogs.Instance.Toast("No receipt data available.", TimeSpan.FromSeconds(4));
                return;
            }

            ReprintButton.IsEnabled = false;
            ReprintButton.Text = "🖨️ Reprinting...";

            try
            {
                var reprint = new ReceiptData
                {
                    StoreName = _lastReceiptData.StoreName,
                    StorePhone = _lastReceiptData.StorePhone,
                    ReceiptNumber = _lastReceiptData.ReceiptNumber,
                    AgentName = _lastReceiptData.AgentName,
                    CollectionPoint = _lastReceiptData.CollectionPoint,
                    PrintDate = DateTime.Now,
                    Items = _lastReceiptData.Items,
                    AmountPaid = _lastReceiptData.AmountPaid,
                    BarcodeLabel = _lastReceiptData.BarcodeLabel,   // preserve case number barcode
                    FooterLine1 = "*** REPRINTED PAYMENT RECEIPT ***",
                    FooterLine2 = string.Format(
                                        "Reprinted: {0:dd MMM yyyy HH:mm} | POWERED BY OSOFTPAY",
                                        DateTime.Now),
                };

                await AttemptPrintAsync(reprint, isReprint: true);
            }
            finally
            {
                ReprintButton.IsEnabled = true;
                ReprintButton.Text = "🖨️ Reprint Receipt";
            }
        }

        private void ShowReprintButton()
        {
            try
            {
                ReprintButton.IsVisible = true;
                ReprintButton.Opacity = 0;
                ReprintButton.FadeTo(1, 300, Easing.CubicOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Payment] ShowReprintButton: {0}", ex));
            }
        }

        private void HideReprintButton()
        {
            try { ReprintButton.IsVisible = false; } catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MESSAGE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private void ShowErrorMessage(string message)
        {
            try { ShowMessage(message, "❌", Color.FromHex("#FFE6E6"), Color.Red); }
            catch (Exception ex) { HandleException(ex, "ShowErrorMessage"); }
        }

        private void ShowSuccessMessage(string message)
        {
            try { ShowMessage(message, "✅", Color.FromHex("#E8F5E8"), Color.ForestGreen); }
            catch (Exception ex) { HandleException(ex, "ShowSuccessMessage"); }
        }

        private void ShowMessage(string message, string icon, Color backgroundColor, Color textColor)
        {
            try
            {
                MessageContainer.IsVisible = true;
                MessageFrame.BackgroundColor = backgroundColor;
                MessageIcon.Text = icon;
                MessageIcon.TextColor = textColor;
                MessageLabel.Text = message;
                MessageLabel.TextColor = textColor;
            }
            catch (Exception ex) { HandleException(ex, "ShowMessage"); }
        }

        private void HideMessage()
        {
            try { MessageContainer.IsVisible = false; }
            catch (Exception ex) { HandleException(ex, "HideMessage"); }
        }

        private async Task ShowToast(string message, bool isSuccess)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert(isSuccess ? "Success" : "Error", message, "OK"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Payment] ShowToast error: {0}. Msg: {1}", ex.Message, message));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHEET DISMISS
        // ─────────────────────────────────────────────────────────────────────

        private async Task DismissSheet()
        {
            try
            {
                if (_isAnimating || _isSheetClosed) return;
                _isSheetClosed = true;
                StopAutoCloseTimer();

                await AnimateSheetOut();
                await this.FadeTo(0, 200, Easing.CubicIn);

                if (Navigation.ModalStack.Count > 0)
                    await Navigation.PopModalAsync();
                else
                    await Navigation.PopAsync();
            }
            catch (Exception ex) { HandleException(ex, "DismissSheet"); }
        }

        protected override bool OnBackButtonPressed()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(async () => await DismissSheet());
                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex, "OnBackButtonPressed");
                return base.OnBackButtonPressed();
            }
        }

        private async void OnResendTokenTapped(object sender, EventArgs e)
        {
            try
            {
                ShowMessage("Navigating to Generate Invoice...", "📤",
                    Color.FromHex("#E3F2FD"), Color.Blue);
                await Navigation.PushAsync(new Views.Generate());
                HideMessage();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Failed to navigate. Please try again.");
                HandleException(ex, "OnResendTokenTapped");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  DRAG GESTURE
        // ─────────────────────────────────────────────────────────────────────

        private async void OnPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            try
            {
                if (_isAnimating || _isVerifying) return;

                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _isDragging = true;
                        _initialTranslationY = SheetFrame.TranslationY;
                        _dragStartY = e.TotalY;
                        break;

                    case GestureStatus.Running:
                        if (_isDragging)
                        {
                            var newY = _initialTranslationY + (e.TotalY - _dragStartY);
                            if (newY >= 0)
                            {
                                SheetFrame.TranslationY = newY;
                                this.Opacity = Math.Max(0.3, 1 - (newY / 400));
                                DragHandle.BackgroundColor = newY > 20 ? Color.Gray : Color.LightGray;
                                DragHandle.WidthRequest = newY > 20 ? 60 : 50;
                            }
                        }
                        break;

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        if (_isDragging)
                        {
                            _isDragging = false;
                            var finalY = SheetFrame.TranslationY;
                            DragHandle.BackgroundColor = Color.LightGray;
                            DragHandle.WidthRequest = 50;

                            if (finalY > DRAG_THRESHOLD)
                                await DismissSheet();
                            else
                                await Task.WhenAll(
                                    SheetFrame.TranslateTo(0, 0, 300, Easing.SpringOut),
                                    this.FadeTo(1, 200, Easing.CubicOut));
                        }
                        break;
                }
            }
            catch (Exception ex) { HandleException(ex, "OnPanUpdated"); }
        }

        private void SetupDragGesture()
        {
            try
            {
                var panGesture = new PanGestureRecognizer();
                panGesture.PanUpdated += OnPanUpdated;
                SheetFrame.GestureRecognizers.Add(panGesture);
                DragHandleArea.GestureRecognizers.Add(panGesture);
            }
            catch (Exception ex) { HandleException(ex, "SetupDragGesture"); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EXCEPTION HANDLER
        // ─────────────────────────────────────────────────────────────────────

        private void HandleException(Exception ex, string context)
        {
            System.Diagnostics.Debug.WriteLine(
                string.Format("[Payment] Error in {0}: {1}", context, ex.Message));
            System.Diagnostics.Debug.WriteLine(
                string.Format("[Payment] Stack: {0}", ex.StackTrace));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INNER MODELS
        // ─────────────────────────────────────────────────────────────────────

        internal class VerifyTokenObject
        {
            public string invoice { get; set; } = "";
            public string mode_of_payment { get; set; } = "";
            public string email { get; set; } = "";
        }

        internal class VerifyTokenResponse
        {
            public string status { get; set; }
            public string message { get; set; }
            public DateTime date_of_payment { get; set; }
            public string payment_item { get; set; }
            public string court { get; set; }
            public decimal amount { get; set; }
            public string agent_name { get; set; }
            public string payer_name { get; set; }
            public string lga { get; set; }
            public DateTime date_recorded { get; set; }
            public string superagent { get; set; }
            public string case_number { get; set; }
        }
    }
}