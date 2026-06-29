using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.PancakeView;
using Xamarin.Forms.Xaml;
using static Android.Print.PrintAttributes;

namespace PAYYOBE.Views.Officer
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MDAInvoiceHistory : ContentPage
    {
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true });
        private List<OfficerTransaction> _cacheList = new List<OfficerTransaction>();
        private string _activeFilter = "all";

        public MDAInvoiceHistory()
        {
            InitializeComponent();
            EndDatePicker.Date = DateTime.Today;
            StartDatePicker.Date = DateTime.Today.AddDays(-30);
        }

        private async void OnSearchLogsTapped(object sender, EventArgs e)
        {
            await FetchLedgerLogsAsync();
        }

        private async Task FetchLedgerLogsAsync()
        {
            if (StartDatePicker.Date > EndDatePicker.Date)
            {
                await DisplayAlert("Operational Error", "Start execution threshold boundary out of sequence.", "OK");
                return;
            }

            LoadingOverlay.IsVisible = true;
            SummaryStrip.IsVisible = false;
            PillsRow.IsVisible = false;
            GroupedResultsPanel.IsVisible = false;
            EmptyState.IsVisible = false;

            try
            {
                string url = $"https://payyobe.com/api/v1/TransactionHistory/{MainPage.OfficerId}";
                var response = await _client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    LoadingOverlay.IsVisible = false;
                    EmptyState.IsVisible = true;
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OfficerApiResponse>(json);

                if (result == null || result.data == null || !result.data.Any())
                {
                    LoadingOverlay.IsVisible = false;
                    EmptyState.IsVisible = true;
                    return;
                }

                // Filter items sequentially using the chosen Date bounds
                _cacheList = result.data
                    .Where(x => x.date_Generated.Date >= StartDatePicker.Date && x.date_Generated.Date <= EndDatePicker.Date)
                    .OrderByDescending(x => x.date_Generated)
                    .ToList();

                if (!_cacheList.Any())
                {
                    LoadingOverlay.IsVisible = false;
                    EmptyState.IsVisible = true;
                    return;
                }

                LoadingOverlay.IsVisible = false;
                _activeFilter = "all";
                UpdatePillUI();
                ProcessAndRenderView();
            }
            catch
            {
                LoadingOverlay.IsVisible = false;
                EmptyState.IsVisible = true;
            }
        }

        private void ProcessAndRenderView()
        {
            GroupedResultsPanel.Children.Clear();

            var visibleList = _activeFilter == "all"
                ? _cacheList
                : _cacheList.Where(x => string.Equals(x.status, _activeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            // Compute Summary Visual Strip Content Metrics
            SumCount.Text = visibleList.Count.ToString();
            SumTotal.Text = $"₦{visibleList.Sum(x => x.amount):N0}";
            SumPending.Text = visibleList.Count(x => x.status == "Pending").ToString();

            SummaryStrip.IsVisible = true;
            PillsRow.IsVisible = true;

            if (!visibleList.Any())
            {
                EmptyState.IsVisible = true;
                return;
            }

            EmptyState.IsVisible = false;

            // Perform dynamic structural chronologically grouped generation
            var grouped = visibleList.GroupBy(x =>
            {
                return x.date_Generated.Date;
            });

            foreach (var group in grouped)
            {
                GroupedResultsPanel.Children.Add(new Label
                {
                    Text = group.Key == DateTime.Today ? "TODAY LOGS" : group.Key.ToString("dddd, MMMM dd, yyyy").ToUpper(),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromHex("#0F172A"),
                    Margin = new Thickness(4, 18, 0, 8),
                    CharacterSpacing = 1.2
                });

                foreach (var item in group)
                {
                    GroupedResultsPanel.Children.Add(GenerateAuditCardElement(item));
                }
            }

            GroupedResultsPanel.IsVisible = true;
        }

        private View GenerateAuditCardElement(OfficerTransaction item)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition { Width = new GridLength(4) },
            new ColumnDefinition { Width = GridLength.Star },
            new ColumnDefinition { Width = GridLength.Auto }
        }
            };

            // Fixed syntax instantiation for native Frame wrapper
            var wrapper = new Frame
            {
                BackgroundColor = Color.White,
                CornerRadius = 14,
                HasShadow = true,
                BorderColor = Color.FromHex("#CBD5E1"), // Native fallback controls border display implicitly
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10),
                Content = grid // Crucial: assigns the sub-elements to the frame control
            };

            grid.Children.Add(new BoxView { Color = item.status == "Successful" ? Color.FromHex("#06B6D4") : Color.FromHex("#C8941A"), VerticalOptions = LayoutOptions.Fill }, 0, 0);

            var midStack = new StackLayout { Spacing = 3, Padding = new Thickness(12, 10) };
            midStack.Children.Add(new Label { Text = item.service_Name, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#0F172A"), LineBreakMode = LineBreakMode.TailTruncation });
            midStack.Children.Add(new Label { Text = $"Payer Identity: {item.payer_Name} · Ref Code: {item.rrr}", FontSize = 10, TextColor = Color.FromHex("#64748B") });

            var metaBadgeStack = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
            metaBadgeStack.Children.Add(new Label { Text = item.date_Generated.ToString("hh:mm tt"), FontSize = 9, TextColor = Color.FromHex("#94A3B8"), VerticalOptions = LayoutOptions.Center });

            midStack.Children.Add(metaBadgeStack);
            grid.Children.Add(midStack, 1, 0);

            var endStack = new StackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End, Padding = new Thickness(0, 0, 12, 0) };
            endStack.Children.Add(new Label { Text = $"₦{item.amount:N0}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#0F172A") });

            grid.Children.Add(endStack, 2, 0);

            return wrapper;
        }

        private void PillAll_Tapped(object sender, EventArgs e) { _activeFilter = "all"; UpdatePillUI(); ProcessAndRenderView(); }
        private void PillPaid_Tapped(object sender, EventArgs e) { _activeFilter = "Successful"; UpdatePillUI(); ProcessAndRenderView(); }
        private void PillPending_Tapped(object sender, EventArgs e) { _activeFilter = "Pending"; UpdatePillUI(); ProcessAndRenderView(); }

        private void UpdatePillUI()
        {
            PillAll.BackgroundColor = _activeFilter == "all" ? Color.FromHex("#0F172A") : Color.White;
            PillAllLabel.TextColor = _activeFilter == "all" ? Color.White : Color.FromHex("#0F172A");

            PillPaid.BackgroundColor = _activeFilter == "Successful" ? Color.FromHex("#0F172A") : Color.White;
            PillPaidLabel.TextColor = _activeFilter == "Successful" ? Color.White : Color.FromHex("#0F172A");

            PillPending.BackgroundColor = _activeFilter == "Pending" ? Color.FromHex("#0F172A") : Color.White;
            PillPendingLabel.TextColor = _activeFilter == "Pending" ? Color.White : Color.FromHex("#0F172A");
        }
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