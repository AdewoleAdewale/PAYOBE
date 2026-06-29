using Acr.UserDialogs;
using Android.Content.Res;
using Newtonsoft.Json;
using PAYYOBE.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace PAYYOBE.Views.Officer
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Dashboard : ContentPage
    {
        // ─────────────────────────────────────────────────────────────────
        //  API BASE
        // ─────────────────────────────────────────────────────────────────
        private const string BaseUrl = "https://payyobe.com/api/v1";

        private CancellationTokenSource _cts;
        private bool _isBusy = false;
        private readonly BluetoothPrinterService _printer =
          new BluetoothPrinterService(use80mm: false);
        // Reuse a single HttpClient so the SSL handler is created once
        private static readonly HttpClient _http = new HttpClient(
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            })
        { Timeout = TimeSpan.FromSeconds(30) };

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

        protected override void OnAppearing()
        {
            base.OnAppearing();
            PopulateStaticFields(); 
            _ = FetchRecentActivityAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cts?.Cancel();
        }

        private void PopulateStaticFields()
        {
            try
            {
                OfficerNameLabel.Text = $"Officer: {MainPage.OfficerName ?? "User"}";
                OfficerCodeLabel.Text = $"Code: {MainPage.OfficerCode ?? "N/A"}";
                WelcomeLabel.Text = GetGreeting();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfficerDashboard] Populate Static Fields Exception: {ex.Message}");
            }
        }

        private static string GetGreeting()
        {
            int hour = DateTime.Now.Hour;
            if (hour < 12) return "System Operational 🌅";
            if (hour < 17) return "System Operational ☀️";
            return "System Operational 🌙";
        }

        private void SetTxnState(bool loading, bool empty, bool error, bool list)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                TxnLoadingCard.IsVisible = loading;
                TxnEmptyCard.IsVisible = empty;
            });
        }

      

        private static string FormatAmount(double amount)
        {
            if (amount >= 1_000_000) return $"₦{amount / 1_000_000:F1}M";
            if (amount >= 1_000) return $"₦{amount / 1_000:F1}K";
            return $"₦{amount:N0}";
        }

  

        private async void OnNewInvoiceTapped(object sender, EventArgs e) => await Navigation.PushAsync(new Views.Officer.MDAInvoice());
        private async void OnTaxReportTapped(object sender, EventArgs e) => await Navigation.PushAsync(new Views.History());
        private async void OnSettingsTapped(object sender, EventArgs e) => await CallPrinterAsync();
        private async void OnTxnRetryTapped(object sender, EventArgs e) => await FetchRecentActivityAsync();
        private async void OnDirectPaymentTapped(object sender, EventArgs e) => await Navigation.PushAsync(new Views.Officer.MDAPayment());

        private async void OnHelpTapped(object sender, EventArgs e)
        {
            await DisplayAlert("YIRS Helpdesk", "Yobe State Integrated Revenue Service Officer Support Line:\n\n📞 +234-803-052-3208", "OK");
        }

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

        private async Task FetchRecentActivityAsync()
        {
            try
            {
                TxnLoadingCard.IsVisible = true;
                GroupedRecentPanel.Children.Clear();

                string url = $"https://payyobe.com/api/v1/TransactionHistory/{MainPage.OfficerId}";
                var response = await _http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    TxnLoadingCard.IsVisible = false;
                    TxnEmptyCard.IsVisible = true;
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OfficerApiResponse>(json);

                if (result == null || result.data == null || !result.data.Any())
                {
                    TxnLoadingCard.IsVisible = false;
                    TxnEmptyCard.IsVisible = true;
                    return;
                }

                TxnLoadingCard.IsVisible = false;
                TxnEmptyCard.IsVisible = false;

                // Bind Summary Performance KPIs
                StatInvoices.Text = result.data.Count.ToString();
                StatTotalAmount.Text = $"₦{result.data.Sum(x => x.amount):N0}";
                StatPending.Text = result.data.Count(x => x.status == "Pending").ToString();

                // Group the top 10 rows chronologically by date
                var top10Grouped = result.data
                    .OrderByDescending(x => x.date_Generated)
                    .Take(10)
                    .GroupBy(x => x.date_Generated.Date);

                foreach (var group in top10Grouped)
                {
                    // Render Section Title Header Card
                    GroupedRecentPanel.Children.Add(new Label
                    {
                        Text = group.Key == DateTime.Today ? "TODAY" : group.Key == DateTime.Today.AddDays(-1) ? "YESTERDAY" : group.Key.ToString("MMMM dd, yyyy"),
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromHex("#64748B"),
                        Margin = new Thickness(4, 12, 0, 6),
                        CharacterSpacing = 1.5
                    });

                    foreach (var tx in group)
                    {
                        GroupedRecentPanel.Children.Add(BuildTransactionRowItem(tx));
                    }
                }
            }
            catch
            {
                TxnLoadingCard.IsVisible = false;
                TxnEmptyCard.IsVisible = true;
            }
        }

        private View BuildTransactionRowItem(OfficerTransaction tx)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = new GridLength(5) }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } }, Margin = new Thickness(0, 0, 0, 10) };

            var card = new Xamarin.Forms.PancakeView.PancakeView { BackgroundColor = Color.White, CornerRadius = 12, Padding = new Thickness(12, 14), HasShadow = true, Content = grid };

            grid.Children.Add(new BoxView { Color = tx.status == "Pending" ? Color.FromHex("#C8941A") : Color.FromHex("#06B6D4"), VerticalOptions = LayoutOptions.Fill }, 0, 0);

            var details = new StackLayout { Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
            details.Children.Add(new Label { Text = tx.service_Name, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#0F172A"), LineBreakMode = LineBreakMode.TailTruncation });
            details.Children.Add(new Label { Text = $"Payer: {tx.payer_Name} · Ref: {tx.rrr}", FontSize = 10, TextColor = Color.FromHex("#64748B") });

            grid.Children.Add(details, 1, 0);

            var trailingStack = new StackLayout { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, Spacing = 2 };
            trailingStack.Children.Add(new Label { Text = $"₦{tx.amount:N0}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#0F172A") });
            trailingStack.Children.Add(new Label { Text = tx.status.ToUpper(), FontSize = 8, FontAttributes = FontAttributes.Bold, TextColor = tx.status == "Pending" ? Color.FromHex("#C8941A") : Color.FromHex("#06B6D4"), HorizontalOptions = LayoutOptions.End });

            grid.Children.Add(trailingStack, 2, 0);
            return card;
        }

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            OfficerSessionManager.ClearSession();
            Application.Current.MainPage = new NavigationPage(new MainPage());
        }

        private async void OnInvoiceHistoryTapped(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new MDAInvoiceHistory());
        }

        public class OfficerApiResponse
        {
            public string statusCode { get; set; }
            public string message { get; set; }
            public List<OfficerTransaction> data { get; set; }
        }

        public class OfficerTransaction
        {
            public int id { get; set; }
            public string rrr { get; set; }
            public string payer_Name { get; set; }
            public string service_Name { get; set; }
            public double amount { get; set; }
            public string status { get; set; }
            public DateTime date_Generated { get; set; }
            public DateTime date_Paid { get; set; }
        }
    }
}
