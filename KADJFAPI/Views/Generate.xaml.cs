using Acr.UserDialogs;
using KADJFAPI.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PancakeView;
using Xamarin.Forms.Xaml;

namespace KADJFAPI.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Generate : ContentPage, INotifyPropertyChanged
    {
        // ── Data ──────────────────────────────────────────────────────────────
        private List<MDAData> _mdaData;
        private List<PaymentData> _paymentData;

        // ── HTTP / processing ─────────────────────────────────────────────────
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, bool> _fieldValidationStatus;
        private readonly Dictionary<string, string> _validationErrors;

        private bool _isProcessing;
        private int _completedFields;
        private const int TotalFields = 5;

        // ── Animation durations ───────────────────────────────────────────────
        private const uint SheetAnimationDuration = 300;
        private const uint FieldAnimationDuration = 200;

        // ── Print state ───────────────────────────────────────────────────────
        private bool _isPrinting = false;   // guard against duplicate prints
        private ReceiptData _lastReceiptData = null;
        private string _lastInvoiceNumber = null;
        private InvoiceResult _lastInvoiceResult = null;

        // ── Guard: prevents double-navigation crash on rapid back-press ───────
        private bool _isNavigatingAway = false;

        // ─────────────────────────────────────────────────────────────────────
        //  BINDABLE PROPERTIES
        // ─────────────────────────────────────────────────────────────────────

        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); UpdateButtonState(); }
        }

        public int CompletedFields
        {
            get => _completedFields;
            set { _completedFields = value; OnPropertyChanged(); UpdateProgressText(); UpdateButtonState(); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────

        public Generate()
        {
            InitializeComponent();

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            BindingContext = this;

            _fieldValidationStatus = new Dictionary<string, bool>
            {
                ["taxId"] = false,
                ["fullName"] = false,
                ["email"] = false,
                ["phone"] = false,
                ["category"] = false,
                ["amount"] = false
            };
            _validationErrors = new Dictionary<string, string>();

            InitializePaymentCategories();
            StartEntranceAnimations();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAGE LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // No _printer to dispose — PrintJobManager is app-scoped
            try { _httpClient?.Dispose(); } catch { }
        }

        protected override bool OnBackButtonPressed()
        {
            if (successSheet != null && successSheet.IsVisible)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await HideSheet(successSheet, sheetOverlay));
                return true;
            }
            if (errorSheet != null && errorSheet.IsVisible)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await HideSheet(errorSheet, sheetOverlay));
                return true;
            }

            if (_isNavigatingAway) return true;
            _isNavigatingAway = true;

            if (IsProcessing) IsProcessing = false;

            return base.OnBackButtonPressed();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ANIMATIONS
        // ─────────────────────────────────────────────────────────────────────

        private async void StartEntranceAnimations()
        {
            try
            {
                await headerSection.FadeTo(0, 0);
                await headerSection.TranslateTo(0, -50, 0);
                _ = headerSection.FadeTo(1, 800, Easing.CubicOut);
                _ = headerSection.TranslateTo(0, 0, 800, Easing.CubicOut);
                await Task.Delay(200);
                await mainFormContainer.FadeTo(0, 0);
                await mainFormContainer.TranslateTo(0, 100, 0);
                _ = mainFormContainer.FadeTo(1, 800, Easing.CubicOut);
                _ = mainFormContainer.TranslateTo(0, 0, 800, Easing.CubicOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] Animation: {0}", ex.Message));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PAYMENT CATEGORIES
        // ─────────────────────────────────────────────────────────────────────

        private async void InitializePaymentCategories()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    PIC.Title = "Loading categories...";
                    PIC.IsEnabled = false;
                });

                var paymentItems = await FetchPaymentItemsWithRetryAsync();

                Device.BeginInvokeOnMainThread(() =>
                {
                    PIC.ItemDisplayBinding = new Binding("serviceName");
                    PIC.ItemsSource = paymentItems;
                    _paymentData = paymentItems;
                    PIC.Title = "Select Payment Category";
                    PIC.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    PIC.Title = "Failed to load categories";
                    PIC.IsEnabled = false;
                    await DisplayAlert("Error",
                        "Failed to load payment categories. Please check your internet connection.", "OK");
                });
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] Categories: {0}", ex));
            }
        }

        private async Task<List<PaymentData>> FetchPaymentItemsWithRetryAsync()
        {
            string url = string.Format(
                "https://yobejud.osoftpay.net/Api/KadunaJFAPI/PaymentItems?Email={0}",
                LoginPage.myemail);

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                               System.Security.Authentication.SslProtocols.Tls11
            };

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "YobeJudiciary-Mobile-App");

                for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
                {
                    try
                    {
                        using (var response = await client.GetAsync(url))
                        {
                            var content = await response.Content.ReadAsStringAsync();

                            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                                throw new UnauthorizedAccessException("Authentication required.");

                            response.EnsureSuccessStatusCode();

                            if (string.IsNullOrWhiteSpace(content))
                                throw new InvalidDataException("Empty response received");

                            var items = JsonConvert.DeserializeObject<List<PaymentData>>(content);
                            if (items == null || !items.Any())
                                throw new InvalidDataException("No payment items found");

                            return items;
                        }
                    }
                    catch (UnauthorizedAccessException) { throw; }
                    catch (Exception ex) when (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            string.Format("[Generate] Attempt {0}: {1}", attempt, ex.Message));
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                    }
                }
            }
            throw new InvalidOperationException(
                string.Format("Failed to fetch payment items after {0} attempts", MAX_RETRY_ATTEMPTS));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FIELD EVENTS
        // ─────────────────────────────────────────────────────────────────────

        private void OnTaxIdTextChanged(object sender, TextChangedEventArgs e)
            => ValidateField("taxId", e.NewTextValue, ValidateTaxId);

        private void OnFullNameTextChanged(object sender, TextChangedEventArgs e)
            => ValidateField("fullName", e.NewTextValue, ValidateFullName);

        private void OnPhoneTextChanged(object sender, TextChangedEventArgs e)
            => ValidateField("phone", e.NewTextValue, ValidatePhone);

        private void OnAmountTextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateField("amount", e.NewTextValue, ValidateAmount);
            FormatAmountDisplay(e.NewTextValue);
        }

        private void OnTaxIdUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("taxId", texid.Text, ValidateTaxId,
               taxIdFrame, taxIdError, taxIdValidIcon);

        private void OnFullNameUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("fullName", fullname.Text, ValidateFullName,
               fullNameFrame, fullNameError, fullNameValidIcon);

        private void OnPhoneUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("phone", userphonenumber.Text, ValidatePhone,
               phoneFrame, phoneError, phoneValidIcon);

        private void OnAmountUnfocused(object sender, FocusEventArgs e)
            => AnimateFieldValidation("amount", useramount.Text, ValidateAmount,
               amountFrame, amountError, amountValidIcon);

        private void OnCategorySelectionChanged(object sender, EventArgs e)
        {
            var picker = sender as Picker;
            var isValid = picker?.SelectedIndex >= 0;
            UpdateFieldValidation("category", isValid,
                isValid ? string.Empty : "Please select a payment category");
            if (isValid) AnimateSuccessValidation(categoryFrame, categoryValidIcon);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  VALIDATORS
        // ─────────────────────────────────────────────────────────────────────

        private (bool, string) ValidateTaxId(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Tax ID is required");
            if (v.Length < 4) return (false, "Tax ID must be at least 4 characters");
            if (v.Length > 8) return (false, "Tax ID cannot exceed 8 characters");
            if (!Regex.IsMatch(v, @"^\d+$")) return (false, "Tax ID must contain only numbers");
            return (true, string.Empty);
        }

        private (bool, string) ValidateFullName(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Full name is required");
            if (v.Trim().Length < 2) return (false, "Name must be at least 2 characters");
            if (!Regex.IsMatch(v.Trim(), @"^[a-zA-Z\s'\-\.]+$"))
                return (false, "Name contains invalid characters");
            if (v.Trim().Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).Count() < 2)
                return (false, "Please enter first and last name");
            return (true, string.Empty);
        }

        private (bool, string) ValidatePhone(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Phone number is required");
            var digits = Regex.Replace(v, @"[^\d]", "");
            if (digits.Length < 10) return (false, "Phone must be at least 10 digits");
            if (digits.Length > 12) return (false, "Phone cannot exceed 12 digits");
            if (digits.StartsWith("234") && digits.Length != 13)
                return (false, "Invalid Nigerian phone number format");
            return (true, string.Empty);
        }

        private (bool, string) ValidateAmount(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return (false, "Amount is required");
            var clean = Regex.Replace(v, @"[₦,\s]", "");
            if (!decimal.TryParse(clean, out decimal amount)) return (false, "Invalid amount format");
            if (amount <= 200) return (false, "Amount must be greater than ₦200");
            if (amount > 10_000_000m) return (false, "Amount cannot exceed ₦10,000,000");
            return (true, string.Empty);
        }

        private void ValidateField(string fieldName, string value,
            Func<string, (bool, string)> validator)
        {
            var (isValid, error) = validator(value);
            UpdateFieldValidation(fieldName, isValid, error);
        }

        private void UpdateFieldValidation(string fieldName, bool isValid, string error)
        {
            bool wasValid = _fieldValidationStatus.ContainsKey(fieldName) &&
                            _fieldValidationStatus[fieldName];
            _fieldValidationStatus[fieldName] = isValid;

            if (isValid) _validationErrors.Remove(fieldName);
            else _validationErrors[fieldName] = error;

            if (!wasValid && isValid) CompletedFields++;
            else if (wasValid && !isValid) CompletedFields--;
        }

        private async void AnimateFieldValidation(string fieldName, string value,
            Func<string, (bool, string)> validator,
            PancakeView frame, Label errorLabel, Label validIcon)
        {
            try
            {
                var (isValid, error) = validator(value);
                UpdateFieldValidation(fieldName, isValid, error);
                if (isValid) { await AnimateSuccessValidation(frame, validIcon); errorLabel.IsVisible = false; }
                else { await AnimateErrorValidation(frame, errorLabel, error); validIcon.IsVisible = false; }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] AnimateField {0}: {1}", fieldName, ex.Message));
            }
        }

        private async Task AnimateSuccessValidation(PancakeView frame, Label validIcon)
        {
            frame.Style = (Style)Resources["EntryContainerSuccessStyle"];
            await frame.ScaleTo(1.05, FieldAnimationDuration / 2, Easing.CubicOut);
            await frame.ScaleTo(1.0, FieldAnimationDuration / 2, Easing.CubicIn);
            validIcon.IsVisible = true;
            validIcon.Scale = 0;
            await validIcon.ScaleTo(1.2, FieldAnimationDuration, Easing.BounceOut);
            await validIcon.ScaleTo(1.0, FieldAnimationDuration / 2, Easing.CubicIn);
        }

        private async Task AnimateErrorValidation(PancakeView frame, Label errorLabel, string error)
        {
            frame.Style = (Style)Resources["EntryContainerErrorStyle"];
            await frame.TranslateTo(-10, 0, 50);
            await frame.TranslateTo(10, 0, 50);
            await frame.TranslateTo(-5, 0, 50);
            await frame.TranslateTo(0, 0, 50);
            errorLabel.Text = error;
            errorLabel.IsVisible = true;
            errorLabel.Opacity = 0;
            await errorLabel.FadeTo(1, FieldAnimationDuration);
        }

        private void FormatAmountDisplay(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) { amountFormatted.IsVisible = false; return; }
                var clean = Regex.Replace(value, @"[₦,\s]", "");
                if (decimal.TryParse(clean, out decimal amount) && amount > 0)
                {
                    amountFormatted.Text = string.Format("₦{0}",
                        amount.ToString("N2", CultureInfo.CreateSpecificCulture("en-NG")));
                    amountFormatted.IsVisible = true;
                }
                else amountFormatted.IsVisible = false;
            }
            catch { amountFormatted.IsVisible = false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PROGRESS / BUTTON
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateProgressText()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                progressText.Text = string.Format("{0}/{1} fields completed", CompletedFields, TotalFields);
                var pct = (double)CompletedFields / TotalFields;
                progressText.TextColor = pct == 1.0
                    ? Color.FromHex("#27AE60")
                    : pct >= 0.5 ? Color.FromHex("#F39C12")
                                 : Color.FromHex("#E74C3C");
            });
        }

        private void UpdateButtonState()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                bool canGenerate = CompletedFields == TotalFields && !IsProcessing;
                generateInvoiceBtn.IsEnabled = canGenerate;
                generateInvoiceBtn.Opacity = canGenerate ? 1.0 : 0.5;

                if (IsProcessing)
                {
                    buttonLoadingIndicator.IsRunning = true;
                    buttonLoadingIndicator.IsVisible = true;
                    buttonLabel.Text = "GENERATING...";
                    buttonIcon.IsVisible = false;
                }
                else
                {
                    buttonLoadingIndicator.IsRunning = false;
                    buttonLoadingIndicator.IsVisible = false;
                    buttonLabel.Text = "GENERATE INVOICE";
                    buttonIcon.IsVisible = true;
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        //  GENERATE INVOICE
        // ─────────────────────────────────────────────────────────────────────

        private async void OnGenerateInvoiceTapped(object sender, EventArgs e)
        {
            if (IsProcessing || CompletedFields != TotalFields) return;

            try
            {
                IsProcessing = true;

                HideReprintButton();
                _lastReceiptData = null;
                _lastInvoiceResult = null;
                _lastInvoiceNumber = null;

                if (!await PerformFinalValidation()) { IsProcessing = false; return; }

                var invoiceData = CreateInvoiceData();
                var result = await GenerateInvoiceAsync(invoiceData);

                if (result.Success) await ShowSuccessSheet(result.Data);
                else await ShowErrorSheet(result.ErrorMessage, result.ErrorDetails);
            }
            catch (Exception ex)
            {
                await ShowErrorSheet("Unexpected Error", ex.Message);
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] OnGenerateInvoiceTapped: {0}", ex));
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task<bool> PerformFinalValidation()
        {
            var errors = new List<string>();

            var validations = new Dictionary<string, (string, Func<string, (bool, string)>)>
            {
                ["fullName"] = (fullname.Text, ValidateFullName),
                ["phone"] = (userphonenumber.Text, ValidatePhone),
                ["amount"] = (useramount.Text, ValidateAmount)
            };

            foreach (var kvp in validations)
            {
                var (isValid, error) = kvp.Value.Item2(kvp.Value.Item1);
                if (!isValid) errors.Add(error);
            }

            if (PIC.SelectedIndex < 0)
                errors.Add("Please select a payment category");

            if (errors.Any())
            {
                await ShowErrorSheet("Validation Failed",
                    string.Join("\n• ", errors.Prepend("Please fix the following errors:")));
                return false;
            }
            return true;
        }

        private InvoiceRequest CreateInvoiceData() => new InvoiceRequest
        {
            TaxId = texid.Text?.Trim(),
            FullName = fullname.Text?.Trim(),
            Email = LoginPage.myemail,
            PhoneNumber = userphonenumber.Text?.Trim(),
            MDA = LoginPage.mycourt,
            CaseFileNumber = CaseNumber.Text,
            PaymentItem = _paymentData[PIC.SelectedIndex].serviceName,
            Amount = decimal.Parse(Regex.Replace(useramount.Text, @"[₦,\s]", "")).ToString()
        };

        private async Task<ApiResponse<InvoiceResult>> GenerateInvoiceAsync(InvoiceRequest request)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (m, c, ch, e) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                   System.Security.Authentication.SslProtocols.Tls11
                };

                var nvc = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("TaxId",         request.TaxId          ?? ""),
                    new KeyValuePair<string, string>("FullName",       request.FullName        ?? ""),
                    new KeyValuePair<string, string>("Email",          request.Email           ?? ""),
                    new KeyValuePair<string, string>("PhoneNumber",    request.PhoneNumber     ?? ""),
                    new KeyValuePair<string, string>("MDA",            request.MDA             ?? ""),
                    new KeyValuePair<string, string>("PaymentItem",    request.PaymentItem     ?? ""),
                    new KeyValuePair<string, string>("CaseFileNumber", request.CaseFileNumber  ?? ""),
                    new KeyValuePair<string, string>("Amount",         request.Amount          ?? ""),
                };

                using (var client = new HttpClient(handler)
                { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) })
                {
                    var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://yobejud.osoftpay.net/Api/KadunaJFAPI/GenerateInvoice")
                    { Content = new FormUrlEncodedContent(nvc) };

                    var response = await client.SendAsync(req);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        if (string.IsNullOrWhiteSpace(responseContent))
                            return new ApiResponse<InvoiceResult>
                            { Success = false, ErrorMessage = "Empty response from server" };

                        var result = JsonConvert.DeserializeObject<InvoiceResult>(responseContent);
                        if (result == null)
                            return new ApiResponse<InvoiceResult>
                            { Success = false, ErrorMessage = "Invalid response format" };

                        result.ReferenceNumber = ExtractReferenceNumber(result.message);
                        return new ApiResponse<InvoiceResult> { Success = true, Data = result };
                    }
                    else
                    {
                        string errorMessage = string.Format("Server returned {0}", response.StatusCode);
                        try
                        {
                            var errResp = JsonConvert.DeserializeObject<ErrorResponse>(responseContent);
                            if (!string.IsNullOrEmpty(errResp?.message)) errorMessage = errResp.message;
                            else if (!string.IsNullOrEmpty(errResp?.error)) errorMessage = errResp.error;
                        }
                        catch { }
                        return new ApiResponse<InvoiceResult>
                        { Success = false, ErrorMessage = errorMessage, ErrorDetails = responseContent };
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return new ApiResponse<InvoiceResult>
                { Success = false, ErrorMessage = "Request timed out. Please check your connection." };
            }
            catch (HttpRequestException ex)
            {
                return new ApiResponse<InvoiceResult>
                { Success = false, ErrorMessage = "Network error", ErrorDetails = ex.Message };
            }
            catch (JsonException ex)
            {
                return new ApiResponse<InvoiceResult>
                { Success = false, ErrorMessage = "Invalid server response", ErrorDetails = ex.Message };
            }
            catch (Exception ex)
            {
                return new ApiResponse<InvoiceResult>
                { Success = false, ErrorMessage = "Unexpected error", ErrorDetails = ex.Message };
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SUCCESS SHEET
        // ─────────────────────────────────────────────────────────────────────

        private async Task ShowSuccessSheet(InvoiceResult result)
        {
            try
            {
                _lastInvoiceNumber = result.ReferenceNumber;
                _lastInvoiceResult = result;
                summaryName.Text = fullname.Text;
                rrr.Text = result.ReferenceNumber;
                summaryCategory.Text = PIC.Items[PIC.SelectedIndex];
                summaryAmount.Text = amountFormatted.Text;

                successDescription.Text =
                    "Invoice generated! Tap the invoice number to copy it.";

                var receipt = BuildInvoiceReceiptData(result, isReprint: false);
                _lastReceiptData = receipt;

                await AttemptPrintAsync(receipt, isReprint: false);
                await ShowSheet(successSheet, sheetOverlay);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] ShowSuccessSheet: {0}", ex));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  TAP INVOICE NUMBER TO COPY
        // ─────────────────────────────────────────────────────────────────────

        private async void OnInvoiceNumberTapped(object sender, EventArgs e)
        {
            try
            {
                var invoiceNo = _lastInvoiceNumber ?? rrr?.Text;
                if (string.IsNullOrWhiteSpace(invoiceNo)) return;

                await Clipboard.SetTextAsync(invoiceNo);

                var originalColor = rrr.TextColor;
                var originalText = rrr.Text;
                rrr.TextColor = Color.FromHex("#27AE60");
                rrr.Text = "✓ Copied!";
                await Task.Delay(1200);
                rrr.Text = originalText;
                rrr.TextColor = originalColor;

                UserDialogs.Instance.Toast(
                    string.Format("Invoice number copied: {0}", invoiceNo),
                    TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] OnInvoiceNumberTapped: {0}", ex.Message));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INVOICE RECEIPT BUILDER
        // ─────────────────────────────────────────────────────────────────────

        private ReceiptData BuildInvoiceReceiptData(InvoiceResult result, bool isReprint = false)
        {
            decimal pendingAmount = 0m;
            if (!string.IsNullOrWhiteSpace(useramount.Text))
                decimal.TryParse(Regex.Replace(useramount.Text, @"[₦,\s]", ""), out pendingAmount);

            var items = new List<ReceiptItem>();

            items.Add(new ReceiptItem
            {
                Description = "INVOICE NUMBER",
                Amount = 0m,
                SubText = result.ReferenceNumber ?? result.InvoiceId ?? "N/A"
            });

            if (isReprint)
                items.Add(new ReceiptItem
                {
                    Description = "** REPRINTED COPY **",
                    Amount = 0m,
                    SubText = string.Format("Reprinted: {0:dd MMM yyyy HH:mm}", DateTime.Now)
                });

            items.Add(new ReceiptItem { Description = "Payment Item", Amount = 0m, SubText = summaryCategory.Text ?? "N/A" });
            items.Add(new ReceiptItem { Description = "Payer Name", Amount = 0m, SubText = fullname.Text?.Trim() ?? "N/A" });
            items.Add(new ReceiptItem { Description = "Tax ID", Amount = 0m, SubText = texid.Text?.Trim() ?? "N/A" });
            items.Add(new ReceiptItem { Description = "MDA", Amount = 0m, SubText = LoginPage.mymda ?? "N/A" });

            if (!string.IsNullOrWhiteSpace(CaseNumber.Text))
                items.Add(new ReceiptItem { Description = "Case No.", Amount = 0m, SubText = CaseNumber.Text });

            items.Add(new ReceiptItem { Description = "Amount Due", Amount = pendingAmount, SubText = null });

            return new ReceiptData
            {
                StoreName = "YOBE STATE JUDICIARY",
                StorePhone = "Contact us: +234 803 052 3208, +234 907 070 1616",
                ReceiptNumber = result.ReferenceNumber ?? "N/A",
                AgentName = LoginPage.MyfullName ?? "N/A",
                CollectionPoint = LoginPage.mycourt ?? LoginPage.mymda ?? "N/A",
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = 0m,
                FooterLine1 = "Present this invoice at the payment counter",
                FooterLine2 = "POWERED BY OSOFTPAY",
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
                    "[Generate] Print already in progress – skipped duplicate call.");
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
                                    isReprint
                                        ? "Invoice receipt reprinted successfully!"
                                        : "Invoice receipt printed.",
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
                        string.Format("[Generate] PrinterException: {0}", pex));
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
                            "Print timed out. Tap Reprint Invoice Receipt when printer is ready.",
                            TimeSpan.FromSeconds(8));
                        ShowReprintButton();
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        string.Format("[Generate] Print error: {0}", ex));
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast(
                            "Printer not connected. Tap Reprint Invoice Receipt to try again.",
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

        private async void OnReprintInvoiceClicked(object sender, EventArgs e)
        {
            if (_lastInvoiceResult == null)
            {
                UserDialogs.Instance.Toast("No invoice data available.", TimeSpan.FromSeconds(4));
                return;
            }

            ReprintInvoiceButton.IsEnabled = false;
            ReprintInvoiceButton.Text = "🖨️ Reprinting...";

            try
            {
                var reprintReceipt = BuildInvoiceReceiptData(_lastInvoiceResult, isReprint: true);
                await AttemptPrintAsync(reprintReceipt, isReprint: true);
            }
            finally
            {
                ReprintInvoiceButton.IsEnabled = true;
                ReprintInvoiceButton.Text = "🖨️ Reprint Invoice Receipt";
            }
        }

        private void ShowReprintButton()
        {
            try
            {
                ReprintInvoiceButton.IsVisible = true;
                ReprintInvoiceButton.Opacity = 0;
                ReprintInvoiceButton.FadeTo(1, 300, Easing.CubicOut);
            }
            catch { }
        }

        private void HideReprintButton()
        {
            try { ReprintInvoiceButton.IsVisible = false; } catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHEET HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private async Task ShowErrorSheet(string errorMessage, string errorDetails = null)
        {
            try
            {
                errorDescription.Text = errorMessage;
                errorDetailsContainer.IsVisible = !string.IsNullOrEmpty(errorDetails);
                await ShowSheet(errorSheet, sheetOverlay);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] ShowErrorSheet: {0}", ex));
            }
        }

        private async Task ShowSheet(PancakeView sheet, Grid overlay)
        {
            try
            {
                overlay.IsVisible = true;
                await overlay.FadeTo(1, SheetAnimationDuration);
                sheet.IsVisible = true;
                await sheet.TranslateTo(0, 0, SheetAnimationDuration, Easing.CubicOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] ShowSheet: {0}", ex.Message));
            }
        }

        private async Task HideSheet(PancakeView sheet, Grid overlay)
        {
            try
            {
                await sheet.TranslateTo(0, 1000, SheetAnimationDuration, Easing.CubicIn);
                sheet.IsVisible = false;
                await overlay.FadeTo(0, SheetAnimationDuration);
                overlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] HideSheet: {0}", ex.Message));
            }
        }

        private async void OnOverlayTapped(object sender, EventArgs e)
        {
            if (successSheet.IsVisible) await HideSheet(successSheet, sheetOverlay);
            else if (errorSheet.IsVisible) await HideSheet(errorSheet, sheetOverlay);
        }

        private async void OnCloseSheetTapped(object sender, EventArgs e)
            => await HideSheet(successSheet, sheetOverlay);

        private async void OnCloseErrorSheetTapped(object sender, EventArgs e)
            => await HideSheet(errorSheet, sheetOverlay);

        private async void OnGenerateAnotherTapped(object sender, EventArgs e)
        {
            await HideSheet(successSheet, sheetOverlay);
            ClearForm();
        }

        private async void OnContinueTapped(object sender, EventArgs e)
            => await HideSheet(successSheet, sheetOverlay);

        private async void OnTryAgainTapped(object sender, EventArgs e)
            => await HideSheet(errorSheet, sheetOverlay);

        private void ClearForm()
        {
            try
            {
                texid.Text = string.Empty;
                fullname.Text = string.Empty;
                CaseNumber.Text = string.Empty;
                userphonenumber.Text = string.Empty;
                useramount.Text = string.Empty;
                PIC.SelectedIndex = -1;

                foreach (var key in _fieldValidationStatus.Keys.ToList())
                    _fieldValidationStatus[key] = false;

                _validationErrors.Clear();
                CompletedFields = 0;
                _lastReceiptData = null;
                _lastInvoiceResult = null;
                _lastInvoiceNumber = null;

                ResetFieldStyles();
                HideAllValidationMessages();
                HideReprintButton();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("[Generate] ClearForm: {0}", ex));
            }
        }

        private void ResetFieldStyles()
        {
            var def = (Style)Resources["EntryContainerStyle"];
            taxIdFrame.Style = def;
            fullNameFrame.Style = def;
            CaseFrame.Style = def;
            phoneFrame.Style = def;
            categoryFrame.Style = def;
            amountFrame.Style = def;
        }

        private void HideAllValidationMessages()
        {
            taxIdError.IsVisible = false;
            fullNameError.IsVisible = false;
            phoneError.IsVisible = false;
            categoryError.IsVisible = false;
            amountError.IsVisible = false;
            amountFormatted.IsVisible = false;

            taxIdValidIcon.IsVisible = false;
            fullNameValidIcon.IsVisible = false;
            phoneValidIcon.IsVisible = false;
            categoryValidIcon.IsVisible = false;
            amountValidIcon.IsVisible = false;
        }

        private static string ExtractReferenceNumber(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message ?? string.Empty;
            var match = Regex.Match(message, @"\d{6,}");
            return match.Success ? match.Value : message;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PROPERTY CHANGED
        // ─────────────────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(
            [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DATA MODELS
    // ══════════════════════════════════════════════════════════════════════════

    internal class MDAData { public string name { get; set; } }
    internal class PaymentData { public string serviceName { get; set; } }

    public class InvoiceRequest
    {
        public string TaxId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string CaseFileNumber { get; set; }
        public string MDA { get; set; }
        public string PaymentItem { get; set; }
        public string Amount { get; set; }
    }

    public class InvoiceResult
    {
        public string message { get; set; }
        public string status_code { get; set; }
        public string InvoiceId { get; set; }
        public string ReferenceNumber { get; set; }
        public string PaymentUrl { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Status { get; set; }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorDetails { get; set; }
    }

    public class ErrorResponse
    {
        public string message { get; set; }
        public string error { get; set; }
        public string status_code { get; set; }
        public string details { get; set; }
    }
}