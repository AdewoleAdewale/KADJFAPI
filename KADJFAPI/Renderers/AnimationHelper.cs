using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace KADJFAPI.Views.Helpers
{
    /// <summary>
    /// Animation helper class for dashboard transitions
    /// </summary>
    public static class AnimationHelper
    {
        public static async Task PulseAnimation(View view, uint duration = 300)
        {
            try
            {
                await view.ScaleTo(1.1, duration / 2, Easing.CubicOut);
                await view.ScaleTo(1.0, duration / 2, Easing.CubicIn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pulse animation error: {ex.Message}");
            }
        }

        public static async Task ShakeAnimation(View view, uint duration = 500)
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    await view.TranslateTo(-10, 0, duration / 6, Easing.Linear);
                    await view.TranslateTo(10, 0, duration / 6, Easing.Linear);
                }
                await view.TranslateTo(0, 0, duration / 6, Easing.Linear);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shake animation error: {ex.Message}");
            }
        }

        public static async Task SlideInFromRight(View view, uint duration = 400)
        {
            try
            {
                view.TranslationX = 300;
                view.Opacity = 0;
                await Task.WhenAll(
                    view.TranslateTo(0, 0, duration, Easing.CubicOut),
                    view.FadeTo(1, duration, Easing.CubicOut)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slide animation error: {ex.Message}");
            }
        }

        public static async Task SlideInFromLeft(View view, uint duration = 400)
        {
            try
            {
                view.TranslationX = -300;
                view.Opacity = 0;
                await Task.WhenAll(
                    view.TranslateTo(0, 0, duration, Easing.CubicOut),
                    view.FadeTo(1, duration, Easing.CubicOut)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Slide animation error: {ex.Message}");
            }
        }

        public static async Task FadeInUp(View view, uint duration = 400, double distance = 50)
        {
            try
            {
                view.TranslationY = distance;
                view.Opacity = 0;
                await Task.WhenAll(
                    view.TranslateTo(0, 0, duration, Easing.CubicOut),
                    view.FadeTo(1, duration, Easing.CubicOut)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fade in up animation error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Service data models for dashboard
    /// </summary>
    public class JudiciaryService
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public string Icon { get; set; }
        public bool IsAvailable { get; set; }
        public ServiceCategory Category { get; set; }
    }

    public enum ServiceCategory
    {
        Affidavit,
        CourtFiling,
        Consultation,
        Certification,
        Other
    }

    /// <summary>
    /// Transaction and payment models
    /// </summary>
    public class PaymentTransaction
    {
        public string TransactionId { get; set; }
        public string ServiceId { get; set; }
        public string ServiceName { get; set; }
        public decimal Amount { get; set; }
        public DateTime TransactionDate { get; set; }
        public PaymentStatus Status { get; set; }
        public string Reference { get; set; }
        public string PaymentMethod { get; set; }
        public string UserId { get; set; }
    }

    public enum PaymentStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Cancelled,
        Refunded
    }

    /// <summary>
    /// Application tracking model
    /// </summary>
    public class Application
    {
        public string ApplicationId { get; set; }
        public string ServiceType { get; set; }
        public ApplicationStatus Status { get; set; }
        public DateTime SubmissionDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public string ApplicantName { get; set; }
        public string ApplicantEmail { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentReference { get; set; }
        public List<ApplicationDocument> Documents { get; set; }
    }

    public enum ApplicationStatus
    {
        Draft,
        Submitted,
        UnderReview,
        PaymentPending,
        Processing,
        Completed,
        Rejected,
        Cancelled
    }

    public class ApplicationDocument
    {
        public string DocumentId { get; set; }
        public string FileName { get; set; }
        public string DocumentType { get; set; }
        public DateTime UploadDate { get; set; }
        public long FileSize { get; set; }
        public string FilePath { get; set; }
    }

    /// <summary>
    /// Dashboard statistics model
    /// </summary>
    public class DashboardStats
    {
        public int PendingApplications { get; set; }
        public int CompletedApplications { get; set; }
        public decimal TotalAmountPaid { get; set; }
        public int ActiveConsultations { get; set; }
        public DateTime LastActivity { get; set; }
        public List<RecentActivity> RecentActivities { get; set; }
    }

    public class RecentActivity
    {
        public string ActivityId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public ActivityType Type { get; set; }
        public string Icon { get; set; }
        public string StatusColor { get; set; }
    }

    public enum ActivityType
    {
        ApplicationSubmitted,
        PaymentCompleted,
        DocumentUploaded,
        StatusChanged,
        ConsultationBooked,
        ServiceCompleted
    }

    /// <summary>
    /// Static service configuration
    /// </summary>
    public static class JudiciaryServices
    {
        public static List<JudiciaryService> GetAvailableServices()
        {
            return new List<JudiciaryService>
            {
                new JudiciaryService
                {
                    Id = "AFF001",
                    Name = "Sworn Affidavit",
                    Description = "Apply for sworn affidavits for legal purposes",
                    Price = 2000,
                    Icon = "⚖️",
                    IsAvailable = true,
                    Category = ServiceCategory.Affidavit
                },
                new JudiciaryService
                {
                    Id = "CF001",
                    Name = "Court Filing",
                    Description = "File court documents online",
                    Price = 5000,
                    Icon = "🏛️",
                    IsAvailable = true,
                    Category = ServiceCategory.CourtFiling
                },
                new JudiciaryService
                {
                    Id = "LC001",
                    Name = "Legal Consultation",
                    Description = "Schedule consultation with legal experts",
                    Price = 10000,
                    Icon = "👨‍⚖️",
                    IsAvailable = true,
                    Category = ServiceCategory.Consultation
                },
                new JudiciaryService
                {
                    Id = "CERT001",
                    Name = "Document Certification",
                    Description = "Certify legal documents",
                    Price = 3000,
                    Icon = "📜",
                    IsAvailable = true,
                    Category = ServiceCategory.Certification
                },
                new JudiciaryService
                {
                    Id = "MAR001",
                    Name = "Marriage Certificate",
                    Description = "Apply for marriage certificates",
                    Price = 15000,
                    Icon = "💒",
                    IsAvailable = true,
                    Category = ServiceCategory.Certification
                }
            };
        }
    }

    /// <summary>
    /// Utility class for dashboard operations
    /// </summary>
    public static class DashboardUtilities
    {
        public static string GenerateTransactionReference()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var random = new Random().Next(1000, 9999);
            return $"YJ-{timestamp}-{random}";
        }

        public static string FormatCurrency(decimal amount)
        {
            return $"₦{amount:N0}";
        }

        public static string GetRelativeTime(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            else if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minutes ago";
            else if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hours ago";
            else if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} days ago";
            else
                return dateTime.ToString("MMM dd, yyyy");
        }

        public static Color GetStatusColor(ApplicationStatus status)
        {
            switch (status)
            {
                case ApplicationStatus.Completed:
                    return Color.FromHex("#27AE60"); // Green
                case ApplicationStatus.Processing:
                case ApplicationStatus.UnderReview:
                    return Color.FromHex("#3498DB"); // Blue
                case ApplicationStatus.PaymentPending:
                case ApplicationStatus.Submitted:
                    return Color.FromHex("#F39C12"); // Orange
                case ApplicationStatus.Rejected:
                case ApplicationStatus.Cancelled:
                    return Color.FromHex("#E74C3C"); // Red
                default:
                    return Color.FromHex("#95A5A6"); // Gray
            }
        }

        public static string GetStatusIcon(ApplicationStatus status)
        {
            switch (status)
            {
                case ApplicationStatus.Completed:
                    return "✅";
                case ApplicationStatus.Processing:
                case ApplicationStatus.UnderReview:
                    return "🔄";
                case ApplicationStatus.PaymentPending:
                    return "💳";
                case ApplicationStatus.Submitted:
                    return "📋";
                case ApplicationStatus.Rejected:
                    return "❌";
                case ApplicationStatus.Cancelled:
                    return "🚫";
                default:
                    return "📄";
            }
        }

        public static async Task<bool> IsNetworkAvailable()
        {
            try
            {
                // Simple network check - you might want to use Connectivity plugin
                return true; // Placeholder - implement actual network check
            }
            catch
            {
                return false;
            }
        }

        public static void LogActivity(string activity, string details = "")
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] Dashboard Activity: {activity} - {details}");
            }
            catch
            {
                // Silent fail for logging
            }
        }
    }

    /// <summary>
    /// Constants for the application
    /// </summary>
    public static class DashboardConstants
    {
        public const string AppName = "Yobe Judiciary";
        public const string SupportPhoneNumber = "+2348012345678";
        public const string SupportEmail = "support@yobejudiciary.gov.ng";
        public const string WebsiteUrl = "https://yobejudiciary.gov.ng";

        // Animation durations
        public const uint FastAnimation = 200;
        public const uint MediumAnimation = 400;
        public const uint SlowAnimation = 800;

        // Colors (hex values)
        public const string PrimaryColorHex = "#2C3E50";
        public const string SecondaryColorHex = "#3498DB";
        public const string AccentColorHex = "#E74C3C";
        public const string SuccessColorHex = "#27AE60";
        public const string WarningColorHex = "#F39C12";
    }
}