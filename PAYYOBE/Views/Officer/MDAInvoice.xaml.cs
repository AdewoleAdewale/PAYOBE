using Acr.UserDialogs;
using Newtonsoft.Json;
using PAYYOBE.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PancakeView;
using Xamarin.Forms.Xaml;
using static Android.Media.Browse.MediaBrowser;

namespace PAYYOBE.Views.Officer
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MDAInvoice : ContentPage
    {
        private readonly HttpClient _client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true }) { Timeout = TimeSpan.FromSeconds(30) };
        private List<MdaItem> _mdaCacheList = new List<MdaItem>();
        private List<MdaServiceItem> _serviceCacheList = new List<MdaServiceItem>();

        private readonly Dictionary<string, bool> _validationMap = new Dictionary<string, bool>
        {
            ["Name"] = false,
            ["Phone"] = false,
            ["Email"] = false,
            ["Mda"] = false,
            ["Service"] = false,
            ["Amount"] = false
        };

        private MdaInvoiceResponse _latestInvoiceResult;

        public MDAInvoice()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                HeaderOfficerName.Text = $"Officer: {MainPage.OfficerName}";
                HeaderOfficerCode.Text = $"System Code Context: {MainPage.OfficerCode}";
                await DownloadMdaDropdownDatasetAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDAInvoice] OnAppearing Exception: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  DYNAMIC ASSIGNED DATASET DROPDOWN DOWNLOAD RETRIEVALS
        // ─────────────────────────────────────────────────────────────────

        private async Task DownloadMdaDropdownDatasetAsync()
        {
            try
            {
                PickerMda.Title = "⏳ Synchronizing Government MDAs...";
                PickerMda.IsEnabled = false;

                string password = await SecureStorage.GetAsync("OfficerPassword") ?? string.Empty;
                var payload = new { Email = MainPage.OfficerEmail, Password = password };

                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://payyobe.com/api/v1/GetMDAs"))
                {
                    request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await _client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        ThrowManagedFault($"Failed to sync structural MDA registries from the YIRS root gateway node. Status: {(int)response.StatusCode}");
                        return;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    var wrapper = JsonConvert.DeserializeObject<MdaListContainer>(json);

                    if (wrapper == null || wrapper.data == null || !wrapper.data.Any())
                    {
                        ThrowManagedFault("The MDA infrastructure dataset returned empty.");
                        return;
                    }

                    _mdaCacheList = wrapper.data.OrderBy(x => x.mdaName).ToList();
                    PickerMda.ItemsSource = _mdaCacheList.Select(x => x.mdaName).ToList();
                    PickerMda.Title = "Select Target MDA Instance";
                    PickerMda.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                ThrowManagedFault($"Network Gateway Synchronization Exception: {ex.Message}");
            }
        }

        private async void OnMdaSelectionChanged(object sender, EventArgs e)
        {
            if (PickerMda.SelectedIndex < 0) return;

            MdaError.IsVisible = false;
            MdaFrame.BorderColor = Color.FromHex("#06B6D4");
            _validationMap["Mda"] = true;
            UpdateValidationProgressStatusUI();

            PickerService.IsEnabled = false;
            PickerService.ItemsSource = null;
            PickerService.Title = "⏳ Downloading Associated MDA Services...";
            _validationMap["Service"] = false;

            try
            {
                var selectedMda = _mdaCacheList[PickerMda.SelectedIndex];
                string password = await SecureStorage.GetAsync("OfficerPassword") ?? string.Empty;
                var payload = new { Email = MainPage.OfficerEmail, Password = password };

                using (var request = new HttpRequestMessage(HttpMethod.Get, $"https://payyobe.com/api/v1/GetServicesByMDA/{selectedMda.id}"))
                {
                    request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await _client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        ThrowManagedFault($"Unable to load services assigned to the selected corporate MDA node. Status: {(int)response.StatusCode}");
                        return;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    var wrapper = JsonConvert.DeserializeObject<MdaServiceListContainer>(json);

                    if (wrapper == null || wrapper.data == null || !wrapper.data.Any())
                    {
                        PickerService.Title = "⚠️ No Services Configured For This MDA";
                        return;
                    }

                    _serviceCacheList = wrapper.data.OrderBy(x => x.serviceName).ToList();
                    PickerService.ItemsSource = _serviceCacheList.Select(x => x.serviceName).ToList();
                    PickerService.Title = "Select Associated Revenue Service";
                    PickerService.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                ThrowManagedFault($"Cascading Service Compilation Fault: {ex.Message}");
            }
        }

        private void OnServiceSelectionChanged(object sender, EventArgs e)
        {
            if (PickerService.SelectedIndex < 0) return;
            ServiceError.IsVisible = false;
            ServiceFrame.BorderColor = Color.FromHex("#06B6D4");
            _validationMap["Service"] = true;
            UpdateValidationProgressStatusUI();
        }

        // ─────────────────────────────────────────────────────────────────
        //  VALIDATION PIPELINES & FORM TRACKING
        // ─────────────────────────────────────────────────────────────────

        private void OnFullNameTextChanged(object sender, TextChangedEventArgs e) => EvaluateLiveField(() => !string.IsNullOrWhiteSpace(e.NewTextValue) && e.NewTextValue.Trim().Length >= 3, "Name");
        private void OnPhoneTextChanged(object sender, TextChangedEventArgs e) => EvaluateLiveField(() => !string.IsNullOrWhiteSpace(e.NewTextValue) && Regex.IsMatch(e.NewTextValue.Trim(), @"^\d{10,13}$"), "Phone");
        private void OnEmailTextChanged(object sender, TextChangedEventArgs e) => EvaluateLiveField(() => !string.IsNullOrWhiteSpace(e.NewTextValue) && Regex.IsMatch(e.NewTextValue.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"), "Email");
        private void OnAmountTextChanged(object sender, TextChangedEventArgs e) => EvaluateLiveField(() => decimal.TryParse(e.NewTextValue, out decimal val) && val > 0, "Amount");

        private void EvaluateLiveField(Func<bool> checkCondition, string fieldKey)
        {
            try
            {
                _validationMap[fieldKey] = checkCondition();
                UpdateValidationProgressStatusUI();
            }
            catch { }
        }

        private void OnFullNameUnfocused(object sender, FocusEventArgs e) => RenderFieldStateVisualFeedback(FullNameFrame, FullNameError, _validationMap["Name"], "Valid full name parameter required.");
        private void OnPhoneUnfocused(object sender, FocusEventArgs e) => RenderFieldStateVisualFeedback(PhoneFrame, PhoneError, _validationMap["Phone"], "Provide a valid 10-13 digit contact configuration.");
        private void OnEmailUnfocused(object sender, FocusEventArgs e) => RenderFieldStateVisualFeedback(EmailFrame, EmailError, _validationMap["Email"], "Standard format validation entry required.");
        private void OnAmountUnfocused(object sender, FocusEventArgs e) => RenderFieldStateVisualFeedback(AmountFrame, AmountError, _validationMap["Amount"], "Value must correspond to a positive non-zero matrix.");

        // FIXED: The yummy prefix is removed. Uses fully qualified class layout types to resolve compiler errors
        private void RenderFieldStateVisualFeedback(Xamarin.Forms.PancakeView.PancakeView layout, Label messageNode, bool isValid, string diagnostic)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (layout != null && messageNode != null)
                {
                    layout.BorderColor = isValid ? Color.FromHex("#06B6D4") : Color.FromHex("#D94040");
                    messageNode.Text = diagnostic;
                    messageNode.IsVisible = !isValid;
                }
            });
        }

        private void UpdateValidationProgressStatusUI()
        {
            try
            {
                int completedCount = _validationMap.Values.Count(v => v);
                Device.BeginInvokeOnMainThread(() =>
                {
                    LblProgressText.Text = $"{completedCount}/6 fields completed";
                    LblProgressText.TextColor = completedCount == 6 ? Color.FromHex("#06B6D4") : Color.FromHex("#D94040");

                    UpdateLifecycleIndicatorDots(completedCount);

                    bool fullyCleared = completedCount == 6;
                    BtnGenerate.IsEnabled = fullyCleared;
                    BtnGenerate.Opacity = fullyCleared ? 1.0 : 0.45;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDAInvoice] Progress UI Update Fault: {ex.Message}");
            }
        }

        private void UpdateLifecycleIndicatorDots(int total)
        {
            if (Step1Dot == null) return;
            Step1Dot.Color = total >= 1 ? Color.FromHex("#06B6D4") : Color.FromHex("#CBD5E1");
            Step2Dot.Color = total >= 2 ? Color.FromHex("#06B6D4") : Color.FromHex("#CBD5E1");
            Step3Dot.Color = total >= 3 ? Color.FromHex("#06B6D4") : Color.FromHex("#CBD5E1");
            Step4Dot.Color = total >= 4 ? Color.FromHex("#06B6D4") : Color.FromHex("#CBD5E1");
            Step5Dot.Color = total >= 5 ? Color.FromHex("#06B6D4") : Color.FromHex("#CBD5E1");
            Step6Dot.Color = total >= 6 ? Color.FromHex("#06B6D4") : Color.FromHex("#CBD5E1");
        }

        // ─────────────────────────────────────────────────────────────────
        //  POST INVOICE TRANSACTION INTEGRATION PIPELINE
        // ─────────────────────────────────────────────────────────────────

        private async void OnGenerateInvoiceClicked(object sender, EventArgs e)
        {
            if (_validationMap.Values.Any(v => !v)) return;

            SetExecutionLoadingState(true);

            try
            {
                var selectedMda = _mdaCacheList[PickerMda.SelectedIndex];
                var selectedService = _serviceCacheList[PickerService.SelectedIndex];
                decimal.TryParse(TxtAmount.Text.Trim(), out decimal numericalAmount);

                var payload = new
                {
                    payer_name = TxtFullName.Text.Trim(),
                    payer_email = TxtEmail.Text.Trim(),
                    payer_phone = TxtPhone.Text.Trim(),
                    OfficerId = MainPage.OfficerId,
                    MDA_Id = selectedMda.id,
                    Service_Name = selectedService.serviceName,
                    Amount = (double)numericalAmount
                };

                string payloadSerialized = JsonConvert.SerializeObject(payload);
                using (var stringContent = new StringContent(payloadSerialized, Encoding.UTF8, "application/json"))
                {
                    var response = await _client.PostAsync("https://payyobe.com/api/v1/generate-invoice", stringContent);
                    string rawResponseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        ThrowManagedFault($"Server returned remote failure endpoint exception context: {(int)response.StatusCode}");
                        return;
                    }

                    var result = JsonConvert.DeserializeObject<MdaInvoiceResponse>(rawResponseJson);

                    if (result != null && result.statusCode == "00")
                    {
                        _latestInvoiceResult = result;

                        LblResponseRrr.Text = result.rrr;
                        LblSuccessPayer.Text = result.payer_name ?? payload.payer_name;
                        LblSuccessService.Text = result.service ?? payload.Service_Name;
                        LblSuccessAmount.Text = $"₦{result.amount:N0}";
                        LblSuccessOrderId.Text = result.orderId;

                        await PresentInteractiveOverlayAsync(SuccessSheet);
                    }
                    else
                    {
                        ThrowManagedFault(result?.message ?? "The collection validation engine returned a processing mismatch rejection.");
                    }
                }
            }
            catch (Exception ex)
            {
                ThrowManagedFault($"Critical Core Engine Generation Interrupt: {ex.Message}");
            }
            finally
            {
                SetExecutionLoadingState(false);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  THERMAL RECEIPT SYSTEM BRIDGE ADAPTATION
        // ─────────────────────────────────────────────────────────────────

        private async void OnPrintReceiptClicked(object sender, EventArgs e)
        {
            if (_latestInvoiceResult == null) return;

            try
            {
                using (UserDialogs.Instance.Loading("Connecting to Thermal Bluetooth Peripheral..."))
                {
                    var items = new List<ReceiptItem>
                    {
                        new ReceiptItem { Description = "INVOICE REFERENCE RRR", Amount = 0m, SubText = _latestInvoiceResult.rrr },
                        new ReceiptItem { Description = "Assigned Service Item", Amount = 0m, SubText = _latestInvoiceResult.service },
                        new ReceiptItem { Description = "Payer Full Name", Amount = 0m, SubText = LblSuccessPayer.Text },
                        new ReceiptItem { Description = "Order Record System Link ID", Amount = 0m, SubText = _latestInvoiceResult.orderId },
                        new ReceiptItem { Description = "Total Balance Due", Amount = (decimal)_latestInvoiceResult.amount }
                    };

                    var receiptPayload = new ReceiptData
                    {
                        StoreName = "YOBE STATE REVENUE SERVICES [YIRS]",
                        StorePhone = "Officer Node Terminal Administration",
                        ReceiptNumber = _latestInvoiceResult.rrr,
                        AgentName = MainPage.OfficerName,
                        CollectionPoint = PickerMda.SelectedItem?.ToString() ?? "YIRS Office",
                        PrintDate = DateTime.Now,
                        Items = items,
                        AmountPaid = 0m,
                        FooterLine1 = "Present Remita reference generation slip code at bank window counters.",
                        FooterLine2 = "POWERED BY OSOFTPAY SYSTEM NETWORK"
                    };

                    var targetPrintJob = await App.PrintJobManager.EnqueueAsync(receiptPayload, "Logo.png");
                    var executionTokenSource = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45));

                    await App.PrintJobManager.ExecuteAsync(targetPrintJob.JobId, new Progress<PrintProgress>(), executionTokenSource.Token);
                    UserDialogs.Instance.Toast("Receipt sequence dispatched successfully.");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hardware Connection Timeout", $"Thermal print link pipeline reported an error state context loop: {ex.Message}", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  UI OVERLAY MANAGEMENT ARCHITECTURE
        // ─────────────────────────────────────────────────────────────────

        private void ThrowManagedFault(string failureText)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                LblErrorDescription.Text = failureText;
                _ = PresentInteractiveOverlayAsync(ErrorSheet);
            });
        }

        // FIXED: Replaced invalid XAML parameter shorthand template tokens with accurate runtime signatures
        private async Task PresentInteractiveOverlayAsync(Xamarin.Forms.PancakeView.PancakeView activeSheet)
        {
            try
            {
                if (SheetOverlay == null || activeSheet == null) return;

                SheetOverlay.IsVisible = true;
                activeSheet.IsVisible = true;
                activeSheet.TranslationY = 1000;
                await activeSheet.TranslateTo(0, 0, 300);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDAInvoice] Sheet Presentation Exception: {ex.Message}");
            }
        }

        private async void OnDismissSheetClicked(object sender, EventArgs e) => await DismissActiveOverlaySheetsAsync();
        private async void OnDismissOverlayTapped(object sender, EventArgs e) => await DismissActiveOverlaySheetsAsync();

        private async Task DismissActiveOverlaySheetsAsync()
        {
            try
            {
                await Task.WhenAll(
                    SuccessSheet.TranslateTo(0, 1000, 250, Easing.Linear),
                    ErrorSheet.TranslateTo(0, 1000, 250, Easing.Linear)
                );
                SuccessSheet.IsVisible = false;
                ErrorSheet.IsVisible = false;
                SheetOverlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDAInvoice] Sheet Dismissal Exception: {ex.Message}");
                SuccessSheet.IsVisible = false;
                ErrorSheet.IsVisible = false;
                SheetOverlay.IsVisible = false;
            }
        }

        private async void OnCopyRrrTapped(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LblResponseRrr.Text)) return;
                await Clipboard.SetTextAsync(LblResponseRrr.Text);
                UserDialogs.Instance.Toast("Remita RRR reference string code copied.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDAInvoice] Copy Operations Error: {ex.Message}");
            }
        }

        private void SetExecutionLoadingState(bool loading)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                BtnLoadingIndicator.IsRunning = loading;
                BtnLoadingIndicator.IsVisible = loading;
                BtnLabel.Text = loading ? "COMPILING ASSESSMENT DATA..." : "GENERATE SYSTEM INVOICE";
                MainFormContainer.IsEnabled = !loading;
            });
        }

        private async void OnBackTapped(object sender, EventArgs e) => await Navigation.PopAsync();

      
    }

    public class MdaListContainer { public string statusCode { get; set; } public string message { get; set; } public List<MdaItem> data { get; set; } }
    public class MdaItem { public int id { get; set; } public string mdaName { get; set; } }

    public class MdaServiceListContainer { public string statusCode { get; set; } public string message { get; set; } public List<MdaServiceItem> data { get; set; } }
    public class MdaServiceItem { public string serviceId { get; set; } public string serviceName { get; set; } public string serviceType { get; set; } }

    public class MdaInvoiceResponse
    {
        public string statusCode { get; set; }
        public string message { get; set; }
        public int paymentId { get; set; }
        public string rrr { get; set; }
        public string orderId { get; set; }
        public double amount { get; set; }
        public string service { get; set; }
        public string status { get; set; }
        public string payer_name { get; set; } // Map fallback if assigned implicitly
    }
}