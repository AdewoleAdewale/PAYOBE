using Acr.UserDialogs;
using Newtonsoft.Json;
using PAYYOBE.Services;
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

namespace PAYYOBE.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class VerifyInvoice : ContentPage
    {
        private HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isVerifying = false;
        private List<string> _recentSearches = new List<string>();
        private const int MAX_RECENT_SEARCHES = 5;
        private const int MIN_REFERENCE_LENGTH = 5;
        private const int MAX_REFERENCE_LENGTH = 50;
        private const int REQUEST_TIMEOUT_SECONDS = 45;

        // ── Print guard ───────────────────────────────────────────────────────
        private bool _isPrinting = false;

        // ── Response model ────────────────────────────────────────────────────
        internal class VerifyResponse
        {
            public int id { get; set; }
            public string mda { get; set; }
            public string payer_Name { get; set; }
            public string payer_Email { get; set; }
            public string payer_Phone { get; set; }
            public decimal amount { get; set; }
            public string service_Name { get; set; }
            public string service_Type_Name { get; set; }
            public string rrr { get; set; }
            public string order_Id { get; set; }
            public string status { get; set; }
            public DateTime date_Generated { get; set; }
            public DateTime? date_Paid { get; set; }
            public string superagent { get; set; }
            public string description { get; set; }

            public bool IsValid() =>
                !string.IsNullOrWhiteSpace(status) &&
                !string.IsNullOrWhiteSpace(payer_Name);
        }

        private VerifyResponse _currentResult;
        private string _lastSearchReference;

        public VerifyInvoice()
        {
            InitializeComponent();
            InitializeHttpClient();
            LoadRecentSearches();
            ShowEmptyState();
            CheckConnectivity();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  INIT
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeHttpClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };
            _httpClient = new HttpClient(handler)
            { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "payyobe/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private async void CheckConnectivity()
        {
            try
            {
                UpdateConnectionStatus(Connectivity.NetworkAccess == NetworkAccess.Internet);
                Connectivity.ConnectivityChanged += OnConnectivityChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VerifyInvoice] Connectivity: {ex.Message}");
            }
        }

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
            => Device.BeginInvokeOnMainThread(() =>
                UpdateConnectionStatus(e.NetworkAccess == NetworkAccess.Internet));

        private void UpdateConnectionStatus(bool isConnected = true)
        {
            try
            {
                connectionStatus.IsVisible = true;
                if (isConnected)
                {
                    connectionStatus.BackgroundColor = Color.FromHex("#e8f5e9");
                    connectionStatusLabel.Text = "● Online";
                    connectionStatusLabel.TextColor = Color.FromHex("#2e7d32");
                }
                else
                {
                    connectionStatus.BackgroundColor = Color.FromHex("#ffebee");
                    connectionStatusLabel.Text = "● Offline";
                    connectionStatusLabel.TextColor = Color.FromHex("#d32f2f");
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  RECENT SEARCHES
        // ─────────────────────────────────────────────────────────────────────

        private void LoadRecentSearches()
        {
            try
            {
                var json = Preferences.Get("RecentSearches", "[]");
                _recentSearches = JsonConvert.DeserializeObject<List<string>>(json)
                                  ?? new List<string>();
                UpdateRecentSearchesUI();
            }
            catch { _recentSearches = new List<string>(); }
        }

        private void SaveRecentSearches()
        {
            try { Preferences.Set("RecentSearches", JsonConvert.SerializeObject(_recentSearches)); }
            catch { }
        }

        private void AddToRecentSearches(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;
            _recentSearches.RemoveAll(x =>
                x.Equals(reference, StringComparison.OrdinalIgnoreCase));
            _recentSearches.Insert(0, reference);
            if (_recentSearches.Count > MAX_RECENT_SEARCHES)
                _recentSearches = _recentSearches.Take(MAX_RECENT_SEARCHES).ToList();
            SaveRecentSearches();
            UpdateRecentSearchesUI();
        }

        private void UpdateRecentSearchesUI()
        {
            try
            {
                recentSearchesContainer.IsVisible = _recentSearches.Any();
                recentSearchesCollection.ItemsSource = _recentSearches;
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  DISPLAY STATES
        // ─────────────────────────────────────────────────────────────────────

        private void ShowEmptyState()
        {
            emptyState.IsVisible = true;
            demandnotice.IsVisible = false;
            resultsHeaderContainer.IsVisible = false;
            printReceiptButton.IsVisible = false;
            HideAllMessages();
        }

        private void DisplayVerificationResult(VerifyResponse r, string reference)
        {
            try
            {
                emptyState.IsVisible = false;
                demandnotice.IsVisible = true;
                resultsHeaderContainer.IsVisible = true;
                recentSearchesContainer.IsVisible = false;

                name.Text = Safe(r.payer_Name, "Unknown Payer").ToUpper();
                DOP.Text = r.date_Paid.HasValue
                                          ? r.date_Paid.Value.ToString("MMM dd, yyyy")
                                          : r.date_Generated.ToString("MMM dd, yyyy");
                agent.Text = Safe(r.superagent, "N/A").ToUpper();
                agentlga.Text = Safe(r.mda, "N/A").ToUpper();
                viewpayemtitems.Text = Safe(r.service_Name, "No Items Listed");
                paidamounts.Text = $"₦ {r.amount:N2}";
                referenceNumber.Text = Safe(reference, Safe(r.rrr, "N/A"));

                string statusText = Safe(r.status, "Pending").ToUpper();
                verifymessage.Text = statusText;
                verificationTimestamp.Text = DateTime.Now.ToString("MMM dd, yyyy 'at' hh:mm tt");

                bool isPaid = string.Equals(
                    r.status, "Successful", StringComparison.OrdinalIgnoreCase);

                if (isPaid)
                {
                    statusBadge.BackgroundColor = Color.FromHex("#e8f5e9");
                    statusBadgeText.Text = "✅ VERIFIED PAYMENT";
                    statusBadgeText.TextColor = Color.FromHex("#2e7d32");
                    statusContainer.BackgroundGradientStartColor = Color.ForestGreen;
                    statusContainer.BackgroundGradientEndColor = Color.FromHex("#004225");
                    statusIcon.Text = "✅";
                }
                else
                {
                    statusBadge.BackgroundColor = Color.FromHex("#fff3e0");
                    statusBadgeText.Text = $"⏳ {statusText}";
                    statusBadgeText.TextColor = Color.FromHex("#e65100");
                    statusContainer.BackgroundGradientStartColor = Color.FromHex("#e65100");
                    statusContainer.BackgroundGradientEndColor = Color.FromHex("#bf360c");
                    statusIcon.Text = "⏳";
                }

                // Show print button after any successful lookup
                printReceiptButton.IsVisible = true;
                printReceiptButton.IsEnabled = true;
                printReceiptButton.Text = "🖨️ Print Verification Receipt";

                HideAllMessages();
                ShowSuccessMessage($"Invoice found — Status: {r.status}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VerifyInvoice] DisplayResult: {ex.Message}");
                ShowError("Error displaying results. Please try again.", true);
            }
        }

        private static string Safe(string value, string fallback = "N/A")
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        // ─────────────────────────────────────────────────────────────────────
        //  VALIDATION
        // ─────────────────────────────────────────────────────────────────────

        private bool ValidateReferenceNumber(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            { ShowError("Please enter a payment reference number", false); return false; }
            reference = reference.Trim();
            if (reference.Length < MIN_REFERENCE_LENGTH)
            { ShowError($"Reference must be at least {MIN_REFERENCE_LENGTH} characters", false); return false; }
            if (reference.Length > MAX_REFERENCE_LENGTH)
            { ShowError($"Reference cannot exceed {MAX_REFERENCE_LENGTH} characters", false); return false; }
            if (!Regex.IsMatch(reference, @"^[a-zA-Z0-9\-_/]+$"))
            { ShowError("Reference can only contain letters, numbers, hyphens, underscores, and slashes", false); return false; }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MESSAGE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private void ShowError(string message, bool isPersistent = false)
        {
            try
            {
                HideAllMessages();
                errorContainer.IsVisible = true;
                errorLabel.Text = message;
                if (!isPersistent)
                    Device.StartTimer(TimeSpan.FromSeconds(6), () =>
                    {
                        Device.BeginInvokeOnMainThread(() => errorContainer.IsVisible = false);
                        return false;
                    });
            }
            catch { }
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
            catch { }
        }

        private void HideAllMessages()
        {
            errorContainer.IsVisible = false;
            successContainer.IsVisible = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI STATE
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateCharacterCounter(int count)
        {
            try
            {
                characterCounter.Text = $"{count} chars";
                characterCounter.IsVisible = count > 0;
                characterCounter.TextColor =
                    count < MIN_REFERENCE_LENGTH ? Color.FromHex("#ff5722")
                    : count > MAX_REFERENCE_LENGTH ? Color.FromHex("#d32f2f")
                    : Color.FromHex("#4caf50");
            }
            catch { }
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
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EVENT HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private async void OnVerifyTapped(object sender, EventArgs e) => await VerifyPayment();
        private async void OnSearchCompleted(object sender, EventArgs e) => await VerifyPayment();

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                HideAllMessages();
                int len = e.NewTextValue?.Length ?? 0;
                UpdateCharacterCounter(len);
                entryContainer.BorderColor = len == 0
                    ? Color.FromHex("#004225")
                    : len < MIN_REFERENCE_LENGTH ? Color.FromHex("#ff5722")
                    : len > MAX_REFERENCE_LENGTH ? Color.FromHex("#d32f2f")
                    : Color.FromHex("#4caf50");
            }
            catch { }
        }

        private void OnEntryFocused(object sender, FocusEventArgs e)
        {
            try { entryContainer.BorderThickness = 3; entryContainer.Elevation = 4; UpdateRecentSearchesUI(); }
            catch { }
        }

        private void OnEntryUnfocused(object sender, FocusEventArgs e)
        {
            try { entryContainer.BorderThickness = 2; entryContainer.Elevation = 2; }
            catch { }
        }

        private void OnRecentSearchTapped(object sender, EventArgs e)
        {
            try
            {
                if (sender is View v && v.BindingContext is string reference)
                {
                    searchentry.Text = reference;
                    searchentry.Focus();
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CORE VERIFY LOGIC
        // ─────────────────────────────────────────────────────────────────────

        private async Task VerifyPayment()
        {
            if (_isVerifying) return;

            try
            {
                if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    ShowError("No internet connection. Please check your network settings.", false);
                    return;
                }

                string reference = searchentry.Text?.Trim();
                if (!ValidateReferenceNumber(reference)) return;

                SetVerifyingState(true);
                printReceiptButton.IsVisible = false;

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                string url = $"https://payyobe.com/api/v1/verify?InvoiceNumber=" +
                             Uri.EscapeDataString(reference);

                using (var response = await _httpClient.GetAsync(url, _cancellationTokenSource.Token))
                {
                    string json = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        if (string.IsNullOrWhiteSpace(json))
                        { ShowError("Empty response from server.", false); return; }

                        var result = JsonConvert.DeserializeObject<VerifyResponse>(json);

                        if (result?.IsValid() == true)
                        {
                            _currentResult = result;
                            _lastSearchReference = reference;
                            AddToRecentSearches(reference);
                            DisplayVerificationResult(result, reference);

                            try
                            {
                                if (string.Equals(result.status, "Successful",
                                    StringComparison.OrdinalIgnoreCase))
                                    HapticFeedback.Perform(HapticFeedbackType.Click);
                                else
                                    Vibration.Vibrate(TimeSpan.FromMilliseconds(200));
                            }
                            catch { }

                            if (!string.Equals(result.status, "Successful",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                await DisplayAlert("ℹ️ Payment Status",
                                    $"This invoice has a status of \"{result.status}\".\n\n" +
                                    "It has not been confirmed as paid yet.", "OK");
                            }
                        }
                        else
                        {
                            ShowError("No matching invoice found for this reference number.", false);
                        }
                    }
                    else
                    {
                        ShowError(GetHttpErrorMessage(response.StatusCode), false);
                    }
                }
            }
            catch (TaskCanceledException)
            { ShowError("Request timed out. Please try again.", false); }
            catch (HttpRequestException ex)
            {
                ShowError("Network error. Please check your connection.", false);
                System.Diagnostics.Debug.WriteLine($"[VerifyInvoice] HTTP: {ex.Message}");
            }
            catch (JsonException ex)
            {
                ShowError("Invalid response format from server.", false);
                System.Diagnostics.Debug.WriteLine($"[VerifyInvoice] JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowError("An unexpected error occurred. Please try again.", false);
                System.Diagnostics.Debug.WriteLine($"[VerifyInvoice] General: {ex.Message}");
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
                case HttpStatusCode.NotFound: return "Payment reference not found";
                case HttpStatusCode.BadRequest: return "Invalid request format";
                case HttpStatusCode.Unauthorized: return "Authentication required";
                case HttpStatusCode.Forbidden: return "Access denied";
                case HttpStatusCode.InternalServerError: return "Server error. Please try again later";
                case HttpStatusCode.ServiceUnavailable: return "Service temporarily unavailable";
                default: return $"Server error ({(int)statusCode}). Please try again";
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PRINT RECEIPT — uses App.PrintJobManager (same as Generate page)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Floating "Print Verification Receipt" button at the bottom of the
        /// result card.  Uses the same PrintJobManager pipeline as Generate.
        /// </summary>
        private async void OnPrintReceiptTapped(object sender, EventArgs e)
        {
            if (_currentResult == null) { ShowError("No invoice loaded to print.", false); return; }
            if (_isPrinting) return;

            printReceiptButton.IsEnabled = false;
            printReceiptButton.Text = "🖨️ Printing...";
            try { await AttemptPrintVerificationAsync(_currentResult, _lastSearchReference); }
            finally
            {
                printReceiptButton.IsEnabled = true;
                printReceiptButton.Text = "🖨️ Print Verification Receipt";
            }
        }

        /// <summary>
        /// The small blue PRINT tile inside the detail card — same pipeline.
        /// </summary>
        private async void OnPrintTapped(object sender, EventArgs e)
        {
            if (_currentResult == null) { ShowError("No invoice loaded.", false); return; }
            await AttemptPrintVerificationAsync(_currentResult, _lastSearchReference);
        }

        private async Task AttemptPrintVerificationAsync(VerifyResponse result, string reference)
        {
            if (_isPrinting) return;
            _isPrinting = true;
            try
            {
                bool granted = await BluetoothPermissionHelper.RequestAsync();
                if (!granted)
                {
                    Device.BeginInvokeOnMainThread(() =>
                        UserDialogs.Instance.Toast(
                            "Bluetooth permission denied. Enable it in Settings.",
                            TimeSpan.FromSeconds(6)));
                    return;
                }

                var receipt = BuildVerificationReceipt(result, reference);
                var job = await App.PrintJobManager.EnqueueAsync(
                    receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.ChunkStarted:
                                UserDialogs.Instance.Toast(
                                    $"Printing {p.ChunkName}…", TimeSpan.FromSeconds(2));
                                break;
                            case PrintProgressStatus.ChunkRetrying:
                                UserDialogs.Instance.Toast(
                                    $"Reconnecting… retry {p.ChunkName} (#{p.AttemptNumber})",
                                    TimeSpan.FromSeconds(2));
                                break;
                            case PrintProgressStatus.SessionCompleted:
                                UserDialogs.Instance.Toast(
                                    "Verification receipt printed!", TimeSpan.FromSeconds(4));
                                break;
                            case PrintProgressStatus.ChunkFailed:
                                UserDialogs.Instance.Toast(
                                    $"Print error on {p.ChunkName}. Tap Print to retry.",
                                    TimeSpan.FromSeconds(6));
                                break;
                        }
                    }));

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    try
                    {
                        await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                        await App.PrintJobManager.DeleteJobAsync(job.JobId);
                    }
                    catch (PrinterException pex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[VerifyInvoice] PrinterException: {pex.Message}");
                        Device.BeginInvokeOnMainThread(() =>
                            UserDialogs.Instance.Toast(
                                $"Print failed: {pex.Message}. Tap Print to retry.",
                                TimeSpan.FromSeconds(8)));
                    }
                    catch (OperationCanceledException)
                    {
                        Device.BeginInvokeOnMainThread(() =>
                            UserDialogs.Instance.Toast(
                                "Print timed out. Tap Print to try again.",
                                TimeSpan.FromSeconds(6)));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[VerifyInvoice] Print error: {ex.Message}");
                        Device.BeginInvokeOnMainThread(() =>
                            UserDialogs.Instance.Toast(
                                "Printer not connected. Pair printer first, then tap Print.",
                                TimeSpan.FromSeconds(8)));
                    }
                }
            }
            finally { _isPrinting = false; }
        }

        /// <summary>
        /// Builds a <see cref="ReceiptData"/> for a verification printout.
        /// Clearly states the payment status and verifying officer details.
        /// </summary>
        private ReceiptData BuildVerificationReceipt(VerifyResponse r, string reference)
        {
            bool isPaid = string.Equals(
                r.status, "Successful", StringComparison.OrdinalIgnoreCase);

            var items = new List<ReceiptItem>
            {
                new ReceiptItem { Description = "REFERENCE NO.",  Amount = 0m, SubText = Safe(reference, Safe(r.rrr, "N/A")) },
                new ReceiptItem { Description = "PAYER NAME",     Amount = 0m, SubText = Safe(r.payer_Name) },
                new ReceiptItem { Description = "PAYER PHONE",    Amount = 0m, SubText = Safe(r.payer_Phone) },
                new ReceiptItem { Description = "SERVICE / ITEM", Amount = 0m, SubText = Safe(r.service_Name) },
                new ReceiptItem { Description = "MDA",            Amount = 0m, SubText = Safe(r.mda) },
                new ReceiptItem { Description = "AMOUNT",         Amount = r.amount, SubText = null },
                new ReceiptItem
                {
                    Description = "DATE ISSUED",
                    Amount      = 0m,
                    SubText     = r.date_Generated.ToString("dd MMM yyyy")
                },
                new ReceiptItem
                {
                    Description = "DATE PAID",
                    Amount      = 0m,
                    SubText     = r.date_Paid.HasValue
                                      ? r.date_Paid.Value.ToString("dd MMM yyyy")
                                      : "NOT YET PAID"
                },
                // ── Status — most important line ───────────────────────────
                new ReceiptItem
                {
                    Description = isPaid
                                      ? "** STATUS: PAYMENT CONFIRMED **"
                                      : $"** STATUS: {Safe(r.status).ToUpper()} **",
                    Amount      = 0m,
                    SubText     = $"Verified: {DateTime.Now:dd MMM yyyy HH:mm}"
                },
                // ── Verifying officer ──────────────────────────────────────
                new ReceiptItem
                {
                    Description = "VERIFIED BY",
                    Amount      = 0m,
                    SubText     = Safe(MainPage.MyfullName, "Revenue Officer")
                },
                new ReceiptItem
                {
                    Description = "OFFICE / MDA",
                    Amount      = 0m,
                    SubText     = Safe(MainPage.mymda, "YIRS")
                }
            };

            return new ReceiptData
            {
                StoreName = "YOBE STATE REVENUE SERVICES [YIRS]",
                StorePhone = "Contact: +234 803 052 3208",
                ReceiptNumber = Safe(reference, Safe(r.rrr, "N/A")),
                AgentName = Safe(MainPage.MyfullName, "Revenue Officer"),
                CollectionPoint = Safe(MainPage.mymda, "YIRS"),
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = isPaid ? r.amount : 0m,
                FooterLine1 = isPaid
                                      ? "Payment confirmed. Keep this receipt."
                                      : "Invoice pending payment.",
                FooterLine2 = "POWERED BY OSOFTPAY",
                // BarcodeLabel = null → no QR on verification receipts
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  SHARE / SAVE
        // ─────────────────────────────────────────────────────────────────────

        private async void OnShareTapped(object sender, EventArgs e)
        {
            if (_currentResult == null) { ShowError("No invoice loaded.", false); return; }
            try
            {
                string shareText =
                    $"🧾 PAYMENT VERIFICATION RECEIPT\n" +
                    $"══════════════════════════════\n" +
                    $"👤 Payer:      {Safe(_currentResult.payer_Name)}\n" +
                    $"💰 Amount:    ₦{_currentResult.amount:N2}\n" +
                    $"📅 Date:       {_currentResult.date_Generated:MMM dd, yyyy}\n" +
                    $"🏛️ MDA:        {Safe(_currentResult.mda)}\n" +
                    $"📝 Service:    {Safe(_currentResult.service_Name)}\n" +
                    $"🔢 Reference:  {_lastSearchReference ?? Safe(_currentResult.rrr)}\n" +
                    $"✅ Status:     {Safe(_currentResult.status)}\n" +
                    $"⏰ Verified:   {DateTime.Now:MMM dd, yyyy 'at' hh:mm tt}\n" +
                    $"══════════════════════════════\n" +
                    $"🔒 Secured by Yobe State Revenue Services";

                await Share.RequestAsync(new ShareTextRequest
                {
                    Text = shareText,
                    Title = "Payment Verification Receipt"
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VerifyInvoice] Share: {ex.Message}");
                await DisplayAlert("Error", "Unable to share at this time.", "OK");
            }
        }

        private async void OnSaveTapped(object sender, EventArgs e)
        {
            if (_currentResult == null) { ShowError("No invoice loaded.", false); return; }
            try
            {
                var data = new
                {
                    PayerName = _currentResult.payer_Name,
                    Amount = _currentResult.amount,
                    Date = _currentResult.date_Generated,
                    MDA = _currentResult.mda,
                    ServiceName = _currentResult.service_Name,
                    Reference = _lastSearchReference ?? _currentResult.rrr,
                    Status = _currentResult.status,
                    SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                string key = $"Receipt_{_lastSearchReference}_{DateTime.Now:yyyyMMdd_HHmmss}";
                Preferences.Set(key, JsonConvert.SerializeObject(data));
                await DisplayAlert("💾 Saved", "Receipt saved locally for offline access.", "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VerifyInvoice] Save: {ex.Message}");
                await DisplayAlert("Error", "Unable to save receipt.", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CheckConnectivity();
            Device.BeginInvokeOnMainThread(() =>
            {
                if (string.IsNullOrEmpty(searchentry.Text))
                    searchentry.Focus();
            });
        }

        protected override void OnDisappearing()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            }
            catch { }
            finally { base.OnDisappearing(); }
        }

        ~VerifyInvoice()
        {
            try { _httpClient?.Dispose(); _cancellationTokenSource?.Dispose(); } catch { }
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            try
            {
                if (Navigation.NavigationStack.Count > 1)
                    await Navigation.PopAsync();
                else
                    await Navigation.PopModalAsync();
            }
            catch { }
        }
    }
}