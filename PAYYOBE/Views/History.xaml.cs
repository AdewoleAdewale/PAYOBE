using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace PAYYOBE.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage
    {
        // ── Model ─────────────────────────────────────────────────────────────────
        class InvoiceItem
        {
            public int id { get; set; }
            public string mda { get; set; }
            public string payer_Name { get; set; }
            public string payer_Email { get; set; }
            public string payer_Phone { get; set; }
            public decimal amount { get; set; }
            public string serviceType_Id { get; set; }
            public string service_Name { get; set; }
            public string service_Type_Name { get; set; }
            public string rrr { get; set; }
            public string order_Id { get; set; }
            public string status { get; set; }
            public string superagent { get; set; }

            private string _dateGenerated = string.Empty;
            public string date_Generated
            {
                get { return _dateGenerated; }
                set { _dateGenerated = value ?? string.Empty; ParsedDate = ParseDate(value); }
            }

            public string date_Paid { get; set; }

            public DateTime ParsedDate { get; private set; }

            static DateTime ParseDate(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return DateTime.Now;
                string[] fmts =
                {
                    "yyyy-MM-ddTHH:mm:ss",
                    "yyyy-MM-ddTHH:mm:ss.fffffff",
                    "yyyy-MM-ddTHH:mm:ss.fff",
                    "MM/dd/yyyy HH:mm:ss",
                    "MM/dd/yyyy",
                    "dd/MM/yyyy",
                    "yyyy-MM-dd"
                };
                foreach (var f in fmts)
                    if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var r)) return r;
                return DateTime.TryParse(s, out var p) ? p : DateTime.Now;
            }

            public string FormattedAmount
            {
                get { return string.Format("₦{0:N0}", amount); }
            }

            public string FormattedTime
            {
                get { return ParsedDate.ToString("h:mm tt"); }
            }

            public string ShortRef
            {
                get
                {
                    if (!string.IsNullOrEmpty(rrr)) return rrr;
                    if (!string.IsNullOrEmpty(order_Id) && order_Id.Length > 8)
                        return order_Id.Substring(order_Id.Length - 8);
                    return order_Id ?? "N/A";
                }
            }

            // "paid" | "pending" | "failed"
            public string StatusNormalized
            {
                get
                {
                    var s = (status ?? string.Empty).Trim().ToLowerInvariant();
                    if (s == "paid" || s == "success") return "paid";
                    if (s == "pending") return "pending";
                    return "failed";
                }
            }

            public Color AccentColor
            {
                get
                {
                    var s = StatusNormalized;
                    if (s == "paid") return Color.FromHex("#1AAD5E");
                    if (s == "pending") return Color.FromHex("#D4AF6A");
                    return Color.FromHex("#E24B4A");
                }
            }

            public Color StatusBadgeBg
            {
                get
                {
                    var s = StatusNormalized;
                    if (s == "paid") return Color.FromHex("#EDF9F3");
                    if (s == "pending") return Color.FromHex("#F5E8CC");
                    return Color.FromHex("#FCEBEB");
                }
            }

            public Color StatusBadgeText
            {
                get
                {
                    var s = StatusNormalized;
                    if (s == "paid") return Color.FromHex("#006B35");
                    if (s == "pending") return Color.FromHex("#8A6520");
                    return Color.FromHex("#A32D2D");
                }
            }

            public Color StatusBorderColor
            {
                get
                {
                    var s = StatusNormalized;
                    if (s == "paid") return Color.FromHex("#C2F0D7");
                    if (s == "pending") return Color.FromHex("#E8D5A0");
                    return Color.FromHex("#F7C1C1");
                }
            }
        }

        // ── Fields ────────────────────────────────────────────────────────────────
        private readonly HttpClient _httpClient;
        private bool _isSearching = false;
        private List<InvoiceItem> _allItems = new List<InvoiceItem>();
        private string _activeFilter = "all";

        // ── Constructor ───────────────────────────────────────────────────────────
        public History()
        {
            InitializeComponent();
            EndDatePicker.Date = DateTime.Today;
            StartDatePicker.Date = DateTime.Today.AddDays(-30);

            _httpClient = BuildClient();
            ConfigureSSL();
        }

        private HttpClient BuildClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                             | System.Security.Authentication.SslProtocols.Tls11
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        private void ConfigureSSL()
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            ServicePointManager.ServerCertificateValidationCallback =
                new RemoteCertificateValidationCallback((s, c, ch, e) =>
                    e == SslPolicyErrors.None || true);
        }

        // ── Search ────────────────────────────────────────────────────────────────
        private async void SearchButton_Tapped(object sender, EventArgs e)
        {
            if (_isSearching) return;
            await PerformSearchAsync();
        }

        private async Task PerformSearchAsync()
        {
            if (StartDatePicker.Date > EndDatePicker.Date)
            {
                await DisplayAlert("Validation", "Start date cannot be after end date.", "OK");
                return;
            }

            _isSearching = true;
            ShowLoading();

            try
            {
                // TODO: replace with your logged-in user's email
                string email = MainPage.myemail;
                string from = StartDatePicker.Date.ToString("MM/dd/yyyy");
                string to = EndDatePicker.Date.ToString("MM/dd/yyyy");

                string url = "https://payyobe.com/api/v1/InvoiceHistory"
                           + "?Email=" + Uri.EscapeDataString(email);

                using (var response = await _httpClient.GetAsync(url))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        ShowError("Server error: " + (int)response.StatusCode + " " + response.ReasonPhrase);
                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var items = JsonConvert.DeserializeObject<List<InvoiceItem>>(json)
                                ?? new List<InvoiceItem>();

                    if (items.Count == 0) { ShowEmpty(); return; }

                    _allItems = items.OrderByDescending(t => t.ParsedDate).ToList();
                    _activeFilter = "all";
                    SetActivePill("all");
                    RenderResults();
                }
            }
            catch (TaskCanceledException)
            {
                ShowError("Request timed out. Check your connection.");
            }
            catch (Exception ex)
            {
                ShowError("Error: " + ex.Message);
                System.Diagnostics.Debug.WriteLine("[InvoiceHistory] " + ex);
            }
            finally
            {
                _isSearching = false;
                HideLoading();
            }
        }

        // ── Render ────────────────────────────────────────────────────────────────
        private void RenderResults()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                EmptyState.IsVisible = false;
                ErrorState.IsVisible = false;

                List<InvoiceItem> filtered;
                if (_activeFilter == "all")
                    filtered = _allItems;
                else
                    filtered = _allItems.Where(i => i.StatusNormalized == _activeFilter).ToList();

                // Summary
                int days = (EndDatePicker.Date - StartDatePicker.Date).Days + 1;
                decimal grandTotal = _allItems.Sum(i => i.amount);
                SumCount.Text = _allItems.Count.ToString();
                SumTotal.Text = grandTotal >= 1000
                    ? string.Format("{0:N1}k", grandTotal / 1000)
                    : string.Format("{0:N0}", grandTotal);
                SumDays.Text = days.ToString();
                SummaryStrip.IsVisible = true;

                UpdatePillLabels();
                PillsRow.IsVisible = true;

                // Group by date
                var groups = filtered
                    .GroupBy(t => t.ParsedDate.Date)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                GroupedResultsPanel.Children.Clear();

                foreach (var group in groups)
                {
                    decimal groupTotal = group.Sum(t => t.amount);
                    bool isToday = group.Key == DateTime.Today;
                    bool isYesterday = group.Key == DateTime.Today.AddDays(-1);

                    string dateLabel;
                    if (isToday)
                        dateLabel = "Today";
                    else if (isYesterday)
                        dateLabel = "Yesterday";
                    else
                        dateLabel = group.Key.ToString("dddd, MMMM d");

                    GroupedResultsPanel.Children.Add(BuildGroupHeader(dateLabel, groupTotal));

                    foreach (var item in group)
                        GroupedResultsPanel.Children.Add(BuildTransactionCard(item));
                }

                GroupedResultsPanel.IsVisible = true;
            });
        }

        // ── Group Header ──────────────────────────────────────────────────────────
        private View BuildGroupHeader(string dateLabel, decimal total)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Margin = new Thickness(4, 20, 4, 10)
            };

            grid.Children.Add(new Label
            {
                Text = dateLabel,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromHex("#004D27"),
                CharacterSpacing = 0.5,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);

            var badge = new Xamarin.Forms.PancakeView.PancakeView
            {
                BackgroundColor = Color.FromHex("#EDF9F3"),
                BorderColor = Color.FromHex("#C2F0D7"),
                BorderThickness = 1,
                Padding = new Thickness(10, 3),
                Content = new Label
                {
                    Text = string.Format("₦{0:N0}", total),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromHex("#004D27")
                }
            };
            grid.Children.Add(badge, 1, 0);

            return grid;
        }

        // ── Transaction Card ──────────────────────────────────────────────────────
        private View BuildTransactionCard(InvoiceItem item)
        {
            var card = new Xamarin.Forms.PancakeView.PancakeView
            {
                BackgroundColor = Color.White,
                HasShadow = true,
                Elevation = 3,
                BorderThickness = 1,
                BorderColor = Color.FromHex("#E8F5ED"),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var outerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(4) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            // Colored left accent bar
            outerGrid.Children.Add(new BoxView
            {
                Color = item.AccentColor,
                CornerRadius = 16,
                VerticalOptions = LayoutOptions.Fill
            }, 0, 0);

            // Details
            var details = new StackLayout
            {
                Spacing = 3,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(14, 13, 8, 13)
            };

            details.Children.Add(new Label
            {
                Text = item.service_Name ?? "N/A",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromHex("#0D2416"),
                LineBreakMode = LineBreakMode.TailTruncation
            });

            details.Children.Add(new Label
            {
                Text = item.mda ?? "—",
                FontSize = 11,
                TextColor = Color.FromHex("#7A9985"),
                LineBreakMode = LineBreakMode.TailTruncation
            });

            // Footer row
            var footer = new StackLayout
            {
                Orientation = StackOrientation.Horizontal,
                Spacing = 8
            };

            footer.Children.Add(new Xamarin.Forms.PancakeView.PancakeView
            {
                BackgroundColor = Color.FromHex("#F4FAF6"),
                BorderThickness = 0,
                Padding = new Thickness(6, 2),
                Content = new Label
                {
                    Text = item.ShortRef,
                    FontSize = 9,
                    TextColor = Color.FromHex("#7A9985")
                }
            });

            footer.Children.Add(new Label
            {
                Text = item.FormattedTime,
                FontSize = 9,
                TextColor = Color.FromHex("#7A9985"),
                VerticalOptions = LayoutOptions.Center
            });

            footer.Children.Add(new Xamarin.Forms.PancakeView.PancakeView
            {
                BackgroundColor = item.StatusBadgeBg,
                BorderColor = item.StatusBorderColor,
                BorderThickness = 1,
                Padding = new Thickness(7, 2),
                Content = new Label
                {
                    Text = (item.status ?? "—").ToUpper(),
                    FontSize = 9,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = item.StatusBadgeText,
                    CharacterSpacing = 0.5
                }
            });

            details.Children.Add(footer);
            outerGrid.Children.Add(details, 1, 0);

            // Amount badge
            var amtBadge = new Xamarin.Forms.PancakeView.PancakeView
            {
                BackgroundColor = Color.FromHex("#EDF9F3"),
                BorderThickness = 0,
                Padding = new Thickness(10, 5),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 12, 0),
                Content = new Label
                {
                    Text = item.FormattedAmount,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromHex("#004D27"),
                    HorizontalOptions = LayoutOptions.Center
                }
            };
            outerGrid.Children.Add(amtBadge, 2, 0);

            card.Content = outerGrid;
            return card;
        }

        // ── Filter Pills ──────────────────────────────────────────────────────────
        private void PillAll_Tapped(object sender, EventArgs e) { ApplyFilter("all"); }
        private void PillPaid_Tapped(object sender, EventArgs e) { ApplyFilter("paid"); }
        private void PillPending_Tapped(object sender, EventArgs e) { ApplyFilter("pending"); }
        private void PillFailed_Tapped(object sender, EventArgs e) { ApplyFilter("failed"); }

        private void ApplyFilter(string status)
        {
            _activeFilter = status;
            SetActivePill(status);
            RenderResults();
        }

        private void SetActivePill(string status)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                var activeColor = Color.FromHex("#003D1F");
                var inactiveBg = Color.White;
                var inactiveBorder = Color.FromHex("#D0E8D8");
                var activeText = Color.White;
                var inactiveText = Color.FromHex("#3D5C48");

                StylePill(PillAll, PillAllLabel, "all", status, activeColor, inactiveBg, inactiveBorder, activeText, inactiveText);
                StylePill(PillPaid, PillPaidLabel, "paid", status, activeColor, inactiveBg, inactiveBorder, activeText, inactiveText);
                StylePill(PillPending, PillPendingLabel, "pending", status, activeColor, inactiveBg, inactiveBorder, activeText, inactiveText);
                StylePill(PillFailed, PillFailedLabel, "failed", status, activeColor, inactiveBg, inactiveBorder, activeText, inactiveText);
            });
        }

        private void StylePill(
            Xamarin.Forms.PancakeView.PancakeView pill, Label label, string key, string active,
            Color activeBg, Color inactiveBg, Color inactiveBorder,
            Color activeText, Color inactiveText)
        {
            bool isActive = key == active;
            pill.BackgroundColor = isActive ? activeBg : inactiveBg;
            pill.BorderColor = isActive ? activeBg : inactiveBorder;
            label.TextColor = isActive ? activeText : inactiveText;
            label.FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None;
        }

        private void UpdatePillLabels()
        {
            int paid = _allItems.Count(i => i.StatusNormalized == "paid");
            int pending = _allItems.Count(i => i.StatusNormalized == "pending");
            int failed = _allItems.Count(i => i.StatusNormalized == "failed");

            PillAllLabel.Text = "All  " + _allItems.Count;
            PillPaidLabel.Text = "Paid  " + paid;
            PillPendingLabel.Text = "Pending  " + pending;
            PillFailedLabel.Text = "Failed  " + failed;
        }

        // ── State Helpers ─────────────────────────────────────────────────────────
        private void ShowLoading()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                LoadingOverlay.IsVisible = true;
                SummaryStrip.IsVisible = false;
                PillsRow.IsVisible = false;
                GroupedResultsPanel.IsVisible = false;
                EmptyState.IsVisible = false;
                ErrorState.IsVisible = false;
            });
        }

        private void HideLoading()
        {
            Device.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
        }

        private void ShowEmpty()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                EmptyState.IsVisible = true;
                GroupedResultsPanel.IsVisible = false;
                ErrorState.IsVisible = false;
                SummaryStrip.IsVisible = false;
                PillsRow.IsVisible = false;
            });
        }

        private void ShowError(string msg)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                ErrorLabel.Text = msg;
                ErrorState.IsVisible = true;
                GroupedResultsPanel.IsVisible = false;
                EmptyState.IsVisible = false;
                SummaryStrip.IsVisible = false;
                PillsRow.IsVisible = false;
            });
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(() =>
                Application.Current.MainPage = new NavigationPage(new Dashboard()));
            return true;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _httpClient?.CancelPendingRequests();
        }
    }
}