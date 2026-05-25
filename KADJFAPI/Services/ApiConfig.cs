using System;

namespace KADJFAPI.Services
{
    /// <summary>
    /// Single source of truth for every backend endpoint.
    /// Migrated from the legacy Yobe Judiciary hosts
    /// (yobejud / Kadunajud) to the Kaduna host.
    ///
    /// To re-point the entire app at a different server,
    /// change <see cref="BaseUrl"/> here — nowhere else.
    /// </summary>
    public static class ApiConfig
    {

        public const string BaseUrl = "https://kadjud.osoftpay.net/Api/KadunaJFAPI";

        public static string Login(string email, string password) =>
            $"{BaseUrl}/Login" +
            $"?Email={Uri.EscapeDataString(email)}" +
            $"&Password={Uri.EscapeDataString(password)}";

        // ... other endpoints


        /// <summary>POST GenerateInvoice  (x-www-form-urlencoded)</summary>
        public static string GenerateInvoice => $"{BaseUrl}/GenerateInvoice";

        /// <summary>GET  PaymentItems  (no query params on the new API)</summary>
        public static string PaymentItems => $"{BaseUrl}/PaymentItems";

        /// <summary>GET  MDA  (list of ministries/courts)</summary>
        public static string Mda => $"{BaseUrl}/MDA";

        /// <summary>GET  InvoiceHistory?Mda=  (court-wide history, not per-agent)</summary>
        public static string InvoiceHistory(string mda) =>
            $"{BaseUrl}/InvoiceHistory?Mda={Uri.EscapeDataString(mda ?? string.Empty)}";

        /// <summary>GET  VerifyPayment?Id=</summary>
        public static string VerifyPayment(string id) =>
            $"{BaseUrl}/VerifyPayment?Id={Uri.EscapeDataString(id ?? string.Empty)}";

        /// <summary>
        /// POST Make_payment.
        /// NOTE: this endpoint is NOT in the supplied API spec.
        /// Pointed at the new host on the assumption the name is unchanged —
        /// confirm with the backend team.
        /// </summary>
        public static string MakePayment => $"{BaseUrl}/Make_payment";
    }
}