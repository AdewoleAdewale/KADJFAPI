using KADJFAPI.Services;
using KADJFAPI.Views;
using System;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace KADJFAPI
{
    public partial class App : Application
    {
        private const int SESSION_TIMEOUT_MINUTES = 60;
        private DateTime _lastActivityTime;
        private bool _isUserLoggedIn = false;
        private bool _isTimerRunning = false;
        public static string PrinterFooter { get; set; }
        public static string RevenueServiceName { get; set; }

        public static IPrinterService Printer { get; private set; }

        /// <summary>
        /// Durable job queue that persists receipts to disk so they can be
        /// retried automatically after a crash, Bluetooth drop, or app restart.
        /// All pages must use <c>App.PrintJobManager</c> rather than calling
        /// <c>Printer</c> directly.
        /// </summary>
        public static PrintJobManager PrintJobManager { get; private set; }
        public static string ThankYouMessage { get; set; }
        public App()
        {
            InitializeComponent();
            RevenueServiceName = "KADUNA STATE JUDICIARY ";
            PrinterFooter = "POWERED BY OSOFTPAY";
            ThankYouMessage = "CONTACT US : 08030523208";
            MainPage = new NavigationPage(new LoginPage());
            _lastActivityTime = DateTime.Now;
            Printer = new BluetoothPrinterService(use80mm: false);
            PrintJobManager = new PrintJobManager(Printer);


        }

        // Property to track user login status
        public bool IsUserLoggedIn
        {
            get => _isUserLoggedIn;
            set
            {
                _isUserLoggedIn = value;
                if (value)
                {
                    StartSessionTimer();
                }
                else
                {
                    StopSessionTimer();
                }
            }
        }

        // Method to update last activity time (call this from your pages on user interactions)
        public void UpdateLastActivity()
        {
            _lastActivityTime = DateTime.Now;
        }

        // Method to navigate to dashboard after successful login
        public void NavigateToDashboard()
        {
            IsUserLoggedIn = true;
            MainPage = new NavigationPage(new Dashboard());
            UpdateLastActivity();
        }

        // Method to handle logout
        public void Logout()
        {
            IsUserLoggedIn = false;
            MainPage = new NavigationPage(new LoginPage());
        }

        // Start the session timer
        private async void StartSessionTimer()
        {
            if (_isTimerRunning) return;

            _isTimerRunning = true;

            while (_isUserLoggedIn && _isTimerRunning)
            {
                await Task.Delay(60000); // Check every minute

                if (_isUserLoggedIn && DateTime.Now.Subtract(_lastActivityTime).TotalMinutes >= SESSION_TIMEOUT_MINUTES)
                {
                    await HandleSessionTimeout();
                    break;
                }
            }

            _isTimerRunning = false;
        }

        // Stop the session timer
        private void StopSessionTimer()
        {
            _isTimerRunning = false;
        }

        // Handle session timeout
        private async Task HandleSessionTimeout()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await Current.MainPage.DisplayAlert("Session Expired",
                    "Your session has expired due to inactivity. Please log in again.", "OK");
                Logout();
            });
        }

        protected override void OnStart()
        {
            // App is starting
            if (_isUserLoggedIn)
            {
                UpdateLastActivity();
                if (!_isTimerRunning)
                {
                    StartSessionTimer();
                }
            }
        }

        protected override void OnSleep()
        {
            // App is going to background/minimized
            // We don't stop the timer here to maintain session tracking
            // The timer continues to run in the background
        }

        protected override void OnResume()
        {
            // App is resuming from background
            if (_isUserLoggedIn)
            {
                UpdateLastActivity();

                // Check if session expired while app was in background
                if (DateTime.Now.Subtract(_lastActivityTime).TotalMinutes >= SESSION_TIMEOUT_MINUTES)
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        await HandleSessionTimeout();
                    });
                }
                else if (!_isTimerRunning)
                {
                    StartSessionTimer();
                }
            }
        }

        // Static method to access the current app instance
        public static new App Current => (App)Application.Current;
    }
}
