using Acr.UserDialogs;
using KADJFAPI.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace KADJFAPI.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage, INotifyPropertyChanged
    {
        #region Models

        public class HistoryData
        {
            // API returns these fields — all nullable-safe
            public int id { get; set; }
            public string caseFileNumber { get; set; }
            public string taxId { get; set; }
            public string fullName { get; set; }
            public string email { get; set; }
            public string phoneNumber { get; set; }
            public string mda { get; set; }
            public string paymentItem { get; set; }
            public string paymentGateway { get; set; }

            // API sends amount as a numeric decimal — use object so JsonConvert handles both string & number
            [JsonProperty("amount")]
            public object amountRaw { get; set; }

            public string paymentReference { get; set; }
            public string status { get; set; }
            public string dateRecorded { get; set; }
            public int filled { get; set; }
            public int formId { get; set; }
            public string sessionId { get; set; }
            public string payer_name { get; set; }
            public string payer_email { get; set; }
            public string payment_item { get; set; }   // snake_case duplicate from API
            public string lga { get; set; }
            public string superagent { get; set; }
            public string date_of_payment { get; set; }
            public string court { get; set; }

            // ── Computed helpers ──────────────────────────────────────────────

            /// <summary>Safely parse amount regardless of whether it arrived as number or string.</summary>
            public decimal NumericAmount
            {
                get
                {
                    try
                    {
                        if (amountRaw == null) return 0m;
                        string raw = amountRaw.ToString().Replace("₦", "").Replace(",", "").Trim();
                        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                                               System.Globalization.CultureInfo.InvariantCulture,
                                               out decimal val) ? val : 0m;
                    }
                    catch { return 0m; }
                }
            }

            public string FormattedAmount
            {
                get
                {
                    try { return $"₦{NumericAmount:N2}"; }
                    catch { return "₦0.00"; }
                }
            }

            /// <summary>Effective payment item — falls back to snake_case twin.</summary>
            public string EffectivePaymentItem =>
                !string.IsNullOrWhiteSpace(paymentItem) ? paymentItem :
                !string.IsNullOrWhiteSpace(payment_item) ? payment_item : "N/A";

            public string ServiceNameTruncated
            {
                get
                {
                    try
                    {
                        string item = EffectivePaymentItem;
                        return item.Length > 30 ? item.Substring(0, 30) + "…" : item;
                    }
                    catch { return "N/A"; }
                }
            }

            public string StatusColor
            {
                get
                {
                    string s = (status ?? "").Trim().ToLowerInvariant();
                    switch (s)
                    {
                        case "successful":
                        case "paid":
                        case "completed":
                        case "success":
                            return "Green";
                        case "pending":
                        case "processing":
                            return "Orange";
                        case "failed":
                        case "fail":
                        case "declined":
                        case "cancelled":
                        case "canceled":
                            return "Red";
                        default: return "Gray";
                    }
                }
            }

            public DateTime ParsedDate
            {
                get
                {
                    try
                    {
                        if (DateTime.TryParse(dateRecorded, out var d1)) return d1;
                        if (DateTime.TryParse(date_of_payment, out var d2)) return d2;
                    }
                    catch { }
                    return DateTime.MinValue;
                }
            }

            public string FormattedDate
            {
                get
                {
                    try
                    {
                        var d = ParsedDate;
                        return d != DateTime.MinValue ? d.ToString("MMM dd, yyyy hh:mm tt") : "—";
                    }
                    catch { return "—"; }
                }
            }

            /// <summary>Display name: prefer payer_name, fall back to fullName.</summary>
            public string DisplayName =>
                !string.IsNullOrWhiteSpace(payer_name) ? payer_name :
                !string.IsNullOrWhiteSpace(fullName) ? fullName : "Unknown";

            /// <summary>Display email: prefer payer_email, fall back to email.</summary>
            public string DisplayEmail =>
                !string.IsNullOrWhiteSpace(payer_email) ? payer_email :
                !string.IsNullOrWhiteSpace(email) ? email : "—";
        }

        public class HistoryStatistics
        {
            public int TotalTransactions { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal SuccessfulAmount { get; set; }
            public decimal PendingAmount { get; set; }
            public decimal FailedAmount { get; set; }
            public int PaidTransactions { get; set; }
            public int PendingTransactions { get; set; }
            public int FailedTransactions { get; set; }

            public string FormattedTotalAmount => $"₦{TotalAmount:N2}";
            public string FormattedSuccessfulAmount => $"₦{SuccessfulAmount:N2}";
            public string FormattedPendingAmount => $"₦{PendingAmount:N2}";
            public string FormattedFailedAmount => $"₦{FailedAmount:N2}";
        }

        #endregion

        #region Properties

        private ObservableCollection<HistoryData> _historyItems;
        public ObservableCollection<HistoryData> HistoryItems
        {
            get => _historyItems ?? (_historyItems = new ObservableCollection<HistoryData>());
            set { _historyItems = value; OnPropertyChanged(); }
        }

        private List<HistoryData> _allHistoryData = new List<HistoryData>();
        private List<HistoryData> _currentFilteredData = new List<HistoryData>();

        private HistoryStatistics _statistics;
        public HistoryStatistics Statistics
        {
            get => _statistics ?? (_statistics = new HistoryStatistics());
            set { _statistics = value; OnPropertyChanged(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotLoading));
                OnPropertyChanged(nameof(HasNoData));
            }
        }

        public bool IsNotLoading => !IsLoading;

        private bool _hasData;
        public bool HasData
        {
            get => _hasData;
            set
            {
                _hasData = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasNoData));
            }
        }

        public bool HasNoData => !HasData && !IsLoading;

        private string _searchText;
        public string SearchText
        {
            get => _searchText ?? string.Empty;
            set { _searchText = value; OnPropertyChanged(); }
        }

        private string _selectedFilter = "All";
        public string SelectedFilter
        {
            get => _selectedFilter ?? "All";
            set
            {
                _selectedFilter = value ?? "All";
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private CancellationTokenSource _searchCts;

        #endregion

        #region Constructor

        public History()
        {
            try
            {
                InitializeComponent();
                BindingContext = this;
                HistoryItems = new ObservableCollection<HistoryData>();
                Statistics = new HistoryStatistics();
                _allHistoryData = new List<HistoryData>();
                _currentFilteredData = new List<HistoryData>();
                InitializePage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Constructor Error: {ex}");
                SafeInitDefaults();
            }
        }

        private void SafeInitDefaults()
        {
            try
            {
                if (HistoryItems == null) HistoryItems = new ObservableCollection<HistoryData>();
                if (Statistics == null) Statistics = new HistoryStatistics();
                if (_allHistoryData == null) _allHistoryData = new List<HistoryData>();
                if (_currentFilteredData == null) _currentFilteredData = new List<HistoryData>();
            }
            catch { /* absolute last resort */ }
        }

        #endregion

        #region Initialization

        private async void InitializePage()
        {
            try
            {
                SetDateDisplay();
                await LoadHistoryData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] InitializePage Error: {ex}");
                SafeShowError("Could not initialise page. Please restart the app.");
            }
        }

        private void SetDateDisplay()
        {
            try
            {
                var now = DateTime.Now;
                if (fixeddate != null) fixeddate.Text = now.ToString("dd");
                if (fixmonth != null) fixmonth.Text = now.ToString("MMMM");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] SetDateDisplay Error: {ex}");
                try
                {
                    if (fixeddate != null) fixeddate.Text = DateTime.Now.Day.ToString("D2");
                    if (fixmonth != null) fixmonth.Text = DateTime.Now.ToString("MMMM");
                }
                catch { }
            }
        }

        #endregion

        #region Data Loading

        private async Task LoadHistoryData()
        {
            if (IsLoading) return;

            IsLoading = true;
            HasData = false;

            try
            {
                // Show loading dialog safely
                IProgressDialog loadingDialog = null;
                try { loadingDialog = UserDialogs.Instance.Loading("Loading transaction history…"); }
                catch { }

                try
                {
                    await Task.Delay(300); // brief UX delay

                    var historyData = await FetchHistoryFromAPI();

                    if (historyData != null && historyData.Count > 0)
                    {
                        _allHistoryData = historyData
                            .Where(x => x != null)
                            .OrderByDescending(x => x.ParsedDate)
                            .ToList();

                        ApplyFilters();
                        HasData = true;
                    }
                    else
                    {
                        // Empty list — NOT an error, just no transactions yet
                        _allHistoryData = new List<HistoryData>();
                        _currentFilteredData = new List<HistoryData>();
                        Device.BeginInvokeOnMainThread(() =>
                        {
                            try { HistoryItems.Clear(); } catch { }
                        });
                        Statistics = new HistoryStatistics();
                        HasData = false;
                    }
                }
                finally
                {
                    try { loadingDialog?.Hide(); loadingDialog?.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] LoadHistoryData Error: {ex}");
                HasData = false;
                SafeShowError(GetUserFriendlyErrorMessage(ex));
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Fetches from API. Returns empty list on any error — never throws to caller.
        /// The API always returns a raw JSON array (never a wrapped object).
        /// Amount arrives as a numeric decimal.
        /// </summary>
        private async Task<List<HistoryData>> FetchHistoryFromAPI()
        {
            const int maxRetries = 3;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    string mda = string.Empty;
                    try { mda = LoginPage.mymda ?? string.Empty; } catch { }

                    if (string.IsNullOrWhiteSpace(mda))
                    {
                        System.Diagnostics.Debug.WriteLine("[History] MDA is empty — cannot fetch history.");
                        return new List<HistoryData>();
                    }

                    string url = ApiConfig.InvoiceHistory(mda);

                    System.Diagnostics.Debug.WriteLine($"[History] Fetching: {url}");

                    var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (msg, cert, chain, errs) => true,
                        SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                     | System.Security.Authentication.SslProtocols.Tls11
                    };

                    using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                    {
                        var response = await client.GetAsync(url).ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            string reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                            System.Diagnostics.Debug.WriteLine($"[History] HTTP {(int)response.StatusCode}: {reason}");
                            if (attempt == maxRetries - 1)
                                throw new HttpRequestException($"Server error {(int)response.StatusCode}: {reason}");
                            await Task.Delay(1000 * (attempt + 1));
                            continue;
                        }

                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        System.Diagnostics.Debug.WriteLine($"[History] Raw JSON length: {json?.Length ?? 0}");

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            System.Diagnostics.Debug.WriteLine("[History] Empty body returned.");
                            return new List<HistoryData>();
                        }

                        json = json.Trim();

                        // ── The API always returns a plain array ──────────────────────
                        if (json.StartsWith("["))
                        {
                            // Parse via JArray so we can safely coerce amount (number OR string)
                            var jArray = JArray.Parse(json);
                            var result = new List<HistoryData>();

                            foreach (var token in jArray)
                            {
                                try
                                {
                                    var item = ParseHistoryItem(token as JObject);
                                    if (item != null) result.Add(item);
                                }
                                catch (Exception itemEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[History] Skipping item: {itemEx.Message}");
                                }
                            }

                            System.Diagnostics.Debug.WriteLine($"[History] Parsed {result.Count} records.");
                            return result;
                        }

                        // Fallback: wrapped object { "Data": [...] }  or { "data": [...] }
                        if (json.StartsWith("{"))
                        {
                            var jObj = JObject.Parse(json);
                            JToken dataToken = jObj["Data"] ?? jObj["data"] ?? jObj["items"] ?? jObj["Items"];
                            if (dataToken is JArray wrappedArray)
                            {
                                var result = new List<HistoryData>();
                                foreach (var token in wrappedArray)
                                {
                                    try
                                    {
                                        var item = ParseHistoryItem(token as JObject);
                                        if (item != null) result.Add(item);
                                    }
                                    catch { }
                                }
                                return result;
                            }
                        }

                        System.Diagnostics.Debug.WriteLine("[History] Unrecognised JSON shape.");
                        return new List<HistoryData>();
                    }
                }
                catch (TaskCanceledException tcEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[History] Timeout on attempt {attempt + 1}: {tcEx.Message}");
                    if (attempt == maxRetries - 1)
                        throw new Exception("Request timed out. Please check your internet connection.");
                    await Task.Delay(1500 * (attempt + 1));
                }
                catch (HttpRequestException httpEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[History] Network error on attempt {attempt + 1}: {httpEx.Message}");
                    if (attempt == maxRetries - 1)
                        throw new Exception($"Network error: {httpEx.Message}");
                    await Task.Delay(1500 * (attempt + 1));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[History] Unexpected error on attempt {attempt + 1}: {ex}");
                    if (attempt == maxRetries - 1) throw;
                    await Task.Delay(1500 * (attempt + 1));
                }
            }

            return new List<HistoryData>();
        }

        /// <summary>Safely parse a single JObject into HistoryData, handling numeric amount.</summary>
        private HistoryData ParseHistoryItem(JObject obj)
        {
            if (obj == null) return null;

            try
            {
                var item = new HistoryData
                {
                    id = obj["id"]?.ToObject<int>() ?? 0,
                    caseFileNumber = obj["caseFileNumber"]?.ToString(),
                    taxId = obj["taxId"]?.ToString(),
                    fullName = obj["fullName"]?.ToString(),
                    email = obj["email"]?.ToString(),
                    phoneNumber = obj["phoneNumber"]?.ToString(),
                    mda = obj["mda"]?.ToString(),
                    paymentItem = obj["paymentItem"]?.ToString(),
                    paymentGateway = obj["paymentGateway"]?.ToString(),
                    paymentReference = obj["paymentReference"]?.ToString(),
                    status = obj["status"]?.ToString(),
                    dateRecorded = obj["dateRecorded"]?.ToString(),
                    filled = obj["filled"]?.ToObject<int>() ?? 0,
                    formId = obj["formId"]?.ToObject<int>() ?? 0,
                    sessionId = obj["sessionId"]?.ToString(),
                    payer_name = obj["payer_name"]?.ToString(),
                    payer_email = obj["payer_email"]?.ToString(),
                    payment_item = obj["payment_item"]?.ToString(),
                    lga = obj["lga"]?.ToString(),
                    superagent = obj["superagent"]?.ToString(),
                    date_of_payment = obj["date_of_payment"]?.ToString(),
                    court = obj["court"]?.ToString(),
                    // Keep raw amount token so NumericAmount can parse it
                    amountRaw = obj["amount"]?.ToString()
                };

                return item;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] ParseHistoryItem failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Data Processing

        private void UpdateHistoryDisplay(List<HistoryData> data)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    HistoryItems.Clear();
                    if (data == null) return;
                    foreach (var item in data)
                        if (item != null) HistoryItems.Add(item);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[History] UpdateHistoryDisplay Error: {ex}");
                }
            });
        }

        private void CalculateStatistics(List<HistoryData> data)
        {
            try
            {
                var stats = new HistoryStatistics();

                if (data != null && data.Count > 0)
                {
                    stats.TotalTransactions = data.Count;

                    foreach (var item in data.Where(x => x != null))
                    {
                        try
                        {
                            decimal amt = item.NumericAmount;
                            stats.TotalAmount += amt;

                            string s = (item.status ?? "").Trim().ToLowerInvariant();
                            if (s == "successful" || s == "paid" || s == "completed" || s == "success")
                            {
                                stats.SuccessfulAmount += amt;
                                stats.PaidTransactions++;
                            }
                            else if (s == "pending" || s == "processing")
                            {
                                stats.PendingAmount += amt;
                                stats.PendingTransactions++;
                            }
                            else
                            {
                                stats.FailedAmount += amt;
                                stats.FailedTransactions++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[History] Stats item error: {ex.Message}");
                        }
                    }
                }

                Device.BeginInvokeOnMainThread(() => { Statistics = stats; });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] CalculateStatistics Error: {ex}");
                Device.BeginInvokeOnMainThread(() => { Statistics = new HistoryStatistics(); });
            }
        }

        #endregion

        #region Search & Filter

        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                var token = _searchCts.Token;
                var text = e?.NewTextValue?.Trim();

                try
                {
                    await Task.Delay(300, token);
                    if (!token.IsCancellationRequested)
                        await PerformSearch(text);
                }
                catch (OperationCanceledException) { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Search changed error: {ex}");
            }
        }

        private Task PerformSearch(string searchText)
        {
            try
            {
                if (_allHistoryData == null) return Task.CompletedTask;

                var filtered = string.IsNullOrWhiteSpace(searchText)
                    ? _allHistoryData.ToList()
                    : _allHistoryData.Where(item => item != null && MatchesSearch(item, searchText.ToLowerInvariant())).ToList();

                filtered = ApplyStatusFilter(filtered, SelectedFilter);

                _currentFilteredData = filtered;
                UpdateHistoryDisplay(filtered);
                CalculateStatistics(filtered);
                HasData = filtered.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] PerformSearch error: {ex}");
            }
            return Task.CompletedTask;
        }

        private bool MatchesSearch(HistoryData item, string lowerSearch)
        {
            return (item.taxId?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.fullName?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.payer_name?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.paymentItem?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.payment_item?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.paymentReference?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.email?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.payer_email?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.phoneNumber?.ToLowerInvariant().Contains(lowerSearch) ?? false)
                || (item.mda?.ToLowerInvariant().Contains(lowerSearch) ?? false);
        }

        private void ApplyFilters()
        {
            try
            {
                if (_allHistoryData == null) return;

                var filtered = ApplyStatusFilter(_allHistoryData, SelectedFilter);

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string lower = SearchText.ToLowerInvariant();
                    filtered = filtered.Where(item => item != null && MatchesSearch(item, lower)).ToList();
                }

                _currentFilteredData = filtered;
                UpdateHistoryDisplay(filtered);
                CalculateStatistics(filtered);
                HasData = filtered.Count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] ApplyFilters error: {ex}");
            }
        }

        private List<HistoryData> ApplyStatusFilter(List<HistoryData> data, string filter)
        {
            try
            {
                if (data == null) return new List<HistoryData>();
                if (string.IsNullOrEmpty(filter) || filter.Equals("All", StringComparison.OrdinalIgnoreCase))
                    return data.ToList();

                string f = filter.Trim().ToLowerInvariant();
                switch (f)
                {
                    case "paid":
                        return data.Where(x => x != null && IsSuccessfulStatus(x.status)).ToList();
                    case "pending":
                        return data.Where(x => x != null && IsPendingStatus(x.status)).ToList();
                    case "failed":
                        return data.Where(x => x != null && IsFailedStatus(x.status)).ToList();
                    default:
                        return data.ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] ApplyStatusFilter error: {ex}");
                return data?.ToList() ?? new List<HistoryData>();
            }
        }

        private bool IsSuccessfulStatus(string status)
        {
            string s = (status ?? "").Trim().ToLowerInvariant();
            return s == "successful" || s == "paid" || s == "completed" || s == "success";
        }

        private bool IsPendingStatus(string status)
        {
            string s = (status ?? "").Trim().ToLowerInvariant();
            return s == "pending" || s == "processing";
        }

        private bool IsFailedStatus(string status)
        {
            string s = (status ?? "").Trim().ToLowerInvariant();
            return s == "failed" || s == "fail" || s == "declined" || s == "cancelled" || s == "canceled";
        }

        #endregion

        #region Event Handlers

        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            try { await LoadHistoryData(); }
            catch (Exception ex) { SafeShowError(GetUserFriendlyErrorMessage(ex)); }
        }

        private async void OnRetryClicked(object sender, EventArgs e)
        {
            try { await LoadHistoryData(); }
            catch (Exception ex) { SafeShowError(GetUserFriendlyErrorMessage(ex)); }
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            try
            {
                if (sender is Picker picker)
                    SelectedFilter = picker.SelectedItem?.ToString() ?? "All";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] OnFilterChanged error: {ex}");
            }
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
                _searchCts?.Cancel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] OnDisappearing error: {ex}");
            }
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            try
            {
                string reference = string.Empty;

                if (sender is View view && view.BindingContext is HistoryData txn)
                    reference = txn.paymentReference ?? string.Empty;

                if (string.IsNullOrEmpty(reference) && sender is Label lbl)
                    reference = lbl.Text ?? string.Empty;

                // Walk up the visual tree as last resort
                if (string.IsNullOrEmpty(reference) && sender is View sv)
                {
                    var parent = sv.Parent;
                    while (parent != null)
                    {
                        if (parent.BindingContext is HistoryData pd)
                        {
                            reference = pd.paymentReference ?? string.Empty;
                            break;
                        }
                        parent = parent.Parent;
                    }
                }

                if (!string.IsNullOrEmpty(reference))
                {
                    await Clipboard.SetTextAsync(reference);
                    try
                    {
                        UserDialogs.Instance.Toast($"Reference copied: {reference}", TimeSpan.FromSeconds(3));
                    }
                    catch { await DisplayAlert("Copied", $"Reference copied: {reference}", "OK"); }

                    // Brief colour flash feedback
                    if (sender is Label feedbackLabel)
                    {
                        try
                        {
                            var orig = feedbackLabel.TextColor;
                            feedbackLabel.TextColor = Color.LimeGreen;
                            await Task.Delay(600);
                            feedbackLabel.TextColor = orig;
                        }
                        catch { }
                    }
                }
                else
                {
                    await DisplayAlert("Info", "No reference number found to copy.", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Copy reference error: {ex}");
                try { await DisplayAlert("Error", "Could not copy reference number.", "OK"); } catch { }
            }
        }

        #endregion

        #region Error Handling

        private void SafeShowError(string message)
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                try { await DisplayAlert("Error", message, "OK"); }
                catch
                {
                    try { UserDialogs.Instance.Toast(message, TimeSpan.FromSeconds(4)); } catch { }
                }
            });
        }

        private string GetUserFriendlyErrorMessage(Exception ex)
        {
            try
            {
                if (ex is HttpRequestException)
                    return "Network error. Please check your internet connection and try again.";
                if (ex is TaskCanceledException)
                    return "Request timed out. Please try again.";
                if (ex is JsonException)
                    return "Unexpected data format received from server. Please contact support.";
                if (ex is UnauthorizedAccessException)
                    return "Session expired. Please log in again.";
                return "An unexpected error occurred. Please try again.";
            }
            catch { return "An error occurred. Please try again."; }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[History] PropertyChanged error: {ex}"); }
        }

        #endregion
    }
}