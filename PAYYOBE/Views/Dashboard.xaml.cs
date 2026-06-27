using Acr.UserDialogs;
using Newtonsoft.Json;
using PAYYOBE.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace PAYYOBE.Views
{
    public partial class Dashboard : ContentPage
    {
        private const string BaseUrl = "https://payyobe.com/api/v1";

        private CancellationTokenSource _cts;
        private bool _isBusy = false;
        private readonly BluetoothPrinterService _printer =
            new BluetoothPrinterService(use80mm: false);

        private static readonly HttpClient _http = new HttpClient(
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            })
        { Timeout = TimeSpan.FromSeconds(30) };

        // ─────────────────────────────────────────────────────────────────
        //  FIRST-LOGIN PASSWORD CHANGE TRACKING
        // ─────────────────────────────────────────────────────────────────

        private static string PasswordChangedKey(string email)
            => $"PasswordChanged_{email?.ToLowerInvariant()?.Trim()}";

        public static bool HasChangedPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return true;
            return Preferences.Get(PasswordChangedKey(email), false);
        }

        public static void MarkPasswordChanged(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            Preferences.Set(PasswordChangedKey(email), true);
        }

        // ─────────────────────────────────────────────────────────────────
        //  CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────

        public Dashboard()
        {
            InitializeComponent();
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;
        }

        // ─────────────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────────────

        protected override void OnAppearing()
        {
            base.OnAppearing();
            PopulateStaticFields();
            _ = LoadTransactionsAsync();
            _ = CheckFirstLoginPasswordPromptAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cts?.Cancel();
        }

        // ─────────────────────────────────────────────────────────────────
        //  FIRST-LOGIN PASSWORD PROMPT
        // ─────────────────────────────────────────────────────────────────

        private async Task CheckFirstLoginPasswordPromptAsync()
        {
            try
            {
                string email = MainPage.myemail;
                if (string.IsNullOrWhiteSpace(email)) return;
                if (HasChangedPassword(email)) return;

                await Task.Delay(800);

                bool change = await DisplayAlert(
                    "🔐 Security Reminder",
                    "For your account security, please change your default password before continuing.",
                    "Change Now", "Later");

                if (change)
                    await Navigation.PushAsync(new Views.Password());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] FirstLoginPrompt: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  POPULATE FROM LOGIN RESPONSE
        // ─────────────────────────────────────────────────────────────────

        private void PopulateStaticFields()
        {
            try
            {
                AgentNameLabel.Text = MainPage.MyfullName ?? "—";
                MdaLabel.Text = TruncateMda(MainPage.mymda ?? "—", 30);
                WelcomeLabel.Text = GetGreeting();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] PopulateStaticFields: {ex.Message}");
            }
        }

        private static string TruncateMda(string text, int max)
            => text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";

        private static string GetGreeting()
        {
            int h = DateTime.Now.Hour;
            if (h < 12) return "Good morning 🌅";
            if (h < 17) return "Good afternoon ☀️";
            return "Good evening 🌙";
        }

        // ─────────────────────────────────────────────────────────────────
        //  TRANSACTION LOAD
        //
        //  FIXES:
        //  1. Cutoff widened from 1 day to 30 days — 24-hour window was
        //     too narrow; agents rarely generate invoices every single day.
        //  2. ListView HeightRequest is set dynamically after binding so
        //     that the ListView inside a ScrollView renders correctly on
        //     Android (a ListView in a ScrollView needs an explicit height).
        //  3. Null-safe date parsing with JsonSerializerSettings so that
        //     malformed dates don't crash deserialization.
        // ─────────────────────────────────────────────────────────────────

        private async Task LoadTransactionsAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            SetTxnState(loading: true, empty: false, error: false, list: false);

            try
            {
                string email = MainPage.myemail?.Trim();
                if (string.IsNullOrEmpty(email))
                {
                    SetTxnError("User email not found. Please log in again.");
                    return;
                }

                string url = $"{BaseUrl}/InvoiceHistory?Email={Uri.EscapeDataString(email)}";

                _cts.Token.ThrowIfCancellationRequested();

                var response = await _http.GetAsync(url, _cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    SetTxnError($"Server returned {(int)response.StatusCode}. Tap Retry.");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"[Dashboard] InvoiceHistory raw JSON: {json?.Substring(0, Math.Min(json.Length, 300))}");

                if (string.IsNullOrWhiteSpace(json))
                {
                    SetTxnError("Empty response from server.");
                    return;
                }

                // ✅ FIX: Use relaxed date handling so badly formatted dates don't throw
                var settings = new JsonSerializerSettings
                {
                    DateFormatHandling = DateFormatHandling.IsoDateFormat,
                    NullValueHandling = NullValueHandling.Ignore,
                    Error = (sender, args) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Dashboard] JSON error: {args.ErrorContext.Error.Message}");
                        args.ErrorContext.Handled = true;
                    }
                };

                var all = JsonConvert.DeserializeObject<List<InvoiceRecord>>(json, settings);

                if (all == null)
                {
                    SetTxnError("Could not parse server response.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Dashboard] Loaded {all.Count} total invoices.");

                // ✅ FIX: Widened from 1 day to 30 days so recent invoices actually appear
                var cutoff = DateTime.UtcNow.AddDays(-30);

                var recent = all
                    .Where(i => i.date_Generated >= cutoff)
                    .OrderByDescending(i => i.date_Generated)
                    .Take(5)
                    .ToList();

                // If nothing in 30 days, just take the 5 most recent regardless of date
                if (recent.Count == 0 && all.Count > 0)
                {
                    recent = all
                        .OrderByDescending(i => i.date_Generated)
                        .Take(5)
                        .ToList();
                }

                int totalCount = all.Count;
                double totalAmount = all.Sum(i => i.amount);
                int pendingCount = all.Count(i =>
                    string.Equals(i.status, "Pending", StringComparison.OrdinalIgnoreCase));

                Device.BeginInvokeOnMainThread(() =>
                {
                    StatInvoices.Text = totalCount.ToString();
                    StatTotalAmount.Text = FormatAmount(totalAmount);
                    StatPending.Text = pendingCount.ToString();

                    if (recent.Count == 0)
                    {
                        SetTxnState(loading: false, empty: true, error: false, list: false);
                    }
                    else
                    {
                        // ✅ FIX: Set HeightRequest so ListView renders inside ScrollView on Android.
                        // Each card is approximately 90dp tall; add padding.
                        TransactionListView.HeightRequest = recent.Count * 95;

                        TransactionListView.ItemsSource = null; // force refresh
                        TransactionListView.ItemsSource = recent;
                        SetTxnState(loading: false, empty: false, error: false, list: true);

                        System.Diagnostics.Debug.WriteLine($"[Dashboard] Showing {recent.Count} recent invoices.");
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // silently cancelled
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] Network: {ex.Message}");
                SetTxnError("No internet connection. Tap Retry.");
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] JSON: {ex.Message}");
                SetTxnError("Invalid data from server.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] LoadTxn: {ex.Message}");
                SetTxnError("Unexpected error. Tap Retry.");
            }
            finally
            {
                _isBusy = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  TXN STATE HELPERS
        // ─────────────────────────────────────────────────────────────────

        private void SetTxnState(bool loading, bool empty, bool error, bool list)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                TxnLoadingCard.IsVisible = loading;
                TxnEmptyCard.IsVisible = empty;
                TxnErrorCard.IsVisible = error;
                TransactionListView.IsVisible = list;
            });
        }

        private void SetTxnError(string message)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                TxnErrorLabel.Text = message;
                SetTxnState(loading: false, empty: false, error: true, list: false);
            });
        }

        // ─────────────────────────────────────────────────────────────────
        //  FORMATTING
        // ─────────────────────────────────────────────────────────────────

        private static string FormatAmount(double amount)
        {
            if (amount >= 1_000_000) return $"₦{amount / 1_000_000:F1}M";
            if (amount >= 1_000) return $"₦{amount / 1_000:F1}K";
            return $"₦{amount:N0}";
        }

        // ─────────────────────────────────────────────────────────────────
        //  LOADING OVERLAY
        // ─────────────────────────────────────────────────────────────────

        private async Task ShowOverlay(string message)
        {
            PageLoadingText.Text = message;
            PageLoadingOverlay.IsVisible = true;
            await PageLoadingOverlay.FadeTo(1, 200);
        }

        private async Task HideOverlay()
        {
            await PageLoadingOverlay.FadeTo(0, 200);
            PageLoadingOverlay.IsVisible = false;
        }

        // ─────────────────────────────────────────────────────────────────
        //  TAP HANDLERS
        // ─────────────────────────────────────────────────────────────────

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            try
            {
                bool confirm = await DisplayAlert("Sign Out", "Are you sure you want to sign out?", "Sign Out", "Cancel");
                if (!confirm) return;

                MainPage.MyfullName = null;
                MainPage.myemail = null;
                MainPage.mymda = null;
                MainPage.mycourt = null;

                Application.Current.MainPage = new NavigationPage(new MainPage());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] Logout: {ex.Message}");
            }
        }

        private async void OnNewInvoiceTapped(object sender, EventArgs e)
        {
            try
            {
                await AnimateTap(sender as View);
                await Navigation.PushAsync(new Views.Generate());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] NewInvoice: {ex.Message}");
            }
        }

        private async void OnDirectPaymentTapped(object sender, EventArgs e)
        {
            await AnimateTap(sender as View);
        }

        private async void OnInvoiceHistoryTapped(object sender, EventArgs e)
        {
            await AnimateTap(sender as View);
            await Navigation.PushAsync(new Views.History());
        }

        private async void OnTaxReportTapped(object sender, EventArgs e)
        {
            await AnimateTap(sender as View);
            await Navigation.PushAsync(new Views.History());
        }

        private async void OnSettingsTapped(object sender, EventArgs e)
        {
            await AnimateTap(sender as View);
            await Navigation.PushAsync(new Views.Password());
        }

        private async void OnVerifyPaymentTapped(object sender, EventArgs e)
        {
            await AnimateTap(sender as View);
            await Navigation.PushAsync(new Views.VerifyInvoice());
        }

        private async void OnTxnRetryTapped(object sender, EventArgs e)
        {
            await LoadTransactionsAsync();
        }

        private async void OnHelpTapped(object sender, EventArgs e)
        {
            try
            {
                await DisplayAlert("YIRS Helpdesk",
                    "Yobe State Integrated Revenue Service:\n\n" +
                    "📞 Phone: +234-803-052-3208\n" +
                    "📧 Email: support@payyobe.osoftpay.net\n" +
                    "🕐 Hours: Mon–Fri  8 AM – 5 PM",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] Help: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  MICRO-ANIMATION
        // ─────────────────────────────────────────────────────────────────

        private static async Task AnimateTap(View view)
        {
            if (view == null) return;
            try
            {
                await view.ScaleTo(0.95, 80, Easing.CubicIn);
                await view.ScaleTo(1.00, 80, Easing.CubicOut);
            }
            catch { }
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
            => await CallPrinterAsync();

        private async Task CallPrinterAsync()
        {
            try
            {
                using (UserDialogs.Instance.Loading("Connecting to Printer…"))
                {
                    var pt = _printer.PrintTestPageAsync();
                    var tmo = Task.Delay(30000);
                    if (await Task.WhenAny(pt, tmo) == tmo)
                    {
                        await DisplayAlert("Printer Error", "Print timed out.", "OK");
                        return;
                    }
                    await pt;
                }
            }
            catch (PrinterException px) { await DisplayAlert("Printer Error", px.Message, "OK"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Dashboard] Printer: {ex.Message}"); }
        }
    }


    public class InvoiceRecord
    {
        [JsonProperty("id")]
        public int id { get; set; }

        [JsonProperty("mda")]
        public string mda { get; set; }

        // ✅ API sends "payerName" → maps to payer_Name used in XAML bindings
        [JsonProperty("payerName")]
        public string payer_Name { get; set; }

        [JsonProperty("payerEmail")]
        public string payer_Email { get; set; }

        [JsonProperty("payerPhone")]
        public string payer_Phone { get; set; }

        [JsonProperty("amount")]
        public double amount { get; set; }

        // ✅ API sends "serviceName" → maps to service_Name used in XAML bindings
        [JsonProperty("serviceName")]
        public string service_Name { get; set; }

        [JsonProperty("serviceTypeName")]
        public string service_Type_Name { get; set; }

        [JsonProperty("rrr")]
        public string rrr { get; set; }

        [JsonProperty("orderId")]
        public string order_Id { get; set; }

        [JsonProperty("status")]
        public string status { get; set; }

        // ✅ API sends "dateGenerated" → maps to date_Generated used in XAML bindings
        [JsonProperty("dateGenerated")]
        public DateTime date_Generated { get; set; }

        [JsonProperty("datePaid")]
        public DateTime? date_Paid { get; set; }

        [JsonProperty("superagent")]
        public string superagent { get; set; }

        [JsonProperty("description")]
        public string description { get; set; }
    }


}