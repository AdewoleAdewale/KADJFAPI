using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    public partial class Verify : ContentPage
    {
        private HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isVerifying = false;
        private List<string> _recentSearches = new List<string>();
        private const int MAX_RECENT_SEARCHES = 5;
        private const int MIN_REFERENCE_LENGTH = 5;
        private const int MAX_REFERENCE_LENGTH = 50;
        private const int REQUEST_TIMEOUT_SECONDS = 45;

        // Enhanced response model with additional validation
        // FIXED VerifyResponse model - amount changed to decimal, added missing fields
        internal class VerifyResponse
        {
            public string status_code { get; set; }
            public string message { get; set; }
            public string agent_name { get; set; }
            public string payer_name { get; set; }
            public string payment_item { get; set; }
            public decimal amount { get; set; }
            public string lga { get; set; }
            public string superagent { get; set; }
            public string date_of_payment { get; set; }
            public string date_recorded { get; set; }
            public string court { get; set; }

            public bool IsValid() =>
                status_code == "00" || !string.IsNullOrWhiteSpace(payer_name);
        }
        // Static properties for data persistence
        public static string TaxId { get; set; }
        public static string FullName { get; set; }
        public static string PhoneNumber { get; set; }
        public static string MDA { get; set; }
        public static string Court { get; set; }
        public static string PaymentItem { get; set; }
        public static string Amount { get; set; }
        public static string PaymentReference { get; set; }
        public static string Status { get; set; }
        public static string DateRecorded { get; set; }
        public static string PayerName { get; set; }
        public static string LGA { get; set; }
        public static string PayerEmail { get; set; }
        public static string DateOfPayment { get; set; }
        public static string SuperAgent { get; set; }
        public static string LastSearchReference { get; set; }

        public Verify()
        {
            InitializeComponent();
            InitializeHttpClient();
            InitializeUI();
            LoadRecentSearches();
            LoadExistingData();
            CheckConnectivity();
        }

        private void InitializeHttpClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "YobeJudiciary-Mobile/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            _cancellationTokenSource = new CancellationTokenSource();
        }

        private void InitializeUI()
        {
            // Set initial UI state
            SetVerificationTimestamp();
            UpdateConnectionStatus();

            // Initialize character counter
            UpdateCharacterCounter(0);
        }

        private async void CheckConnectivity()
        {
            try
            {
                var current = Connectivity.NetworkAccess;
                UpdateConnectionStatus(current == NetworkAccess.Internet);

                // Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += OnConnectivityChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking connectivity: {ex.Message}");
            }
        }

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                UpdateConnectionStatus(e.NetworkAccess == NetworkAccess.Internet);
            });
        }

        private void UpdateConnectionStatus(bool isConnected = true)
        {
            try
            {
                connectionStatus.IsVisible = true;
                if (isConnected)
                {
                    connectionStatus.BackgroundColor = Color.FromHex("#e8f5e9");
                    connectionStatusLabel.Text = "Connection Status: Online";
                    connectionStatusLabel.TextColor = Color.FromHex("#2e7d32");
                }
                else
                {
                    connectionStatus.BackgroundColor = Color.FromHex("#ffebee");
                    connectionStatusLabel.Text = "Connection Status: Offline";
                    connectionStatusLabel.TextColor = Color.FromHex("#d32f2f");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating connection status: {ex.Message}");
            }
        }

        private void LoadRecentSearches()
        {
            try
            {
                // Load from preferences
                var recentSearchesJson = Preferences.Get("RecentSearches", "[]");
                _recentSearches = JsonConvert.DeserializeObject<List<string>>(recentSearchesJson) ?? new List<string>();

                UpdateRecentSearchesUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent searches: {ex.Message}");
                _recentSearches = new List<string>();
            }
        }

        private void SaveRecentSearches()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_recentSearches);
                Preferences.Set("RecentSearches", json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving recent searches: {ex.Message}");
            }
        }

        private void AddToRecentSearches(string reference)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reference)) return;

                // Remove if already exists
                _recentSearches.RemoveAll(x => x.Equals(reference, StringComparison.OrdinalIgnoreCase));

                // Add to beginning
                _recentSearches.Insert(0, reference);

                // Keep only max items
                if (_recentSearches.Count > MAX_RECENT_SEARCHES)
                {
                    _recentSearches = _recentSearches.Take(MAX_RECENT_SEARCHES).ToList();
                }

                SaveRecentSearches();
                UpdateRecentSearchesUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding to recent searches: {ex.Message}");
            }
        }

        private void UpdateRecentSearchesUI()
        {
            try
            {
                if (_recentSearches.Any())
                {
                    recentSearchesContainer.IsVisible = true;
                    recentSearchesCollection.ItemsSource = _recentSearches;
                }
                else
                {
                    recentSearchesContainer.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating recent searches UI: {ex.Message}");
            }
        }

        private void LoadExistingData()
        {
            try
            {
                if (!string.IsNullOrEmpty(Status))
                {
                    DisplayVerificationResult();
                }
                else
                {
                    ShowEmptyState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading existing data: {ex.Message}");
                ShowEmptyState();
            }
        }

        private void ShowEmptyState()
        {
            emptyState.IsVisible = true;
            demandnotice.IsVisible = false;
            resultsHeaderContainer.IsVisible = false;
            HideAllMessages();
        }

        private void SetVerificationTimestamp()
        {
            verificationTimestamp.Text = DateTime.Now.ToString("MMM dd, yyyy 'at' hh:mm tt");
        }

        private void DisplayVerificationResult()
        {
            try
            {
                emptyState.IsVisible = false;
                demandnotice.IsVisible = true;
                resultsHeaderContainer.IsVisible = true;
                recentSearchesContainer.IsVisible = false;

                // Populate UI elements with enhanced null checks and formatting
                name.Text = FormatDisplayText(PayerName, "Unknown Payer");
                DOP.Text = FormatDisplayText(DateOfPayment, "Not Available");
                invoicecourt.Text = FormatDisplayText(Court, "Unknown Court");
                agent.Text = FormatDisplayText(FullName, "Unknown Agent");
                agentlga.Text = FormatDisplayText(LGA, "Unknown LGA");
                viewpayemtitems.Text = FormatDisplayText(PaymentItem, "No Items Listed");
                paidamounts.Text = FormatAmount(Amount);
                referenceNumber.Text = FormatDisplayText(LastSearchReference, "N/A");

                // Update verification message and status
                string displayMessage = FormatDisplayText(Status, "Verification Completed");
                verifymessage.Text = displayMessage.ToUpper();

                // Update status badge
                UpdateStatusBadge();
                UpdateStatusContainer();
                SetVerificationTimestamp();

                // Show success message
                ShowSuccessMessage(displayMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying verification result: {ex.Message}");
                ShowError("Error displaying results. Please try again.", true);
            }
        }

        private string FormatDisplayText(string text, string defaultValue = "N/A")
        {
            return !string.IsNullOrWhiteSpace(text) ? text.Trim() : defaultValue;
        }

        private void UpdateStatusBadge()
        {
            try
            {
                if (Status == "Successful")
                {
                    statusBadge.BackgroundColor = Color.FromHex("#e8f5e9");
                    statusBadgeText.Text = "✅ VERIFIED PAYMENT";
                    statusBadgeText.TextColor = Color.FromHex("#2e7d32");
                }
                else
                {
                    statusBadge.BackgroundColor = Color.FromHex("#ffebee");
                    statusBadgeText.Text = "❌ VERIFICATION FAILED";
                    statusBadgeText.TextColor = Color.FromHex("#d32f2f");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating status badge: {ex.Message}");
            }
        }

        private void UpdateStatusContainer()
        {
            try
            {
                if (Status == "Successful")
                {
                    statusContainer.BackgroundGradientStartColor = Color.ForestGreen;
                    statusContainer.BackgroundGradientEndColor = Color.FromHex("#004225");
                    statusIcon.Text = "✅";
                }
                else
                {
                    statusContainer.BackgroundGradientStartColor = Color.OrangeRed;
                    statusContainer.BackgroundGradientEndColor = Color.FromHex("#cc3300");
                    statusIcon.Text = "❌";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating status container: {ex.Message}");
            }
        }

        private string FormatAmount(string amount)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(amount))
                    return "₦ 0.00";

                // Amount is already formatted as "F2" decimal string from ProcessVerificationResult
                if (decimal.TryParse(amount, out decimal value))
                    return $"₦ {value:N2}";

                return $"₦ {amount}";
            }
            catch
            {
                return $"₦ {amount ?? "0.00"}";
            }
        }
        private bool ValidateReferenceNumber(string reference)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    ShowError("Please enter a payment reference number", false);
                    return false;
                }

                reference = reference.Trim();

                if (reference.Length < MIN_REFERENCE_LENGTH)
                {
                    ShowError($"Reference number must be at least {MIN_REFERENCE_LENGTH} characters long", false);
                    return false;
                }

                if (reference.Length > MAX_REFERENCE_LENGTH)
                {
                    ShowError($"Reference number cannot exceed {MAX_REFERENCE_LENGTH} characters", false);
                    return false;
                }

                // Enhanced validation: alphanumeric with some special characters
                if (!Regex.IsMatch(reference, @"^[a-zA-Z0-9\-_/]+$"))
                {
                    ShowError("Reference number can only contain letters, numbers, hyphens, underscores, and forward slashes", false);
                    return false;
                }

                // Check for suspicious patterns
                if (Regex.IsMatch(reference, @"(.)\1{4,}")) // 5 or more consecutive identical characters
                {
                    ShowError("Reference number format appears invalid", false);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating reference number: {ex.Message}");
                ShowError("Error validating reference number", false);
                return false;
            }
        }

        private void ShowError(string message, bool isPersistent = false)
        {
            try
            {
                HideAllMessages();
                errorContainer.IsVisible = true;
                errorLabel.Text = message;

                if (!isPersistent)
                {
                    Device.StartTimer(TimeSpan.FromSeconds(6), () =>
                    {
                        Device.BeginInvokeOnMainThread(() => errorContainer.IsVisible = false);
                        return false;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing error message: {ex.Message}");
            }
        }

        private void ShowSuccessMessage(string message)
        {
            try
            {
                HideAllMessages();
                successContainer.IsVisible = true;
                successLabel.Text = message;

                Device.StartTimer(TimeSpan.FromSeconds(4), () =>
                {
                    Device.BeginInvokeOnMainThread(() => successContainer.IsVisible = false);
                    return false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing success message: {ex.Message}");
            }
        }

        private void HideAllMessages()
        {
            errorContainer.IsVisible = false;
            successContainer.IsVisible = false;
        }

        private void UpdateCharacterCounter(int count)
        {
            try
            {
                characterCounter.Text = $"{count} characters";
                characterCounter.IsVisible = count > 0;

                // Change color based on length
                if (count < MIN_REFERENCE_LENGTH)
                {
                    characterCounter.TextColor = Color.FromHex("#ff5722");
                }
                else if (count > MAX_REFERENCE_LENGTH)
                {
                    characterCounter.TextColor = Color.FromHex("#d32f2f");
                }
                else
                {
                    characterCounter.TextColor = Color.FromHex("#4caf50");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating character counter: {ex.Message}");
            }
        }

        private void SetVerifyingState(bool isVerifying)
        {
            try
            {
                _isVerifying = isVerifying;
                searchActivityIndicator.IsVisible = isVerifying;
                searchActivityIndicator.IsRunning = isVerifying;
                searchentry.IsEnabled = !isVerifying;

                if (isVerifying)
                {
                    searchButtonText.Text = "VERIFYING...";
                    searchButtonIcon.IsVisible = false;
                    searchButton.BackgroundGradientStartColor = Color.Gray;
                    searchButton.BackgroundGradientEndColor = Color.DarkGray;
                    HideAllMessages();
                }
                else
                {
                    searchButtonText.Text = "VERIFY PAYMENT";
                    searchButtonIcon.IsVisible = true;
                    searchButton.BackgroundGradientStartColor = Color.FromHex("#004225");
                    searchButton.BackgroundGradientEndColor = Color.FromHex("#2d5a3d");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting verifying state: {ex.Message}");
            }
        }

        // Event Handlers
        private async void OnVerifyTapped(object sender, EventArgs e)
        {
            await VerifyPayment();
        }

        private async void OnSearchCompleted(object sender, EventArgs e)
        {
            await VerifyPayment();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                HideAllMessages();
                int length = e.NewTextValue?.Length ?? 0;
                UpdateCharacterCounter(length);

                // Update entry container border color based on validation
                if (length == 0)
                {
                    entryContainer.BorderColor = Color.FromHex("#004225");
                }
                else if (length < MIN_REFERENCE_LENGTH)
                {
                    entryContainer.BorderColor = Color.FromHex("#ff5722");
                }
                else if (length > MAX_REFERENCE_LENGTH)
                {
                    entryContainer.BorderColor = Color.FromHex("#d32f2f");
                }
                else
                {
                    entryContainer.BorderColor = Color.FromHex("#4caf50");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling text change: {ex.Message}");
            }
        }

        private void OnEntryFocused(object sender, FocusEventArgs e)
        {
            try
            {
                entryContainer.BorderThickness = 3;
                entryContainer.Elevation = 4;
                UpdateRecentSearchesUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling entry focus: {ex.Message}");
            }
        }

        private void OnEntryUnfocused(object sender, FocusEventArgs e)
        {
            try
            {
                entryContainer.BorderThickness = 2;
                entryContainer.Elevation = 2;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling entry unfocus: {ex.Message}");
            }
        }

        private void OnRecentSearchTapped(object sender, EventArgs e)
        {
            try
            {
                if (sender is View view && view.BindingContext is string reference)
                {
                    searchentry.Text = reference;
                    searchentry.Focus();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling recent search tap: {ex.Message}");
            }
        }

        private async Task VerifyPayment()
        {
            if (_isVerifying) return;

            try
            {
                // Check connectivity first
                var networkAccess = Connectivity.NetworkAccess;
                if (networkAccess != NetworkAccess.Internet)
                {
                    ShowError("No internet connection. Please check your network settings.", false);
                    return;
                }

                string reference = searchentry.Text?.Trim();

                if (!ValidateReferenceNumber(reference))
                    return;

                SetVerifyingState(true);

                // Cancel any existing request
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                // Store the reference for display
                LastSearchReference = reference;

                string url = $"https://yobejud.osoftpay.net/Api/KadunaJFAPI/verify?InvoiceNumber={Uri.EscapeDataString(reference)}";

                using (var response = await _httpClient.GetAsync(url, _cancellationTokenSource.Token))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();

                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var result = JsonConvert.DeserializeObject<VerifyResponse>(json);

                            if (result?.IsValid() == true)
                            {
                                await ProcessVerificationResult(result, reference);
                            }
                            else
                            {
                                ShowError("Invalid response received from server", false);
                            }
                        }
                        else
                        {
                            ShowError("Empty response received from server", false);
                        }
                    }
                    else
                    {
                        string errorMessage = GetHttpErrorMessage(response.StatusCode);
                        ShowError(errorMessage, false);
                    }
                }
            }
            catch (TaskCanceledException tcEx)
            {
                if (tcEx.CancellationToken.IsCancellationRequested)
                {
                    ShowError("Request cancelled", false);
                }
                else
                {
                    ShowError("Request timed out. Please try again.", false);
                }
            }
            catch (HttpRequestException httpEx)
            {
                ShowError("Network error. Please check your connection and try again.", false);
                System.Diagnostics.Debug.WriteLine($"HTTP Error: {httpEx.Message}");
            }
            catch (JsonException jsonEx)
            {
                ShowError("Invalid response format from server", false);
                System.Diagnostics.Debug.WriteLine($"JSON Error: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                ShowError("An unexpected error occurred. Please try again.", false);
                System.Diagnostics.Debug.WriteLine($"General Error: {ex.Message}");
            }
            finally
            {
                SetVerifyingState(false);
            }
        }

        private string GetHttpErrorMessage(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.NotFound:
                    return "Payment reference not found";
                case HttpStatusCode.BadRequest:
                    return "Invalid request format";
                case HttpStatusCode.Unauthorized:
                    return "Authentication required";
                case HttpStatusCode.Forbidden:
                    return "Access denied";
                case HttpStatusCode.InternalServerError:
                    return "Server error. Please try again later";
                case HttpStatusCode.ServiceUnavailable:
                    return "Service temporarily unavailable";
                case HttpStatusCode.RequestTimeout:
                    return "Request timed out. Please try again";
                default:
                    return $"Server error ({(int)statusCode}). Please try again";
            }
        }

        private async Task ProcessVerificationResult(VerifyResponse result, string reference)
        {
            try
            {
                if (result != null)
                {
                    Status = (result.status_code == "00") ? "Successful" : (result.message ?? "Verification completed");
                    SuperAgent = result.superagent?.Trim() ?? "";
                    DateRecorded = result.date_recorded?.Trim() ?? "";
                    LGA = result.lga?.Trim() ?? "";
                    PayerName = result.payer_name?.Trim() ?? "";
                    FullName = result.agent_name?.Trim() ?? "";   // "agent" column in the UI
                    Court = result.court?.Trim() ?? "";
                    PaymentItem = result.payment_item?.Trim() ?? "";
                    Amount = result.amount.ToString("F2");
                    DateOfPayment = result.date_of_payment?.Trim() ?? "";

                    AddToRecentSearches(reference);
                    await Application.Current.SavePropertiesAsync();
                    DisplayVerificationResult();

                    try
                    {
                        if (Status == "Successful")
                            HapticFeedback.Perform(HapticFeedbackType.Click);
                        else
                            Vibration.Vibrate(TimeSpan.FromMilliseconds(200));
                    }
                    catch { }

                    if (Status == "Successful")
                    {
                        UserDialogs.Instance.Toast(
                            "Payment verification completed successfully! All details have been retrieved",
                            TimeSpan.FromSeconds(3));
                    }
                    else
                    {
                        // Show the actual status so user knows what's happening (e.g. "Pending")
                        await DisplayAlert("ℹ️ Verification Result",
                            $"Status: {(Status.Length > 100 ? Status.Substring(0, 100) + "..." : Status)}\n\nThis payment has not been confirmed yet.",
                            "OK");
                    }
                }
                else
                {
                    ShowError("No information found for this reference number", false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing verification result: {ex.Message}");
                ShowError("Error processing server response", false);
            }
        }
        // Enhanced Action Handlers
        private async void OnPrintTapped(object sender, EventArgs e)
        {
            try
            {
                // Simulate print functionality
                await DisplayAlert("🖨️ Print Receipt",
                    "Print functionality is being prepared. You can save this page as PDF or take a screenshot for now.",
                    "OK");

                // Future implementation: Generate PDF and print
                // await GenerateAndPrintReceipt();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Print error: {ex.Message}");
                await DisplayAlert("Error", "Unable to print at this time.", "OK");
            }
        }

        private async void OnShareTapped(object sender, EventArgs e)
        {
            try
            {
                string shareText = $"🧾 PAYMENT VERIFICATION RECEIPT\n" +
                                 $"═══════════════════════════════\n" +
                                 $"👤 Payer: {PayerName}\n" +
                                 $"💰 Amount: {FormatAmount(Amount)}\n" +
                                 $"📅 Date: {DateOfPayment}\n" +
                                 $"🏛️ Court: {Court}\n" +
                                 $"🏢 Agent: {FullName}\n" +
                                 $"📍 LGA: {LGA}\n" +
                                 $"🔢 Reference: {LastSearchReference}\n" +
                                 $"✅ Status: {Status}\n" +
                                 $"⏰ Verified: {DateTime.Now:MMM dd, yyyy 'at' hh:mm tt}\n" +
                                 $"═══════════════════════════════\n" +
                                 $"🔒 Secured by Yobe State Judiciary";

                await Share.RequestAsync(new ShareTextRequest
                {
                    Text = shareText,
                    Title = "Payment Verification Receipt"
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Share error: {ex.Message}");
                await DisplayAlert("Error", "Unable to share at this time.", "OK");
            }
        }

        private async void OnSaveTapped(object sender, EventArgs e)
        {
            try
            {
                // Save to local storage/preferences for offline access
                var receiptData = new
                {
                    PayerName,
                    Amount,
                    DateOfPayment,
                    Court,
                    FullName,
                    LGA,
                    Reference = LastSearchReference,
                    Status = Status,
                    VerificationDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                string receiptJson = JsonConvert.SerializeObject(receiptData);
                string key = $"Receipt_{LastSearchReference}_{DateTime.Now:yyyyMMdd_HHmmss}";
                Preferences.Set(key, receiptJson);

                await DisplayAlert("💾 Receipt Saved",
                    "Your payment verification receipt has been saved locally for offline access.",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save error: {ex.Message}");
                await DisplayAlert("Error", "Unable to save receipt at this time.", "OK");
            }
        }

        // Lifecycle Management
        protected override void OnDisappearing()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during disappearing: {ex.Message}");
            }
            finally
            {
                base.OnDisappearing();
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                // Focus the search entry when page appears
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (string.IsNullOrEmpty(searchentry.Text))
                    {
                        searchentry.Focus();
                    }
                });

                // Refresh connectivity status
                CheckConnectivity();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during appearing: {ex.Message}");
            }
        }

        // Resource cleanup
        ~Verify()
        {
            try
            {
                _httpClient?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during disposal: {ex.Message}");
            }
        }
    }
}